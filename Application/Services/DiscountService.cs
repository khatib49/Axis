using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;

namespace Application.Services
{
    public class DiscountService : IDiscountService
    {
        private readonly IBaseRepository<Discount> _repo;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;
        public DiscountService(IBaseRepository<Discount> repo, IUnitOfWork uow, DomainMapper mapper)
        {
            _repo = repo;
            _uow = uow;
            _mapper = mapper;
        }

        public async Task<DiscountDto> CreateAsync(DiscountCreateDto dto, CancellationToken ct = default)
        {
            var e = _mapper.ToEntity(dto);
            e.CreatedOn = DateTime.UtcNow;
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _repo.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<DiscountDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: true, ct);
            return e is null ? null : _mapper.ToDto(e);
        }

        public async Task<PaginatedResponse<DiscountDto>> GetByTypeAsync(string type, BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            var list = await _repo.ListAsync(i => i.Type.Trim().ToLower().Equals(type.Trim().ToLower()), asNoTracking: true, ct);
            var totalCount = list.Count();

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<DiscountDto>(totalCount, result, pagination.Page, pagination.PageSize);
        }

        public async Task<PaginatedResponse<DiscountDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            var list = await _repo.ListAsync(null, asNoTracking: true, ct);
            var totalCount = list.Count();

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<DiscountDto>(totalCount, result, pagination.Page, pagination.PageSize);
        }

        public async Task<bool> UpdateAsync(int id, DiscountUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;
            e.UpdatedOn = DateTime.UtcNow;

            if (dto.Name != null) e.Name = dto.Name;
            if (dto.Type != null) e.Type = dto.Type;
            if (dto.Description != null) e.Description = dto.Description;
            if (dto.Percentage.HasValue) e.Percentage = dto.Percentage.Value;
            if (dto.IsActive.HasValue) e.IsActive = dto.IsActive.Value;

            await _uow.SaveChangesAsync(ct);
            return true;
        }
    }
}
