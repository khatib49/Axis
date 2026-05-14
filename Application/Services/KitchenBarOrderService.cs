using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class KitchenBarOrderService : IKitchenBarOrderService
    {
        private readonly IBaseRepository<KitchenBarOrder> _repo;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<KitchenBarOrderService> _logger;

        public KitchenBarOrderService(
            IBaseRepository<KitchenBarOrder> repo,
            IUnitOfWork uow,
            ILogger<KitchenBarOrderService> logger)
        {
            _repo = repo;
            _uow = uow;
            _logger = logger;
        }

        public async Task<KitchenBarOrderDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var order = await _repo.Query()
                .Include(o => o.PreparedByUser)
                .FirstOrDefaultAsync(o => o.Id == id, ct);

            return order == null ? null : MapToDto(order);
        }

        public async Task<PaginatedResponse<KitchenBarOrderDto>> ListAsync(KitchenBarOrderListDto filter, CancellationToken ct = default)
        {
            var query = _repo.Query()
                .Include(o => o.PreparedByUser)
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(filter.Station))
                query = query.Where(o => o.Station == filter.Station);

            if (!string.IsNullOrWhiteSpace(filter.Status))
                query = query.Where(o => o.Status == filter.Status);

            if (filter.FromDate.HasValue)
                query = query.Where(o => o.OrderedAt >= filter.FromDate.Value);

            if (filter.ToDate.HasValue)
                query = query.Where(o => o.OrderedAt <= filter.ToDate.Value);

            // Order by most recent first
            query = query.OrderByDescending(o => o.OrderedAt);

            // Count total
            var totalCount = await query.CountAsync(ct);

            // Paginate
            var orders = await query
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync(ct);

            var dtos = orders.Select(MapToDto).ToList();

            return new PaginatedResponse<KitchenBarOrderDto>(
                totalCount, dtos, filter.Page, filter.PageSize);
        }

        public async Task<bool> UpdateStatusAsync(KitchenBarOrderUpdateStatusDto dto, CancellationToken ct = default)
        {
            var order = await _repo.GetByIdAsync(dto.Id, asNoTracking: false, ct);
            if (order == null) return false;

            order.Status = dto.Status;

            if (dto.Status == "Done")
            {
                order.PreparedAt = DateTime.UtcNow;
                order.PreparedBy = dto.PreparedBy;
            }

            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Updated KitchenBarOrder {OrderId} to status {Status}",
                dto.Id, dto.Status);

            return true;
        }

        public async Task<bool> MarkAsPrintedAsync(List<int> orderIds, CancellationToken ct = default)
        {
            if (!orderIds.Any()) return false;

            var orders = await _repo.Query(asNoTracking: false)
                .Where(o => orderIds.Contains(o.Id))
                .ToListAsync(ct);

            if (!orders.Any()) return false;

            var now = DateTime.UtcNow;
            foreach (var order in orders)
            {
                order.PrintedAt = now;
            }

            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Marked {Count} orders as printed",
                orders.Count);

            return true;
        }

        public async Task<List<KitchenBarOrderDto>> GetPendingOrdersByStationAsync(string station, CancellationToken ct = default)
        {
            var orders = await _repo.Query()
                .Include(o => o.PreparedByUser)
                .Where(o => o.Station == station && o.Status == "Pending")
                .OrderBy(o => o.OrderedAt)
                .ToListAsync(ct);

            return orders.Select(MapToDto).ToList();
        }

        private static KitchenBarOrderDto MapToDto(KitchenBarOrder order)
        {
            return new KitchenBarOrderDto(
                order.Id,
                order.TransactionId,
                order.ItemId,
                order.ItemName,
                order.Quantity,
                order.ItemPrice,
                order.Station,
                order.Status,
                order.OrderedAt,
                order.PreparedAt,
                order.PreparedBy,
                order.PreparedByUser?.UserName,
                order.PrintedAt,
                order.TableNumber,
                order.GuestName,
                order.ItemComment,
                order.CreatedByUsername,
                order.CreatedAt
            );
        }
    }
}
