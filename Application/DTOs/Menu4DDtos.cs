namespace Application.DTOs
{
    /// <summary>
    /// One labelled part of a dish. X/Y: centre of the part on the photo, in percent
    /// (0,0 = top-left). Pos3D / Normal3D: where the label sits on the 3D model,
    /// as model-viewer hotspot strings ("0.01m 0.05m 0.02m"); empty until placed.
    /// </summary>
    public record Dish4DPart(string Name, string? Detail, double X, double Y, string? Pos3D = null, string? Normal3D = null);

    public record Item4DRowDto(
        int ItemId, string ItemName, string? ImagePath, string? Category, string? Description,
        List<Dish4DPart> Parts, bool PartsPublished, decimal? RealSizeCm,
        string GenStatus, int GenProgress, string? GenError, DateTime? GenFinishedOn,
        bool HasModel, bool ModelActive, bool ModelIsAi);

    public record Item4DPublicDto(int ItemId, List<Dish4DPart> Parts, decimal? RealSizeCm);

    public record Item4DUpdateDto(List<Dish4DPart> Parts, bool Published, decimal? RealSizeCm);

    public record Menu4DStatusDto(
        bool AiReady, bool Make3DReady, int ItemsWithPhoto, int WithParts, int Published,
        int WithModel, int Queued, int Running, int Failed, decimal EstimatedCostPerModelUsd);

    public record Menu4DBulkResultDto(int Queued, string Message);
}
