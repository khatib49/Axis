using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class ChannelService : IChannelService
    {
        private readonly IBaseRepository<Channel> _repo;
        private readonly IUnitOfWork _uow;

        public ChannelService(IBaseRepository<Channel> repo, IUnitOfWork uow)
        {
            _repo = repo;
            _uow = uow;
        }

        public async Task<IReadOnlyList<ChannelDto>> ListAsync(bool includeHidden, CancellationToken ct = default)
        {
            var q = _repo.Query();
            if (!includeHidden)
                q = q.Where(c => c.IsActive);

            return await q
                .OrderBy(c => c.Name)
                .Select(c => new ChannelDto(
                    c.Id, c.Name, c.Description, c.IsActive, c.CreatedOn, c.ModifiedOn))
                .ToListAsync(ct);
        }

        public async Task<ChannelDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var c = await _repo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c == null) return null;
            return new ChannelDto(c.Id, c.Name, c.Description, c.IsActive, c.CreatedOn, c.ModifiedOn);
        }

        public async Task<ChannelDto> CreateAsync(ChannelCreateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Channel name is required.");

            var name = dto.Name.Trim();
            var exists = await _repo.Query().AnyAsync(c => c.Name.ToLower() == name.ToLower(), ct);
            if (exists)
                throw new InvalidOperationException($"A channel named '{name}' already exists.");

            var entity = new Channel
            {
                Name = name,
                Description = dto.Description?.Trim(),
                IsActive = true,
                CreatedOn = DateTime.UtcNow
            };
            await _repo.AddAsync(entity, ct);
            await _uow.SaveChangesAsync(ct);

            return new ChannelDto(entity.Id, entity.Name, entity.Description, entity.IsActive, entity.CreatedOn, entity.ModifiedOn);
        }

        public async Task<ChannelDto> UpdateAsync(int id, ChannelUpdateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Channel name is required.");

            var entity = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new KeyNotFoundException("Channel not found.");

            var newName = dto.Name.Trim();
            var nameClash = await _repo.Query()
                .AnyAsync(c => c.Id != id && c.Name.ToLower() == newName.ToLower(), ct);
            if (nameClash)
                throw new InvalidOperationException($"Another channel named '{newName}' already exists.");

            entity.Name = newName;
            entity.Description = dto.Description?.Trim();
            entity.IsActive = dto.IsActive;
            entity.ModifiedOn = DateTime.UtcNow;

            _repo.Update(entity);
            await _uow.SaveChangesAsync(ct);

            return new ChannelDto(entity.Id, entity.Name, entity.Description, entity.IsActive, entity.CreatedOn, entity.ModifiedOn);
        }

        public async Task<bool> DeactivateAsync(int id, CancellationToken ct = default)
        {
            // Soft-delete: keep the row so historical transactions that
            // reference it still resolve correctly in reports; just hide it
            // from pickers and the active list.
            var entity = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity == null || !entity.IsActive) return false;

            entity.IsActive = false;
            entity.ModifiedOn = DateTime.UtcNow;
            _repo.Update(entity);
            await _uow.SaveChangesAsync(ct);
            return true;
        }
    }
}
