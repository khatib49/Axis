using Application.DTOs;

namespace Application.IServices
{
    public interface IMarketingSegmentService
    {
        Task<IReadOnlyList<SegmentDto>> ListAsync(CancellationToken ct = default);
        Task<SegmentDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<SegmentDto>> CreateAsync(SegmentCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<SegmentDto>> UpdateAsync(int id, SegmentUpdateDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default);
        /// <summary>Count + first 50 rows of a saved segment's audience.</summary>
        Task<BaseResponse<AudiencePreviewDto>> PreviewAsync(int id, CancellationToken ct = default);
        /// <summary>Count + first 50 rows for ad-hoc rules (live rule builder).</summary>
        Task<AudiencePreviewDto> PreviewRulesAsync(SegmentRulesDto rules, CancellationToken ct = default);
        /// <summary>Full audience (used by the campaign sender).</summary>
        Task<IReadOnlyList<AudienceRowDto>> BuildAudienceAsync(SegmentRulesDto rules, CancellationToken ct = default);
    }
}
