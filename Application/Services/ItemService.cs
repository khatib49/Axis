using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Application.Services
{
    public class ItemService : IItemService
    {
        private readonly IBaseRepository<Item> _repo;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;
        private readonly IImageStorageService _imageStorage;

        public ItemService(IBaseRepository<Item> repo, IUnitOfWork uow, DomainMapper mapper, IImageStorageService imageStorage)
        {
            _repo = repo; _uow = uow; _mapper = mapper;
            _imageStorage = imageStorage;
        }

        public async Task<ItemDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: true, ct);
            return e is null ? null : _mapper.ToDto(e);
        }

        public async Task<PaginatedResponse<ItemDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            
            var query = _repo.QueryableAsync(null, asNoTracking: true);

         
            if (pagination.CategoryId.HasValue)
            {
                query = query.Where(x => x.CategoryId == pagination.CategoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(pagination.search))
            {
                query = query.Where(x => x.Name.Contains(pagination.search));
            }

            
            var totalCount = await query.CountAsync(ct);

           
            var pagedList = await query
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToListAsync(ct);

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<ItemDto>(totalCount, result, pagination.Page, pagination.PageSize);
        }

        public async Task<ItemDto> CreateAsync(ItemCreateDto dto, CancellationToken ct = default)
        {
            var e = _mapper.ToEntity(dto);
            if (dto.Image != null)
            {
                e.ImagePath = await _imageStorage.SaveImageAsync(dto.Image, ct);
            }
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }

        public async Task<bool> UpdateAsync(int id, ItemUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _mapper.MapTo(dto, e); // updates only non-null fields
            if (dto.Image != null)
            {
                e.ImagePath = await _imageStorage.SaveImageAsync(dto.Image, ct);
            }
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

        public async Task<PaginatedResponse<ItemDto>> GetByCategoryIdAsync(int id, BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            var list = await _repo.ListAsync(i => i.CategoryId == id, asNoTracking: true, ct);
            var totalCount = list.Count();

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<ItemDto>(totalCount, result, pagination.Page, pagination.PageSize);
        }
    }
}
