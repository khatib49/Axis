using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.TillControls
{
    /// <summary>
    /// Void/refund approvals. Approving a Void delegates to the existing
    /// ITransactionRecordService.DeleteAsync (restores stock + audits). Approving a
    /// Refund records a PayOut on the approver's open shift.
    /// </summary>
    public class VoidRequestService : IVoidRequestService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ITransactionRecordService _transactions;
        private readonly ILogger<VoidRequestService> _log;

        public VoidRequestService(ApplicationDbContext db, ICurrentUserAccessor user,
            ITransactionRecordService transactions, ILogger<VoidRequestService> log)
        {
            _db = db;
            _user = user;
            _transactions = transactions;
            _log = log;
        }

        // ── Reasons ─────────────────────────────────────────────
        public async Task<List<VoidReasonDto>> ListReasonsAsync(bool includeInactive = false, CancellationToken ct = default)
        {
            var q = _db.Set<VoidReason>().AsNoTracking();
            if (!includeInactive) q = q.Where(r => r.IsActive);
            return (await q.OrderBy(r => r.Name).ToListAsync(ct)).Select(ToDto).ToList();
        }

        public async Task<BaseResponse<VoidReasonDto>> CreateReasonAsync(VoidReasonCreateDto dto, CancellationToken ct = default)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            if (name.Length == 0) return Fail<VoidReasonDto>("Name is required.");
            if (await _db.Set<VoidReason>().AnyAsync(r => r.Name == name, ct)) return Fail<VoidReasonDto>("A reason with that name already exists.");
            var e = new VoidReason { Name = name, RequiresApproval = dto.RequiresApproval, IsActive = dto.IsActive };
            _db.Set<VoidReason>().Add(e);
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(e), "Reason created.");
        }

        public async Task<BaseResponse<VoidReasonDto>> UpdateReasonAsync(int id, VoidReasonUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _db.Set<VoidReason>().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (e is null) return Fail<VoidReasonDto>("Reason not found.");
            if (dto.Name != null)
            {
                var name = dto.Name.Trim();
                if (name.Length == 0) return Fail<VoidReasonDto>("Name is required.");
                if (await _db.Set<VoidReason>().AnyAsync(r => r.Name == name && r.Id != id, ct)) return Fail<VoidReasonDto>("A reason with that name already exists.");
                e.Name = name;
            }
            if (dto.RequiresApproval.HasValue) e.RequiresApproval = dto.RequiresApproval.Value;
            if (dto.IsActive.HasValue) e.IsActive = dto.IsActive.Value;
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(e), "Reason updated.");
        }

        public async Task<bool> DeleteReasonAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<VoidReason>().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (e is null) return false;
            var used = await _db.Set<VoidRequest>().AnyAsync(v => v.ReasonId == id, ct);
            if (used) { e.IsActive = false; } // keep history intact; soft-disable instead
            else _db.Set<VoidReason>().Remove(e);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ── Requests ────────────────────────────────────────────
        public async Task<BaseResponse<TillVoidRequestDto>> CreateAsync(VoidRequestCreateDto dto, CancellationToken ct = default)
        {
            var type = NormalizeType(dto.Type);
            if (type is null) return Fail<TillVoidRequestDto>("Type must be 'Void' or 'Refund'.");
            var reason = await _db.Set<VoidReason>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.ReasonId, ct);
            if (reason is null || !reason.IsActive) return Fail<TillVoidRequestDto>("Reason not found or inactive.");
            var tx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TransactionId, ct);
            if (tx is null) return Fail<TillVoidRequestDto>($"Transaction #{dto.TransactionId} not found.");
            if (dto.Amount.HasValue && dto.Amount.Value <= 0) return Fail<TillVoidRequestDto>("Amount must be greater than zero.");
            if (type == VoidRequestTypes.Refund && dto.Amount.HasValue && dto.Amount.Value > tx.TotalPrice)
                return Fail<TillVoidRequestDto>($"Refund amount exceeds the transaction total ({tx.TotalPrice:0.00}).");
            var pendingExists = await _db.Set<VoidRequest>().AnyAsync(v => v.TransactionId == dto.TransactionId && v.Status == VoidRequestStatuses.Pending, ct);
            if (pendingExists) return Fail<TillVoidRequestDto>("A pending request already exists for this transaction.");

            var e = new VoidRequest
            {
                TransactionId = dto.TransactionId,
                Type = type,
                Amount = dto.Amount ?? tx.TotalPrice,
                ReasonId = reason.Id,
                ReasonText = dto.ReasonText?.Trim(),
                RequestedBy = _user.UserName,
                RequestedOn = DateTime.UtcNow,
                Status = VoidRequestStatuses.Pending,
                BranchId = _user.BranchId,
            };
            _db.Set<VoidRequest>().Add(e);
            await _db.SaveChangesAsync(ct);

            // Reasons that don't need a second pair of eyes approve themselves (voids only).
            if (type == VoidRequestTypes.Void && !reason.RequiresApproval)
                return await ApproveAsync(e.Id, new VoidDecisionDto("Auto-approved: reason does not require approval."), ct);

            return Ok(ToDto(e, reason), "Request submitted for approval.");
        }

        public async Task<PaginatedResponse<TillVoidRequestDto>> ListAsync(VoidRequestFilterDto filter, CancellationToken ct = default)
        {
            var q = _db.Set<VoidRequest>().AsNoTracking().Include(v => v.Reason).AsQueryable();
            if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(v => v.Status == filter.Status.Trim());
            if (filter.TransactionId.HasValue) q = q.Where(v => v.TransactionId == filter.TransactionId.Value);
            var page = Math.Max(1, filter.Page);
            var size = Math.Clamp(filter.PageSize, 1, 200);
            var total = await q.CountAsync(ct);
            var list = await q.OrderByDescending(v => v.RequestedOn).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return new PaginatedResponse<TillVoidRequestDto>(total, list.Select(ToDto).ToList(), page, size);
        }

        public async Task<List<TillVoidRequestDto>> ListPendingAsync(CancellationToken ct = default)
        {
            var list = await _db.Set<VoidRequest>().AsNoTracking().Include(v => v.Reason)
                .Where(v => v.Status == VoidRequestStatuses.Pending)
                .OrderBy(v => v.RequestedOn).ToListAsync(ct);
            return list.Select(ToDto).ToList();
        }

        public async Task<TillVoidRequestDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<VoidRequest>().AsNoTracking().Include(v => v.Reason).FirstOrDefaultAsync(v => v.Id == id, ct);
            return e is null ? null : ToDto(e);
        }

        public async Task<BaseResponse<TillVoidRequestDto>> ApproveAsync(int id, VoidDecisionDto dto, CancellationToken ct = default)
        {
            var e = await _db.Set<VoidRequest>().Include(v => v.Reason).FirstOrDefaultAsync(v => v.Id == id, ct);
            if (e is null) return Fail<TillVoidRequestDto>("Request not found.");
            if (e.Status != VoidRequestStatuses.Pending) return Fail<TillVoidRequestDto>($"Request is already {e.Status}.");

            string note;
            if (e.Type == VoidRequestTypes.Void)
            {
                var deleted = await _transactions.DeleteAsync(e.TransactionId, ct);
                if (!deleted) return Fail<TillVoidRequestDto>($"Transaction #{e.TransactionId} no longer exists; nothing to void.");
                note = $"Transaction #{e.TransactionId} voided (stock restored).";
            }
            else
            {
                var userId = _user.UserId;
                var shift = userId is null ? null : await _db.Set<CashierShift>()
                    .Where(s => s.UserId == userId.Value && s.Status == ShiftStatuses.Open)
                    .OrderByDescending(s => s.OpenedOn).FirstOrDefaultAsync(ct);
                if (shift is null) return Fail<TillVoidRequestDto>("Open a shift before approving a refund: the refund is paid out of your drawer.");
                var amount = e.Amount ?? 0m;
                if (amount <= 0) return Fail<TillVoidRequestDto>("Refund amount is missing.");
                _db.Set<CashMovement>().Add(new CashMovement
                {
                    ShiftId = shift.Id,
                    Type = CashMovementTypes.PayOut,
                    Amount = amount,
                    Reason = $"Refund #{e.Id}",
                    CreatedOn = DateTime.UtcNow,
                    CreatedBy = _user.UserName,
                });
                note = $"Refund of {amount:0.00} paid out of shift #{shift.Id}.";
            }

            e.Status = VoidRequestStatuses.Approved;
            e.DecidedBy = _user.UserName;
            e.DecidedOn = DateTime.UtcNow;
            e.DecisionNote = string.IsNullOrWhiteSpace(dto?.Note) ? note : $"{dto!.Note!.Trim()} — {note}";
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("{Type} request {Id} for tx {Tx} approved by {User}", e.Type, e.Id, e.TransactionId, _user.UserName);
            return Ok(ToDto(e), "Request approved.");
        }

        public async Task<BaseResponse<TillVoidRequestDto>> RejectAsync(int id, VoidDecisionDto dto, CancellationToken ct = default)
        {
            var e = await _db.Set<VoidRequest>().Include(v => v.Reason).FirstOrDefaultAsync(v => v.Id == id, ct);
            if (e is null) return Fail<TillVoidRequestDto>("Request not found.");
            if (e.Status != VoidRequestStatuses.Pending) return Fail<TillVoidRequestDto>($"Request is already {e.Status}.");
            e.Status = VoidRequestStatuses.Rejected;
            e.DecidedBy = _user.UserName;
            e.DecidedOn = DateTime.UtcNow;
            e.DecisionNote = dto?.Note?.Trim();
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(e), "Request rejected.");
        }

        private static string? NormalizeType(string? type)
        {
            if (string.Equals(type, VoidRequestTypes.Void, StringComparison.OrdinalIgnoreCase)) return VoidRequestTypes.Void;
            if (string.Equals(type, VoidRequestTypes.Refund, StringComparison.OrdinalIgnoreCase)) return VoidRequestTypes.Refund;
            return null;
        }

        private static VoidReasonDto ToDto(VoidReason r) => new(r.Id, r.Name, r.RequiresApproval, r.IsActive);
        private static TillVoidRequestDto ToDto(VoidRequest v) => ToDto(v, v.Reason);
        private static TillVoidRequestDto ToDto(VoidRequest v, VoidReason? reason) => new(
            v.Id, v.TransactionId, v.Type, v.Amount, v.ReasonId, reason?.Name ?? string.Empty,
            reason?.RequiresApproval ?? true, v.ReasonText, v.RequestedBy, v.RequestedOn, v.Status,
            v.DecidedBy, v.DecidedOn, v.DecisionNote, v.BranchId);
        private static BaseResponse<T> Fail<T>(string msg) => new(false, msg, msg);
        private static BaseResponse<T> Ok<T>(T data, string msg) => new(true, null, msg, data);
    }
}
