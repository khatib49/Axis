using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class PurchaseService : IPurchaseService
    {
        private readonly IBaseRepository<Purchase> _repo;
        private readonly IBaseRepository<PurchaseLine> _lineRepo;
        private readonly IBaseRepository<Supplier> _supplierRepo;
        private readonly IBaseRepository<Ingredient> _ingredientRepo;
        private readonly IBaseRepository<StockMovement> _movementRepo;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<PurchaseService> _logger;

        public PurchaseService(
            IBaseRepository<Purchase> repo,
            IBaseRepository<PurchaseLine> lineRepo,
            IBaseRepository<Supplier> supplierRepo,
            IBaseRepository<Ingredient> ingredientRepo,
            IBaseRepository<StockMovement> movementRepo,
            IUnitOfWork uow,
            ILogger<PurchaseService> logger)
        {
            _repo = repo;
            _lineRepo = lineRepo;
            _supplierRepo = supplierRepo;
            _ingredientRepo = ingredientRepo;
            _movementRepo = movementRepo;
            _uow = uow;
            _logger = logger;
        }

        // ── Create — atomic: Purchase + Lines + StockMovements + BuyPrice update
        public async Task<PurchaseDto> CreateAsync(PurchaseCreateDto dto, string? actor, CancellationToken ct = default)
        {
            if (dto.Lines == null || dto.Lines.Count == 0)
                throw new ArgumentException("Purchase needs at least one line.");

            // Validate supplier (if specified)
            if (dto.SupplierId.HasValue)
            {
                var supplierExists = await _supplierRepo.Query().AnyAsync(s => s.Id == dto.SupplierId.Value && s.IsActive, ct);
                if (!supplierExists) throw new ArgumentException("Supplier not found or inactive.");
            }

            // Validate every ingredient
            var ingredientIds = dto.Lines.Select(l => l.IngredientId).Distinct().ToList();
            var tracked = await _ingredientRepo.Query(asNoTracking: false)
                .Where(i => ingredientIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);
            var missing = ingredientIds.Except(tracked.Keys).ToList();
            if (missing.Any())
                throw new ArgumentException($"Unknown ingredient ids: {string.Join(", ", missing)}");

            foreach (var line in dto.Lines)
            {
                if (line.Quantity <= 0) throw new ArgumentException("Quantity must be > 0 on every line.");
                if (line.UnitCost < 0) throw new ArgumentException("Unit cost cannot be negative.");
            }

            // Build entity
            var purchase = new Purchase
            {
                SupplierId = dto.SupplierId,
                PurchaseDate = dto.PurchaseDate == default ? DateTime.UtcNow : dto.PurchaseDate.ToUniversalTime(),
                InvoiceNumber = dto.InvoiceNumber?.Trim(),
                Notes = dto.Notes?.Trim(),
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow
            };

            decimal totalCost = 0m;

            foreach (var line in dto.Lines)
            {
                var ing = tracked[line.IngredientId];
                var lineTotal = Math.Round(line.Quantity * line.UnitCost, 2);
                totalCost += lineTotal;

                // Update ingredient: stock up, BuyPricePerUnit = latest unit cost.
                ing.QuantityOnHand = Math.Round(ing.QuantityOnHand + line.Quantity, 3);
                ing.BuyPricePerUnit = line.UnitCost;
                ing.ModifiedOn = DateTime.UtcNow;

                purchase.Lines.Add(new PurchaseLine
                {
                    IngredientId = ing.Id,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    LineTotal = lineTotal,
                    Notes = line.Notes?.Trim()
                });

                // StockMovement (Purchase). Saved as part of the same DB
                // transaction below. ReferenceId will be filled after the
                // Purchase has its Id.
                await _movementRepo.AddAsync(new StockMovement
                {
                    IngredientId = ing.Id,
                    Quantity = line.Quantity,
                    Type = "Purchase",
                    ReferenceType = "Purchase",
                    ReferenceId = null, // patched below after the Purchase saves
                    UnitCost = line.UnitCost,
                    TotalCost = lineTotal,
                    Notes = line.Notes?.Trim(),
                    BalanceAfter = ing.QuantityOnHand,
                    CreatedBy = actor,
                    CreatedOn = DateTime.UtcNow
                }, ct);
            }

            purchase.TotalCost = totalCost;

            await _repo.AddAsync(purchase, ct);
            await _uow.SaveChangesAsync(ct);

            // Patch the movements with the Purchase.Id (we only have it
            // after the first save). The movements above are still tracked.
            var unpatched = await _movementRepo.Query(asNoTracking: false)
                .Where(m => m.ReferenceType == "Purchase"
                         && m.ReferenceId == null
                         && m.CreatedBy == actor
                         && m.CreatedOn >= purchase.CreatedOn.AddSeconds(-5))
                .ToListAsync(ct);
            foreach (var m in unpatched) m.ReferenceId = purchase.Id;
            await _uow.SaveChangesAsync(ct);

            return await GetAsync(purchase.Id, ct) ?? throw new InvalidOperationException("Lost the purchase right after saving it.");
        }

        // ── Read ────────────────────────────────────────────────────────
        public async Task<PurchaseDto?> GetAsync(int id, CancellationToken ct = default)
        {
            return await _repo.Query()
                .Include(p => p.Supplier)
                .Include(p => p.Lines).ThenInclude(l => l.Ingredient)
                .Where(p => p.Id == id)
                .Select(p => new PurchaseDto(
                    p.Id, p.SupplierId, p.Supplier != null ? p.Supplier.Name : null,
                    p.PurchaseDate, p.InvoiceNumber, p.TotalCost, p.Notes,
                    p.CreatedBy, p.CreatedOn,
                    p.Lines.Select(l => new PurchaseLineDto(
                        l.Id, l.IngredientId, l.Ingredient.Name, l.Ingredient.Unit,
                        l.Quantity, l.UnitCost, l.LineTotal, l.Notes)).ToList()))
                .FirstOrDefaultAsync(ct);
        }

        public async Task<PaginatedResponse<PurchaseDto>> ListAsync(PurchaseFilterDto filter, CancellationToken ct = default)
        {
            var q = _repo.Query()
                .Include(p => p.Supplier)
                .Include(p => p.Lines).ThenInclude(l => l.Ingredient)
                .AsQueryable();

            if (filter.SupplierId.HasValue) q = q.Where(p => p.SupplierId == filter.SupplierId.Value);
            if (filter.IngredientId.HasValue) q = q.Where(p => p.Lines.Any(l => l.IngredientId == filter.IngredientId.Value));
            if (filter.From.HasValue) q = q.Where(p => p.PurchaseDate >= filter.From.Value);
            if (filter.To.HasValue)
            {
                var toExclusive = filter.To.Value.Date.AddDays(1);
                q = q.Where(p => p.PurchaseDate < toExclusive);
            }

            var totalCount = await q.CountAsync(ct);
            var page = filter.Page < 1 ? 1 : filter.Page;
            var pageSize = filter.PageSize < 1 ? 25 : filter.PageSize;

            var rows = await q
                .OrderByDescending(p => p.PurchaseDate).ThenByDescending(p => p.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new PurchaseDto(
                    p.Id, p.SupplierId, p.Supplier != null ? p.Supplier.Name : null,
                    p.PurchaseDate, p.InvoiceNumber, p.TotalCost, p.Notes,
                    p.CreatedBy, p.CreatedOn,
                    p.Lines.Select(l => new PurchaseLineDto(
                        l.Id, l.IngredientId, l.Ingredient.Name, l.Ingredient.Unit,
                        l.Quantity, l.UnitCost, l.LineTotal, l.Notes)).ToList()))
                .ToListAsync(ct);

            return new PaginatedResponse<PurchaseDto>(totalCount, rows, page, pageSize);
        }

        // ── Price trend ─────────────────────────────────────────────────
        public async Task<IReadOnlyList<PriceTrendPointDto>> GetPriceTrendAsync(int ingredientId, DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var q = _lineRepo.Query()
                .Include(l => l.Purchase).ThenInclude(p => p.Supplier)
                .Where(l => l.IngredientId == ingredientId);

            if (from.HasValue) q = q.Where(l => l.Purchase.PurchaseDate >= from.Value);
            if (to.HasValue)
            {
                var toExclusive = to.Value.Date.AddDays(1);
                q = q.Where(l => l.Purchase.PurchaseDate < toExclusive);
            }

            return await q
                .OrderBy(l => l.Purchase.PurchaseDate)
                .Select(l => new PriceTrendPointDto(
                    l.Purchase.PurchaseDate,
                    l.UnitCost,
                    l.Quantity,
                    l.Purchase.SupplierId,
                    l.Purchase.Supplier != null ? l.Purchase.Supplier.Name : null,
                    l.PurchaseId))
                .ToListAsync(ct);
        }
    }
}
