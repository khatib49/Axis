using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class SettingService : ISettingService
    {
        private readonly IBaseRepository<Setting> _repo;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;
        public SettingService(IBaseRepository<Setting> repo, IUnitOfWork uow, DomainMapper mapper)
        {
            _repo = repo; _uow = uow; _mapper = mapper;
        }

        public async Task<SettingDto?> GetAsync(int id, CancellationToken ct = default)
        {
            //var e = await _repo.GetByIdAsync(id, asNoTracking: true, ct);
            var e = await _repo.Query()
                    .Include(s => s.Game)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == id, ct);
            return e is null ? null : _mapper.ToDto(e);
        }

        public async Task<PaginatedResponse<SettingDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            //var list = await _repo.ListAsync(null, asNoTracking: true, ct);
            var list = await _repo.Query()
                        .Include(s => s.Game)
                        .AsNoTracking()
                        .ToListAsync(ct);
            var totalCount = list.Count();

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<SettingDto>(totalCount, result, pagination.Page, pagination.PageSize);
        }

        public async Task<SettingDto> CreateAsync(SettingCreateDto dto, string createdBy, CancellationToken ct = default)
        {
            var e = _mapper.ToEntity(dto);
            e.CreatedBy = createdBy ?? "";
            e.CreatedOn = DateTime.UtcNow;
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }
        public async Task<bool> UpdateAsync(int id, SettingUpdateDto dto, string? ModifiedBy, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;
            e.ModifiedBy = ModifiedBy ?? "";
            e.ModifiedOn = DateTime.UtcNow;
            _mapper.MapTo(dto, e); // updates only non-null fields
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _repo.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }
    }
}
