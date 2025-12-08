using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class KitchenService : IKitchenService
    {
        private readonly IBaseRepository<TransactionRecord> _transactionRepo;
        private readonly IBaseRepository<Category> _categoryRepo;
        private readonly IBaseRepository<Status> _statusRepo;
        private readonly IRoleCategoryService _roleCategoryService;
        private readonly IUnitOfWork _uow;

        public KitchenService(
            IBaseRepository<TransactionRecord> transactionRepo,
            IBaseRepository<Category> categoryRepo,
            IBaseRepository<Status> statusRepo,
            IRoleCategoryService roleCategoryService,
            IUnitOfWork uow)
        {
            _transactionRepo = transactionRepo;
            _categoryRepo = categoryRepo;
            _statusRepo = statusRepo;
            _roleCategoryService = roleCategoryService;
            _uow = uow;
        }

        public async Task<List<KitchenOrderDto>> GetKitchenOrdersAsync(
            int? foodStatusId = null,
            string? userRole = null,
            CancellationToken ct = default)
        {
            // Get TCG categories to exclude (always exclude retail)
            var tcgCategoryIds = await GetTcgRetailCategoryIds(ct);

            // Get allowed categories for this role (from RoleCategory table)
            var allowedCategoryIds = new List<int>();
            if (!string.IsNullOrEmpty(userRole) && userRole.ToLower() != "admin" && userRole.ToLower() != "cashier")
            {
                allowedCategoryIds = await _roleCategoryService.GetCategoryIdsForRoleAsync(userRole, ct);

                // If no categories assigned to this role, return empty list
                if (!allowedCategoryIds.Any())
                {
                    return new List<KitchenOrderDto>();
                }
            }

            var query = _transactionRepo.Query()
                .Where(t => t.StatusId == 6) // Only paid/completed transactions
                .Where(t => t.GameId == null) // Only item transactions (FNB)
                .Where(t => t.TransactionItems.Any()) // Must have items
                .Where(t => t.TransactionItems.Any(ti =>
                    ti.Item != null && !tcgCategoryIds.Contains(ti.Item.CategoryId))); // Exclude TCG

            // Filter by food status if specified
            if (foodStatusId.HasValue)
            {
                query = query.Where(t => t.FK_FoodStatusId == foodStatusId.Value);
            }
            else
            {
                // Default: show orders that are not yet served (pending, in progress, ready)
                query = query.Where(t => t.FK_FoodStatusId != null && t.FK_FoodStatusId != 10);
            }

            // ✅ Filter by role-specific categories (if not admin/cashier)
            if (allowedCategoryIds.Any())
            {
                query = query.Where(t => t.TransactionItems.Any(ti =>
                    ti.Item != null && allowedCategoryIds.Contains(ti.Item.CategoryId)));
            }

            // First get the transactions with included data
            var transactions = await query
                .Include(t => t.FoodStatus)
                .Include(t => t.User)
                .Include(t => t.Room)
                .Include(t => t.Set)
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                        .ThenInclude(i => i!.Category)
                .OrderBy(t => t.CreatedOn)
                .ToListAsync(ct);

            // Then filter and map in memory
            var orders = transactions.Select(t => new KitchenOrderDto(
                TransactionId: t.Id,
                OrderTime: t.CreatedOn,
                OrderedBy: t.CreatedBy,
                FoodStatusId: t.FK_FoodStatusId ?? 0,
                FoodStatusName: t.FoodStatus?.Name ?? "Unknown",
                Items: t.TransactionItems
                    .Where(ti => ti.Item != null &&
                                !tcgCategoryIds.Contains(ti.Item.CategoryId) &&
                                (allowedCategoryIds.Count == 0 || allowedCategoryIds.Contains(ti.Item.CategoryId)))
                    .Select(ti => new KitchenOrderItemDto(
                        ItemId: ti.ItemId,
                        ItemName: ti.Item!.Name,
                        Quantity: ti.Quantity,
                        CategoryName: ti.Item.Category?.Name,
                        SpecialInstructions: null
                    ))
                    .ToList(),
                CustomerName: t.User != null ? $"{t.User.FirstName} {t.User.LastName}".Trim() : null,
                RoomId: t.RoomId,
                RoomName: t.Room?.Name,
                SetId: t.SetId,
                SetName: t.Set?.Name,
                Comment: t.Comment
            ))
            .Where(o => o.Items.Any()) // Only include orders that have items after filtering
            .ToList();

            return orders;
        }

        public async Task<KitchenOrderDto?> GetKitchenOrderByIdAsync(
            int transactionId,
            string? userRole = null,
            CancellationToken ct = default)
        {
            var tcgCategoryIds = await GetTcgRetailCategoryIds(ct);

            // Get allowed categories for this role
            var allowedCategoryIds = new List<int>();
            if (!string.IsNullOrEmpty(userRole) && userRole.ToLower() != "admin" && userRole.ToLower() != "cashier")
            {
                allowedCategoryIds = await _roleCategoryService.GetCategoryIdsForRoleAsync(userRole, ct);
            }

            var transaction = await _transactionRepo.Query()
                .Where(t => t.Id == transactionId)
                .Where(t => t.GameId == null)
                .Include(t => t.FoodStatus)
                .Include(t => t.User)
                .Include(t => t.Room)
                .Include(t => t.Set)
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                        .ThenInclude(i => i!.Category)
                .FirstOrDefaultAsync(ct);

            if (transaction == null)
                return null;

            return new KitchenOrderDto(
                TransactionId: transaction.Id,
                OrderTime: transaction.CreatedOn,
                OrderedBy: transaction.CreatedBy,
                FoodStatusId: transaction.FK_FoodStatusId ?? 0,
                FoodStatusName: transaction.FoodStatus?.Name ?? "Unknown",
                Items: transaction.TransactionItems
                    .Where(ti => ti.Item != null &&
                                !tcgCategoryIds.Contains(ti.Item.CategoryId) &&
                                (allowedCategoryIds.Count == 0 || allowedCategoryIds.Contains(ti.Item.CategoryId)))
                    .Select(ti => new KitchenOrderItemDto(
                        ItemId: ti.ItemId,
                        ItemName: ti.Item!.Name,
                        Quantity: ti.Quantity,
                        CategoryName: ti.Item.Category?.Name,
                        SpecialInstructions: null
                    ))
                    .ToList(),
                CustomerName: transaction.User != null ? $"{transaction.User.FirstName} {transaction.User.LastName}".Trim() : null,
                RoomId: transaction.RoomId,
                RoomName: transaction.Room?.Name,
                SetId: transaction.SetId,
                SetName: transaction.Set?.Name,
                Comment: transaction.Comment
            );
        }

        public async Task<bool> UpdateFoodStatusAsync(
            int transactionId,
            int newFoodStatusId,
            string updatedBy,
            CancellationToken ct = default)
        {
            var statusExists = await _statusRepo.Query()
                .AnyAsync(s => s.Id == newFoodStatusId, ct);

            if (!statusExists)
            {
                throw new ArgumentException($"Invalid food status ID: {newFoodStatusId}");
            }

            var transaction = await _transactionRepo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

            if (transaction == null)
            {
                return false;
            }

            transaction.FK_FoodStatusId = newFoodStatusId;
            transaction.ModifiedOn = DateTime.UtcNow;

            _transactionRepo.Update(transaction);
            await _uow.SaveChangesAsync(ct);

            return true;
        }

        public async Task<KitchenStatsDto> GetKitchenStatsAsync(
            string? userRole = null,
            CancellationToken ct = default)
        {
            var tcgCategoryIds = await GetTcgRetailCategoryIds(ct);

            // Get allowed categories for this role
            var allowedCategoryIds = new List<int>();
            if (!string.IsNullOrEmpty(userRole) && userRole.ToLower() != "admin" && userRole.ToLower() != "cashier")
            {
                allowedCategoryIds = await _roleCategoryService.GetCategoryIdsForRoleAsync(userRole, ct);
            }

            var today = DateTime.UtcNow.Date;

            var ordersQuery = _transactionRepo.Query()
                .Where(t => t.StatusId == 6)
                .Where(t => t.GameId == null)
                .Where(t => t.TransactionItems.Any(ti =>
                    ti.Item != null && !tcgCategoryIds.Contains(ti.Item.CategoryId)))
                .Where(t => t.CreatedOn >= today);

            // Filter by role
            if (allowedCategoryIds.Any())
            {
                ordersQuery = ordersQuery.Where(t => t.TransactionItems.Any(ti =>
                    ti.Item != null && allowedCategoryIds.Contains(ti.Item.CategoryId)));
            }

            var pendingOrders = await ordersQuery
                .CountAsync(t => t.FK_FoodStatusId == 7, ct);

            var inProgressOrders = await ordersQuery
                .CountAsync(t => t.FK_FoodStatusId == 8, ct);

            var readyOrders = await ordersQuery
                .CountAsync(t => t.FK_FoodStatusId == 9, ct);

            var servedToday = await ordersQuery
                .CountAsync(t => t.FK_FoodStatusId == 10, ct);

            var averagePreparationTime = 0m;

            return new KitchenStatsDto(
                PendingOrders: pendingOrders,
                InProgressOrders: inProgressOrders,
                ReadyOrders: readyOrders,
                ServedToday: servedToday,
                AveragePreparationTime: averagePreparationTime
            );
        }

        private async Task<List<int>> GetTcgRetailCategoryIds(CancellationToken ct)
        {
            return await _categoryRepo.Query()
                .Where(c => c.Name.ToLower().Contains("tcg retail") ||
                           c.Name.ToLower() == "tcg" ||
                           c.Type.ToLower().Contains("tcg") ||
                           (c.ItemType != null && c.ItemType.ToLower() == "retail"))
                .Select(c => c.Id)
                .ToListAsync(ct);
        }
    }
}