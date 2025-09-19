using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;

namespace Application.Services
{
    public class ItemService : IItemService
    {
        private readonly IBaseRepository<Item> _repo;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;

        public ItemService(IBaseRepository<Item> repo, IUnitOfWork uow, DomainMapper mapper)
        {
            _repo = repo; _uow = uow; _mapper = mapper;
        }

        public async Task<ItemDto?> GetAsync(Guid id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: true, ct);
            return e is null ? null : _mapper.ToDto(e);
        }

        public async Task<PaginatedResponse<ItemDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            var list = await _repo.ListAsync(null, asNoTracking: true, ct);
            var totalCount = list.Count();

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<ItemDto>(totalCount, result);
        }

        public async Task<ItemDto> CreateAsync(ItemCreateDto dto, CancellationToken ct = default)
        {
            var e = _mapper.ToEntity(dto);
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }

        public async Task<bool> UpdateAsync(Guid id, ItemUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _mapper.MapTo(dto, e); // updates only non-null fields
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _repo.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }
    }
}
