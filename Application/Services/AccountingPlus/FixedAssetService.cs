using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.AccountingPlus
{
    public class FixedAssetService : IFixedAssetService
    {
        private static readonly string[] Methods = { "StraightLine", "DecliningBalance" };

        private readonly ApplicationDbContext _db;
        private readonly IJournalService _journal;
        private readonly IAccountingPeriodService _periods;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<FixedAssetService> _logger;

        public FixedAssetService(
            ApplicationDbContext db,
            IJournalService journal,
            IAccountingPeriodService periods,
            ICurrentUserAccessor user,
            ILogger<FixedAssetService> logger)
        {
            _db = db;
            _journal = journal;
            _periods = periods;
            _user = user;
            _logger = logger;
        }

        // ============================================
        // CRUD
        // ============================================

        public async Task<List<FixedAssetDto>> ListAsync(string? status, CancellationToken ct = default)
        {
            var q = _db.Set<FixedAsset>().AsNoTracking().Include(a => a.DepreciationEntries).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
            var rows = await q.OrderBy(a => a.Name).ToListAsync(ct);
            return rows.Select(ToDto).ToList();
        }

        public async Task<BaseResponse<FixedAssetDto>> GetAsync(int id, CancellationToken ct = default)
        {
            var a = await _db.Set<FixedAsset>().AsNoTracking().Include(x => x.DepreciationEntries).FirstOrDefaultAsync(x => x.Id == id, ct);
            return a is null ? new BaseResponse<FixedAssetDto>(false, "Asset not found", "") : new BaseResponse<FixedAssetDto>(true, null, "", ToDto(a));
        }

        public async Task<BaseResponse<FixedAssetDto>> CreateAsync(FixedAssetUpsertDto dto, CancellationToken ct = default)
        {
            var err = await ValidateAsync(dto, ct);
            if (err is not null) return new BaseResponse<FixedAssetDto>(false, err, "");
            var a = new FixedAsset { CreatedOn = DateTime.UtcNow, BranchId = _user.BranchId, Status = "Active" };
            Apply(a, dto);
            _db.Add(a);
            await _db.SaveChangesAsync(ct);
            return await GetAsync(a.Id, ct);
        }

        public async Task<BaseResponse<FixedAssetDto>> UpdateAsync(int id, FixedAssetUpsertDto dto, CancellationToken ct = default)
        {
            var a = await _db.Set<FixedAsset>().Include(x => x.DepreciationEntries).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return new BaseResponse<FixedAssetDto>(false, "Asset not found", "");
            if (a.Status == "Disposed") return new BaseResponse<FixedAssetDto>(false, "Disposed assets cannot be edited", "");
            var err = await ValidateAsync(dto, ct);
            if (err is not null) return new BaseResponse<FixedAssetDto>(false, err, "");
            if (a.DepreciationEntries.Count > 0 && (a.Cost != dto.Cost || a.SalvageValue != dto.SalvageValue || a.UsefulLifeMonths != dto.UsefulLifeMonths
                                                   || !a.Method.Equals(dto.Method, StringComparison.OrdinalIgnoreCase) || a.AcquiredOn.Date != AcctDates.Utc(dto.AcquiredOn).Date))
                return new BaseResponse<FixedAssetDto>(false, "Cost, salvage, life, method and acquisition date are frozen once depreciation has been posted", "");
            Apply(a, dto);
            await _db.SaveChangesAsync(ct);
            return await GetAsync(a.Id, ct);
        }

        public async Task<BaseResponse> DeleteAsync(int id, CancellationToken ct = default)
        {
            var a = await _db.Set<FixedAsset>().Include(x => x.DepreciationEntries).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return new BaseResponse(false, "Asset not found");
            if (a.DepreciationEntries.Count > 0 || a.Status == "Disposed") return new BaseResponse(false, "Assets with postings cannot be deleted; dispose them instead");
            _db.Remove(a);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse(true, null);
        }

        // ============================================
        // DEPRECIATION
        // ============================================

        public async Task<BaseResponse<List<DepreciationScheduleRowDto>>> GetScheduleAsync(int id, CancellationToken ct = default)
        {
            var a = await _db.Set<FixedAsset>().AsNoTracking().Include(x => x.DepreciationEntries).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return new BaseResponse<List<DepreciationScheduleRowDto>>(false, "Asset not found", "");
            var posted = a.DepreciationEntries.Select(d => (d.PeriodYear, d.PeriodMonth)).ToHashSet();
            var rows = DepreciationSchedule.Build(a.AcquiredOn, a.Cost, a.SalvageValue, a.UsefulLifeMonths, a.Method)
                .Select(r => new DepreciationScheduleRowDto(r.Year, r.Month, r.Amount, r.AccumulatedAfter, r.NetBookValueAfter, posted.Contains((r.Year, r.Month))))
                .ToList();
            return new BaseResponse<List<DepreciationScheduleRowDto>>(true, null, "", rows);
        }

        public async Task<List<DepreciationEntryDto>> GetEntriesAsync(int id, CancellationToken ct = default)
        {
            var rows = await _db.Set<DepreciationEntry>().AsNoTracking().Where(d => d.AssetId == id)
                .OrderBy(d => d.PeriodYear).ThenBy(d => d.PeriodMonth).ToListAsync(ct);
            return rows.Select(d => new DepreciationEntryDto(d.Id, d.AssetId, d.PeriodYear, d.PeriodMonth, d.Amount, d.JournalEntryId, d.PostedOn, d.PostedBy)).ToList();
        }

        public async Task<BaseResponse<DepreciationRunResultDto>> RunDepreciationAsync(int year, int month, int? assetId, CancellationToken ct = default)
        {
            if (month < 1 || month > 12 || year < 2000 || year > 2100)
                return new BaseResponse<DepreciationRunResultDto>(false, "Invalid year/month", "");

            var periodEnd = AcctDates.MonthEnd(year, month);
            var open = await _periods.EnsureOpenAsync(periodEnd, ct);
            if (!open.Success) return new BaseResponse<DepreciationRunResultDto>(false, open.Error, "");

            var q = _db.Set<FixedAsset>().Include(a => a.DepreciationEntries).Where(a => a.Status == "Active" && a.AcquiredOn <= periodEnd);
            if (assetId.HasValue) q = q.Where(a => a.Id == assetId.Value);
            var assets = await q.ToListAsync(ct);

            var messages = new List<string>();
            int posted = 0, skipped = 0;
            decimal total = 0;
            foreach (var a in assets)
            {
                if (a.DepreciationEntries.Any(d => d.PeriodYear == year && d.PeriodMonth == month))
                {
                    skipped++;
                    messages.Add($"{a.Name}: already posted for {year}-{month:00}");
                    continue;
                }
                var row = DepreciationSchedule.Build(a.AcquiredOn, a.Cost, a.SalvageValue, a.UsefulLifeMonths, a.Method)
                    .FirstOrDefault(r => r.Year == year && r.Month == month);
                if (row is null || row.Amount <= 0)
                {
                    skipped++;
                    messages.Add($"{a.Name}: nothing to depreciate for {year}-{month:00}");
                    continue;
                }

                var je = await _journal.CreateJournalEntryAsync(new JournalEntryCreateDto(
                    periodEnd.Date, $"Depreciation {year}-{month:00}: {a.Name}", "Depreciation", a.Id,
                    new List<JournalEntryLineCreateDto>
                    {
                        new(a.DepExpenseGlAccountId, row.Amount, 0, $"Depreciation {a.Name}"),
                        new(a.AccumulatedDepGlAccountId, 0, row.Amount, $"Accumulated depreciation {a.Name}"),
                    }), _user.UserId, ct);
                if (!je.Success || je.Data is null)
                {
                    messages.Add($"{a.Name}: journal failed - {je.Error}");
                    continue;
                }
                var post = await _journal.PostJournalEntryAsync(je.Data.Id, _user.UserId, ct);
                if (!post.Success)
                {
                    messages.Add($"{a.Name}: posting failed - {post.Error}");
                    continue;
                }

                a.DepreciationEntries.Add(new DepreciationEntry
                {
                    PeriodYear = year, PeriodMonth = month, Amount = row.Amount, JournalEntryId = je.Data.Id,
                    PostedOn = DateTime.UtcNow, PostedBy = _user.UserName,
                });
                await _db.SaveChangesAsync(ct);
                posted++;
                total += row.Amount;
            }

            _logger.LogInformation("Depreciation run {Year}-{Month}: {Posted} posted, {Skipped} skipped", year, month, posted, skipped);
            return new BaseResponse<DepreciationRunResultDto>(true, null, "Depreciation run complete",
                new DepreciationRunResultDto(year, month, assets.Count, posted, skipped, Math.Round(total, 2), messages));
        }

        public async Task<BaseResponse<FixedAssetDto>> DisposeAsync(int id, AssetDisposeDto dto, CancellationToken ct = default)
        {
            var a = await _db.Set<FixedAsset>().Include(x => x.DepreciationEntries).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return new BaseResponse<FixedAssetDto>(false, "Asset not found", "");
            if (a.Status == "Disposed") return new BaseResponse<FixedAssetDto>(false, "Asset is already disposed", "");
            if (dto.Proceeds < 0) return new BaseResponse<FixedAssetDto>(false, "Proceeds cannot be negative", "");

            var open = await _periods.EnsureOpenAsync(dto.DisposedOn, ct);
            if (!open.Success) return new BaseResponse<FixedAssetDto>(false, open.Error, "");

            var cash = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.CashGlAccountId && x.IsActive, ct);
            if (cash is null && dto.Proceeds > 0) return new BaseResponse<FixedAssetDto>(false, "Cash GL account not found or inactive", "");
            var gainLoss = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.GainLossGlAccountId && x.IsActive, ct);
            if (gainLoss is null) return new BaseResponse<FixedAssetDto>(false, "Gain/loss GL account not found or inactive", "");

            var accumulated = Math.Round(a.DepreciationEntries.Sum(d => d.Amount), 2);
            var proceeds = Math.Round(dto.Proceeds, 2);
            var diff = Math.Round(proceeds + accumulated - a.Cost, 2); // >0 gain, <0 loss

            var lines = new List<JournalEntryLineCreateDto>();
            if (proceeds > 0) lines.Add(new(cash!.Id, proceeds, 0, $"Disposal proceeds {a.Name}"));
            if (accumulated > 0) lines.Add(new(a.AccumulatedDepGlAccountId, accumulated, 0, $"Reverse accumulated depreciation {a.Name}"));
            if (diff < 0) lines.Add(new(gainLoss.Id, -diff, 0, $"Loss on disposal {a.Name}"));
            lines.Add(new(a.AssetGlAccountId, 0, a.Cost, $"Remove asset cost {a.Name}"));
            if (diff > 0) lines.Add(new(gainLoss.Id, 0, diff, $"Gain on disposal {a.Name}"));

            var je = await _journal.CreateJournalEntryAsync(new JournalEntryCreateDto(
                AcctDates.Utc(dto.DisposedOn), $"Disposal of {a.Name}", "AssetDisposal", a.Id, lines), _user.UserId, ct);
            if (!je.Success || je.Data is null) return new BaseResponse<FixedAssetDto>(false, $"Journal entry failed: {je.Error}", "");
            var post = await _journal.PostJournalEntryAsync(je.Data.Id, _user.UserId, ct);
            if (!post.Success) return new BaseResponse<FixedAssetDto>(false, $"Journal posting failed: {post.Error}", "");

            a.Status = "Disposed";
            a.DisposedOn = AcctDates.Utc(dto.DisposedOn);
            a.DisposalProceeds = proceeds;
            a.DisposalJournalEntryId = je.Data.Id;
            if (!string.IsNullOrWhiteSpace(dto.Notes)) a.Notes = string.IsNullOrWhiteSpace(a.Notes) ? dto.Notes.Trim() : $"{a.Notes}\n{dto.Notes.Trim()}";
            await _db.SaveChangesAsync(ct);
            return await GetAsync(a.Id, ct);
        }

        public async Task<AssetRegisterSummaryDto> GetRegisterSummaryAsync(CancellationToken ct = default)
        {
            var all = await ListAsync(null, ct);
            var active = all.Where(a => a.Status == "Active").ToList();
            return new AssetRegisterSummaryDto(
                active.Count, all.Count(a => a.Status == "Disposed"),
                Math.Round(active.Sum(a => a.Cost), 2), Math.Round(active.Sum(a => a.AccumulatedDepreciation), 2),
                Math.Round(active.Sum(a => a.NetBookValue), 2), all);
        }

        // ============================================
        // HELPERS
        // ============================================

        private async Task<string?> ValidateAsync(FixedAssetUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required";
            if (dto.Cost <= 0) return "Cost must be positive";
            if (dto.SalvageValue < 0 || dto.SalvageValue >= dto.Cost) return "Salvage value must be between 0 and cost";
            if (dto.UsefulLifeMonths <= 0 || dto.UsefulLifeMonths > 600) return "Useful life must be 1-600 months";
            if (!Methods.Contains(dto.Method, StringComparer.OrdinalIgnoreCase)) return "Method must be StraightLine or DecliningBalance";
            var ids = new[] { dto.AssetGlAccountId, dto.AccumulatedDepGlAccountId, dto.DepExpenseGlAccountId };
            var found = await _db.Set<Account>().AsNoTracking().CountAsync(a => ids.Contains(a.Id) && a.IsActive, ct);
            if (found != ids.Distinct().Count()) return "One or more GL accounts not found or inactive";
            return null;
        }

        private static void Apply(FixedAsset a, FixedAssetUpsertDto dto)
        {
            a.Name = dto.Name.Trim();
            a.Category = dto.Category?.Trim();
            a.AcquiredOn = AcctDates.StartOfDay(dto.AcquiredOn);
            a.Cost = Math.Round(dto.Cost, 2);
            a.SalvageValue = Math.Round(dto.SalvageValue, 2);
            a.UsefulLifeMonths = dto.UsefulLifeMonths;
            a.Method = Methods.First(m => m.Equals(dto.Method, StringComparison.OrdinalIgnoreCase));
            a.AssetGlAccountId = dto.AssetGlAccountId;
            a.AccumulatedDepGlAccountId = dto.AccumulatedDepGlAccountId;
            a.DepExpenseGlAccountId = dto.DepExpenseGlAccountId;
            a.Notes = dto.Notes?.Trim();
        }

        private static FixedAssetDto ToDto(FixedAsset a)
        {
            var accumulated = Math.Round(a.DepreciationEntries.Sum(d => d.Amount), 2);
            return new FixedAssetDto(a.Id, a.Name, a.Category, a.AcquiredOn, a.Cost, a.SalvageValue, a.UsefulLifeMonths, a.Method,
                a.AssetGlAccountId, a.AccumulatedDepGlAccountId, a.DepExpenseGlAccountId, a.Status, a.DisposedOn, a.DisposalProceeds, a.Notes, a.BranchId,
                accumulated, a.Status == "Disposed" ? 0 : Math.Round(a.Cost - accumulated, 2), a.DepreciationEntries.Count);
        }
    }
}
