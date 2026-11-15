using Application.DTOs;

namespace Application.IServices
{
    /// <summary>The 4D menu: labelled dish parts (AI from the photo) and AI photo-to-3D models for table AR.</summary>
    public interface IMenu4DService
    {
        Task<List<Item4DRowDto>> ListAsync(CancellationToken ct = default);
        Task<Menu4DStatusDto> StatusAsync(CancellationToken ct = default);
        Task<List<Item4DPublicDto>> ListPublicAsync(CancellationToken ct = default);

        /// <summary>Ask the AI to find and label the visible parts of the item's photo.</summary>
        Task<BaseResponse<Item4DRowDto>> AnalyzeAsync(int itemId, CancellationToken ct = default);
        /// <summary>How many items with a photo have no parts yet (the background job labels them).</summary>
        Task<int> CountUnlabelledAsync(CancellationToken ct = default);
        Task<BaseResponse<Item4DRowDto>> UpdateAsync(int itemId, Item4DUpdateDto dto, CancellationToken ct = default);
        Task<Menu4DBulkResultDto> PublishAllAsync(CancellationToken ct = default);

        /// <summary>Queue the AI photo-to-3D job for one item.</summary>
        Task<BaseResponse<Item4DRowDto>> QueueModelAsync(int itemId, CancellationToken ct = default);
        /// <summary>Queue the job for every item with a photo and no model.</summary>
        Task<Menu4DBulkResultDto> QueueAllModelsAsync(CancellationToken ct = default);

        /// <summary>Background: label all unlabelled items (called by the queued job).</summary>
        Task<int> AnalyzeAllAsync(CancellationToken ct = default);
        /// <summary>Background: submit queued jobs and collect finished models.</summary>
        Task<int> ProcessModelQueueAsync(CancellationToken ct = default);
    }

    /// <summary>Reads an item photo (relative path under wwwroot, or a full URL) as bytes.</summary>
    public interface IMediaFileReader
    {
        Task<(byte[] Bytes, string MediaType)?> ReadAsync(string pathOrUrl, CancellationToken ct = default);
    }
}
