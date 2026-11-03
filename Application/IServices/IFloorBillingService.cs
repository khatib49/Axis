using Application.DTOs;

namespace Application.IServices
{
    /// <summary>Course sequencing, bill splits and tips for an invoice.</summary>
    public interface IFloorBillingService
    {
        // Courses
        Task<List<OrderCourseDto>> ListCoursesAsync(int transactionId, CancellationToken ct = default);
        Task<BaseResponse<List<OrderCourseDto>>> SetCoursesAsync(int transactionId, SetCoursesRequestDto dto, CancellationToken ct = default);
        Task<BaseResponse<List<OrderCourseDto>>> FireCourseAsync(int transactionId, int courseNumber, CancellationToken ct = default);

        // Splits
        Task<BaseResponse<BillSplitListDto>> ListSplitsAsync(int transactionId, CancellationToken ct = default);
        Task<BaseResponse<BillSplitListDto>> CreateEqualSplitsAsync(int transactionId, int ways, CancellationToken ct = default);
        Task<BaseResponse<BillSplitListDto>> CreateCustomSplitsAsync(int transactionId, CustomSplitRequestDto dto, CancellationToken ct = default);
        Task<BaseResponse<BillSplitListDto>> PaySplitAsync(int splitId, PaySplitRequestDto dto, CancellationToken ct = default);
        Task<BaseResponse<BillSplitListDto>> DeleteSplitAsync(int splitId, CancellationToken ct = default);

        // Tips
        Task<List<TipDto>> ListTipsAsync(int transactionId, CancellationToken ct = default);
        Task<BaseResponse<TipDto>> AddTipAsync(int transactionId, TipCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteTipAsync(int tipId, CancellationToken ct = default);
        Task<DailyTipsReportDto> DailyTipsReportAsync(DateTime day, CancellationToken ct = default);
    }
}
