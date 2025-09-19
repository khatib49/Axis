using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;

namespace Application.Services
{
    public class StatusService : IStatusService
    {
        private readonly IBaseRepository<Status> _repo;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;

        public StatusService(IBaseRepository<Status> repo, IUnitOfWork uow, DomainMapper mapper)
        {
            _repo = repo; _uow = uow; _mapper = mapper;
        }

        public async Task<StatusDto?> GetAsync(Guid id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: true, ct);
            return e is null ? null : _mapper.ToDto(e);
        }

        public async Task<PaginatedResponse<StatusDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            var list = await _repo.ListAsync(null, asNoTracking: true, ct);
            var totalCount = list.Count();

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<StatusDto>(totalCount, result);
        }

        public async Task<StatusDto> CreateAsync(StatusCreateDto dto, CancellationToken ct = default)
        {
            var e = _mapper.ToEntity(dto);
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }

        public async Task<bool> UpdateAsync(Guid id, StatusUpdateDto dto, CancellationToken ct = default)
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
