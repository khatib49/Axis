using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class IngredientService : IIngredientService
    {
        private readonly IBaseRepository<Ingredient> _repo;
        private readonly IBaseRepository<StockMovement> _movementRepo;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<IngredientService> _logger;

        // Centralized list — must match what the FE dropdown offers and the
        // schema's CHECK-style validation. Keep small and explicit.
        private static readonly HashSet<string> AllowedWasteReasons = new(StringComparer.OrdinalIgnoreCase)
        {
            "Spoilage", "Spillage", "Burnt", "Expired", "Customer Return", "Other"
        };

        public IngredientService(
            IBaseRepository<Ingredient> repo,
            IBaseRepository<StockMovement> movementRepo,
            IUnitOfWork uow,
            ILogger<IngredientService> logger)
        {
            _repo = repo;
            _movementRepo = movementRepo;
            _uow = uow;
            _logger = logger;
        }

        // ── Reads ──────────────────────────────────────────────────────
        public async Task<IReadOnlyList<IngredientDto>> ListAsync(bool includeHidden, CancellationToken ct = default)
        {
            var q = _repo.Query();
            if (!includeHidden) q = q.Where(i => i.IsActive);

            var list = await q.OrderBy(i => i.Name).ToListAsync(ct);
            return list.Select(MapToDto).ToList();
        }

        public async Task<IngredientDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.Query().FirstOrDefaultAsync(i => i.Id == id, ct);
            return e == null ? null : MapToDto(e);
        }

        public async Task<IReadOnlyList<IngredientDto>> GetLowStockAsync(CancellationToken ct = default)
        {
            var list = await _repo.Query()
                .Where(i => i.IsActive
                         && i.ReorderLevel.HasValue
                         && i.QuantityOnHand < i.ReorderLevel.Value)
                .OrderBy(i => i.QuantityOnHand)
                .ToListAsync(ct);
            return list.Select(MapToDto).ToList();
        }

        // ── Mutations ──────────────────────────────────────────────────
        public async Task<IngredientDto> CreateAsync(IngredientCreateDto dto, string? actor, CancellationToken ct = default)
        {
            ValidateNameAndUnit(dto.Name, dto.Unit);

            var name = dto.Name.Trim();
            var exists = await _repo.Query().AnyAsync(i => i.Name.ToLower() == name.ToLower(), ct);
            if (exists) throw new InvalidOperationException($"An ingredient named '{name}' already exists.");

            var opening = dto.OpeningQuantity ?? 0m;
            if (opening < 0) throw new ArgumentException("Opening quantity cannot be negative.");

            var entity = new Ingredient
            {
                Name = name,
                Unit = dto.Unit.Trim(),
                QuantityOnHand = opening,
                ReorderLevel = dto.ReorderLevel,
                BuyPricePerUnit = dto.BuyPricePerUnit,
                Notes = dto.Notes?.Trim(),
                IsActive = true,
                CreatedOn = DateTime.UtcNow
            };
            await _repo.AddAsync(entity, ct);
            await _uow.SaveChangesAsync(ct);

            if (opening > 0)
            {
                // Record the opening balance as a Purchase movement so the
                // audit log explains where the initial quantity came from.
                await _movementRepo.AddAsync(new StockMovement
                {
                    IngredientId = entity.Id,
                    Quantity = opening,
                    Type = "Purchase",
                    Notes = "Opening balance",
                    BalanceAfter = opening,
                    CreatedBy = actor,
                    CreatedOn = DateTime.UtcNow
                }, ct);
                await _uow.SaveChangesAsync(ct);
            }

            return MapToDto(entity);
        }

        public async Task<IngredientDto> UpdateAsync(int id, IngredientUpdateDto dto, string? actor, CancellationToken ct = default)
        {
            ValidateNameAndUnit(dto.Name, dto.Unit);

            var entity = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(i => i.Id == id, ct)
                ?? throw new KeyNotFoundException("Ingredient not found.");

            var newName = dto.Name.Trim();
            var clash = await _repo.Query()
                .AnyAsync(i => i.Id != id && i.Name.ToLower() == newName.ToLower(), ct);
            if (clash) throw new InvalidOperationException($"Another ingredient named '{newName}' already exists.");

            entity.Name = newName;
            entity.Unit = dto.Unit.Trim();
            entity.ReorderLevel = dto.ReorderLevel;
            entity.BuyPricePerUnit = dto.BuyPricePerUnit;
            entity.Notes = dto.Notes?.Trim();
            entity.IsActive = dto.IsActive;
            entity.ModifiedOn = DateTime.UtcNow;

            _repo.Update(entity);
            await _uow.SaveChangesAsync(ct);
            return MapToDto(entity);
        }

        public async Task<bool> DeactivateAsync(int id, string? actor, CancellationToken ct = default)
        {
            var entity = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(i => i.Id == id, ct);
            if (entity == null || !entity.IsActive) return false;
            entity.IsActive = false;
            entity.ModifiedOn = DateTime.UtcNow;
            _repo.Update(entity);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        // ── Stock events ──────────────────────────────────────────────
        public async Task<IngredientDto> AddStockAsync(AddStockRequestDto dto, string? actor, CancellationToken ct = default)
        {
            if (dto.Quantity <= 0) throw new ArgumentException("Quantity must be positive.");
            if (dto.UnitCost.HasValue && dto.UnitCost.Value < 0) throw new ArgumentException("Unit cost cannot be negative.");
            var entity = await LoadTrackedAsync(dto.IngredientId, ct);

            entity.QuantityOnHand = Math.Round(entity.QuantityOnHand + dto.Quantity, 3);
            // If the caller passed a unit cost, snapshot it and update the
            // ingredient's BuyPricePerUnit (latest-cost method).
            decimal? totalCost = null;
            if (dto.UnitCost.HasValue)
            {
                entity.BuyPricePerUnit = dto.UnitCost.Value;
                totalCost = Math.Round(dto.Quantity * dto.UnitCost.Value, 2);
            }
            entity.ModifiedOn = DateTime.UtcNow;

            await _movementRepo.AddAsync(new StockMovement
            {
                IngredientId = entity.Id,
                Quantity = dto.Quantity, // positive
                Type = "Purchase",
                Notes = dto.Notes?.Trim(),
                BalanceAfter = entity.QuantityOnHand,
                UnitCost = dto.UnitCost,
                TotalCost = totalCost,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow
            }, ct);

            await _uow.SaveChangesAsync(ct);
            return MapToDto(entity);
        }

        public async Task<IngredientDto> RecordWasteAsync(RecordWasteRequestDto dto, string? actor, CancellationToken ct = default)
        {
            if (dto.Quantity <= 0) throw new ArgumentException("Quantity must be positive.");
            if (string.IsNullOrWhiteSpace(dto.WasteReason) || !AllowedWasteReasons.Contains(dto.WasteReason.Trim()))
                throw new ArgumentException($"Waste reason must be one of: {string.Join(", ", AllowedWasteReasons)}");

            var entity = await LoadTrackedAsync(dto.IngredientId, ct);

            // Waste is a negative movement.
            entity.QuantityOnHand = Math.Round(entity.QuantityOnHand - dto.Quantity, 3);
            entity.ModifiedOn = DateTime.UtcNow;

            await _movementRepo.AddAsync(new StockMovement
            {
                IngredientId = entity.Id,
                Quantity = -dto.Quantity,
                Type = "Waste",
                WasteReason = dto.WasteReason.Trim(),
                Notes = dto.Notes?.Trim(),
                BalanceAfter = entity.QuantityOnHand,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow
            }, ct);

            await _uow.SaveChangesAsync(ct);
            return MapToDto(entity);
        }

        public async Task<IngredientDto> AdjustStockAsync(AdjustStockRequestDto dto, string? actor, CancellationToken ct = default)
        {
            var entity = await LoadTrackedAsync(dto.IngredientId, ct);

            // Adjust = set absolute target. Movement records the delta so the
            // audit log shows what changed.
            var delta = Math.Round(dto.NewQuantity - entity.QuantityOnHand, 3);
            if (delta == 0)
            {
                // No-op; don't pollute the audit log with zero entries.
                return MapToDto(entity);
            }

            entity.QuantityOnHand = Math.Round(dto.NewQuantity, 3);
            entity.ModifiedOn = DateTime.UtcNow;

            await _movementRepo.AddAsync(new StockMovement
            {
                IngredientId = entity.Id,
                Quantity = delta, // signed
                Type = "Adjustment",
                Notes = dto.Notes?.Trim() ?? $"Manual adjustment to {dto.NewQuantity}",
                BalanceAfter = entity.QuantityOnHand,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow
            }, ct);

            await _uow.SaveChangesAsync(ct);
            return MapToDto(entity);
        }

        // ── Audit log ──────────────────────────────────────────────────
        public async Task<PaginatedResponse<StockMovementDto>> GetMovementsAsync(StockMovementFilterDto filter, CancellationToken ct = default)
        {
            var q = _movementRepo.Query().Include(m => m.Ingredient).AsQueryable();

            if (filter.IngredientId.HasValue) q = q.Where(m => m.IngredientId == filter.IngredientId.Value);
            if (!string.IsNullOrWhiteSpace(filter.Type)) q = q.Where(m => m.Type == filter.Type);
            if (filter.From.HasValue) q = q.Where(m => m.CreatedOn >= filter.From.Value);
            if (filter.To.HasValue)
            {
                var toExclusive = filter.To.Value.Date.AddDays(1);
                q = q.Where(m => m.CreatedOn < toExclusive);
            }

            var totalCount = await q.CountAsync(ct);

            var page = filter.Page < 1 ? 1 : filter.Page;
            var pageSize = filter.PageSize < 1 ? 50 : filter.PageSize;

            var rows = await q
                .OrderByDescending(m => m.CreatedOn).ThenByDescending(m => m.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(m => new StockMovementDto(
                    m.Id, m.IngredientId, m.Ingredient.Name, m.Ingredient.Unit,
                    m.Quantity, m.Type, m.ReferenceType, m.ReferenceId,
                    m.WasteReason, m.Notes, m.BalanceAfter, m.CreatedBy, m.CreatedOn,
                    m.UnitCost, m.TotalCost))
                .ToListAsync(ct);

            return new PaginatedResponse<StockMovementDto>(totalCount, rows, page, pageSize);
        }

        // ── Helpers ────────────────────────────────────────────────────
        private async Task<Ingredient> LoadTrackedAsync(int id, CancellationToken ct)
            => await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(i => i.Id == id, ct)
               ?? throw new KeyNotFoundException("Ingredient not found.");

        private static void ValidateNameAndUnit(string name, string unit)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.");
            if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit is required.");
        }

        private static IngredientDto MapToDto(Ingredient e) =>
            new(
                e.Id, e.Name, e.Unit, e.QuantityOnHand, e.ReorderLevel, e.BuyPricePerUnit,
                e.IsActive, e.Notes, e.CreatedOn, e.ModifiedOn,
                IsBelowReorderLevel: e.ReorderLevel.HasValue && e.QuantityOnHand < e.ReorderLevel.Value,
                IsNegative: e.QuantityOnHand < 0
            );
    }
}
