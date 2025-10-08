using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;

namespace Application.Services
{
    public class ExpenseService : IExpenseService
    {
        private readonly IBaseRepository<Expense> _repo;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;

        public ExpenseService(IBaseRepository<Expense> repo, IUnitOfWork uow, DomainMapper mapper)
        {
            _repo = repo; _uow = uow; _mapper = mapper;
        }

        public async Task<ExpenseDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: true, ct);
            return e is null ? null : _mapper.ToDto(e);
        }

        public async Task<PaginatedResponse<ExpenseDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            var list = await _repo.ListAsync(null, asNoTracking: true, ct);
            var totalCount = list.Count();

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<ExpenseDto>(totalCount, result, pagination.Page, pagination.PageSize); ;
        }

        public async Task<ExpenseDto> CreateAsync(ExpenseCreateDto dto, CancellationToken ct = default)
        {
            var e = _mapper.ToEntity(dto);
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }

        public async Task<bool> UpdateAsync(int id, ExpenseUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

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
