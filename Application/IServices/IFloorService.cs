using Application.DTOs;

namespace Application.IServices
{
    /// <summary>Floor plan admin (areas, tables, layout) and the live layout for the till.</summary>
    public interface IFloorService
    {
        Task<List<FloorAreaDto>> ListAreasAsync(bool includeInactive, CancellationToken ct = default);
        Task<FloorAreaDto> CreateAreaAsync(FloorAreaUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<FloorAreaDto>> UpdateAreaAsync(int id, FloorAreaUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteAreaAsync(int id, CancellationToken ct = default);

        Task<List<DiningTableDto>> ListTablesAsync(int? areaId, bool includeInactive, CancellationToken ct = default);
        Task<BaseResponse<DiningTableDto>> CreateTableAsync(DiningTableUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<DiningTableDto>> UpdateTableAsync(int id, DiningTableUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteTableAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<int>> SaveLayoutAsync(BulkLayoutSaveDto dto, CancellationToken ct = default);

        /// <summary>Areas + tables + the open session summary of each table (for the till floor view).</summary>
        Task<FloorLayoutDto> GetLayoutAsync(CancellationToken ct = default);
    }
}
