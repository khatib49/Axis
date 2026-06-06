namespace Application.DTOs
{
    public record ChannelDto(
        int Id,
        string Name,
        string? Description,
        bool IsActive,
        DateTime CreatedOn,
        DateTime? ModifiedOn
    );

    public record ChannelCreateDto(
        string Name,
        string? Description
    );

    public record ChannelUpdateDto(
        string Name,
        string? Description,
        bool IsActive
    );
}
