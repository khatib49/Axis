using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class TransactionAuditLogService : ITransactionAuditLogService
    {
        private readonly IBaseRepository<TransactionAuditLog> _repo;

        public TransactionAuditLogService(IBaseRepository<TransactionAuditLog> repo)
            => _repo = repo;

        public async Task<List<TransactionAuditLogDto>> GetByTransaction(int transactionId, CancellationToken ct = default)
        {
            return await _repo.QueryableAsync(x => x.TransactionId == transactionId)
                .OrderByDescending(x => x.ChangedOn)
                .Select(x => new TransactionAuditLogDto(
                    x.Id, x.TransactionId, x.ChangedBy, x.ChangedOn,
                    x.Action, x.FieldChanged, x.OldValue, x.NewValue, x.Notes))
                .ToListAsync(ct);
        }

        public async Task<PaginatedResponse<TransactionAuditLogDto>> ListAll(int page, int pageSize, CancellationToken ct = default)
        {
            var query = _repo.QueryableAsync().OrderByDescending(x => x.ChangedOn);
            var total = await query.CountAsync(ct);
            var items = await query
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new TransactionAuditLogDto(
                    x.Id, x.TransactionId, x.ChangedBy, x.ChangedOn,
                    x.Action, x.FieldChanged, x.OldValue, x.NewValue, x.Notes))
                .ToListAsync(ct);

            return new PaginatedResponse<TransactionAuditLogDto>(total, items, page, pageSize);
        }
    }
}