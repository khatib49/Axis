using Application.DTOs;

namespace Application.IServices
{
    public interface IReservationService
    {
        Task<ReservationDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<ReservationDto>> ListAsync(ReservationListFilterDto filter, CancellationToken ct = default);
        Task<ReservationBoardDto> TodayBoardAsync(DateTime? day, CancellationToken ct = default);
        Task<BaseResponse<ReservationDto>> CreateAsync(ReservationUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<ReservationDto>> UpdateAsync(int id, ReservationUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default);
        Task<AvailabilityResultDto> CheckAvailabilityAsync(AvailabilityQueryDto q, CancellationToken ct = default);
        Task<BaseResponse<ReservationDto>> ConfirmAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<ReservationDto>> SeatAsync(int id, SeatReservationRequestDto dto, CancellationToken ct = default);
        Task<BaseResponse<ReservationDto>> NoShowAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<ReservationDto>> CancelAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<ReservationDto>> CompleteAsync(int id, CancellationToken ct = default);
        /// <summary>Anonymous online request: Source=Online, Status=Pending, max 3 pending per phone.</summary>
        Task<BaseResponse<ReservationDto>> PublicRequestAsync(PublicReservationRequestDto dto, CancellationToken ct = default);
    }
}
