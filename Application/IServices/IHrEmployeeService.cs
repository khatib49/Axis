using Application.DTOs;

namespace Application.IServices
{
    public interface IHrEmployeeService
    {
        Task<PaginatedResponse<HrEmployeeDto>> ListAsync(string? search, bool includeInactive, int page, int pageSize, CancellationToken ct = default);
        Task<HrEmployeeDto?> GetAsync(int id, CancellationToken ct = default);
        Task<HrEmployeeDto?> GetMineAsync(CancellationToken ct = default);
        Task<BaseResponse<HrEmployeeDto>> CreateAsync(HrEmployeeCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<HrEmployeeDto>> UpdateAsync(int id, HrEmployeeUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<bool>> SetPinAsync(int id, string pin, CancellationToken ct = default);
        Task<BaseResponse<HrEmployeeDto>> LinkUserAsync(int id, int? userId, CancellationToken ct = default);
    }
}
