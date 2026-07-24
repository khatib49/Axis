namespace Application.DTOs
{
    /// <summary>
    /// DTO for displaying role-category mapping
    /// </summary>
    public record RoleCategoryDto(
        int Id,
        string RoleName,
        int CategoryId,
        string CategoryName,
        string CategoryType,
        bool IsActive,
        DateTime CreatedOn,
        string CreatedBy
    );

    /// <summary>
    /// DTO for creating a new role-category mapping
    /// </summary>
    public record RoleCategoryCreateDto(
        string RoleName,        // "chef", "bartender", etc.
        int CategoryId
    );

    /// <summary>
    /// DTO for bulk assigning categories to a role
    /// </summary>
    public record RoleCategoryBulkAssignDto(
        string RoleName,
        List<int> CategoryIds   // Replace all existing mappings with these
    );

    /// <summary>
    /// DTO for getting role configuration
    /// Shows which categories a role can see
    /// </summary>
    public record RoleConfigurationDto(
        string RoleName,
        List<CategorySummaryDto> AssignedCategories,
        List<CategorySummaryDto> AvailableCategories
    );

    /// <summary>
    /// Summary of a category
    /// </summary>
    public record CategorySummaryDto(
        int Id,
        string Name,
        string Type,
        string? ItemType
    );

    /// <summary>
    /// Response with all role configurations
    /// </summary>
    public record AllRoleConfigurationsDto(
        List<RoleConfigurationDto> Roles
    );
}