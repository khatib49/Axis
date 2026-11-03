using Application.DTOs;

namespace Application.IServices
{
    public interface IHrScheduleService
    {
        Task<List<HrShiftTemplateDto>> ListTemplatesAsync(CancellationToken ct = default);
        Task<HrShiftTemplateDto> CreateTemplateAsync(HrShiftTemplateUpsertDto dto, CancellationToken ct = default);
        Task<bool> UpdateTemplateAsync(int id, HrShiftTemplateUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteTemplateAsync(int id, CancellationToken ct = default);

        Task<HrWeekViewDto> GetWeekAsync(DateOnly? weekStart, CancellationToken ct = default);
        Task<HrWeekViewDto?> GetMyWeekAsync(DateOnly? weekStart, CancellationToken ct = default);
        Task<BaseResponse<HrShiftDto>> CreateShiftAsync(HrShiftCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<HrShiftDto>> UpdateShiftAsync(int id, HrShiftUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteShiftAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<int>> CopyPreviousWeekAsync(DateOnly weekStart, CancellationToken ct = default);
    }
}
