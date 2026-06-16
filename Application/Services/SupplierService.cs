using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class SupplierService : ISupplierService
    {
        private readonly IBaseRepository<Supplier> _repo;
        private readonly IUnitOfWork _uow;

        public SupplierService(IBaseRepository<Supplier> repo, IUnitOfWork uow)
        {
            _repo = repo;
            _uow = uow;
        }

        public async Task<IReadOnlyList<SupplierDto>> ListAsync(bool includeHidden, CancellationToken ct = default)
        {
            var q = _repo.Query();
            if (!includeHidden) q = q.Where(s => s.IsActive);
            return await q
                .OrderBy(s => s.Name)
                .Select(s => new SupplierDto(
                    s.Id, s.Name, s.ContactInfo, s.Notes, s.IsActive, s.CreatedOn, s.ModifiedOn))
                .ToListAsync(ct);
        }

        public async Task<SupplierDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var s = await _repo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
            return s == null ? null : new SupplierDto(s.Id, s.Name, s.ContactInfo, s.Notes, s.IsActive, s.CreatedOn, s.ModifiedOn);
        }

        public async Task<SupplierDto> CreateAsync(SupplierCreateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.");
            var name = dto.Name.Trim();
            var clash = await _repo.Query().AnyAsync(s => s.Name.ToLower() == name.ToLower(), ct);
            if (clash) throw new InvalidOperationException($"A supplier named '{name}' already exists.");

            var entity = new Supplier
            {
                Name = name,
                ContactInfo = dto.ContactInfo?.Trim(),
                Notes = dto.Notes?.Trim(),
                IsActive = true,
                CreatedOn = DateTime.UtcNow
            };
            await _repo.AddAsync(entity, ct);
            await _uow.SaveChangesAsync(ct);
            return new SupplierDto(entity.Id, entity.Name, entity.ContactInfo, entity.Notes, entity.IsActive, entity.CreatedOn, entity.ModifiedOn);
        }

        public async Task<SupplierDto> UpdateAsync(int id, SupplierUpdateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.");
            var entity = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Supplier not found.");

            var name = dto.Name.Trim();
            var clash = await _repo.Query().AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), ct);
            if (clash) throw new InvalidOperationException($"Another supplier named '{name}' already exists.");

            entity.Name = name;
            entity.ContactInfo = dto.ContactInfo?.Trim();
            entity.Notes = dto.Notes?.Trim();
            entity.IsActive = dto.IsActive;
            entity.ModifiedOn = DateTime.UtcNow;

            _repo.Update(entity);
            await _uow.SaveChangesAsync(ct);
            return new SupplierDto(entity.Id, entity.Name, entity.ContactInfo, entity.Notes, entity.IsActive, entity.CreatedOn, entity.ModifiedOn);
        }

        public async Task<bool> DeactivateAsync(int id, CancellationToken ct = default)
        {
            var entity = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity == null || !entity.IsActive) return false;
            entity.IsActive = false;
            entity.ModifiedOn = DateTime.UtcNow;
            _repo.Update(entity);
            await _uow.SaveChangesAsync(ct);
            return true;
        }
    }
}
