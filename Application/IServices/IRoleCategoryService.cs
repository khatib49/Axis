using Application.DTOs;

namespace Application.IServices
{
    public interface IRoleCategoryService
    {
        /// <summary>
        /// Get all role-category mappings
        /// </summary>
        Task<List<RoleCategoryDto>> GetAllMappingsAsync(CancellationToken ct = default);

        /// <summary>
        /// Get mappings for a specific role
        /// </summary>
        Task<List<RoleCategoryDto>> GetMappingsByRoleAsync(string roleName, CancellationToken ct = default);

        /// <summary>
        /// Get configuration for a specific role (assigned + available categories)
        /// </summary>
        Task<RoleConfigurationDto> GetRoleConfigurationAsync(string roleName, CancellationToken ct = default);

        /// <summary>
        /// Get all role configurations (for admin dashboard)
        /// </summary>
        Task<AllRoleConfigurationsDto> GetAllRoleConfigurationsAsync(CancellationToken ct = default);

        /// <summary>
        /// Assign a single category to a role
        /// </summary>
        Task<RoleCategoryDto> AssignCategoryToRoleAsync(RoleCategoryCreateDto dto, string createdBy, CancellationToken ct = default);

        /// <summary>
        /// Bulk assign categories to a role (replaces existing mappings)
        /// </summary>
        Task<List<RoleCategoryDto>> BulkAssignCategoriesToRoleAsync(RoleCategoryBulkAssignDto dto, string createdBy, CancellationToken ct = default);

        /// <summary>
        /// Remove a category from a role
        /// </summary>
        Task<bool> RemoveCategoryFromRoleAsync(int mappingId, CancellationToken ct = default);

        /// <summary>
        /// Remove all categories from a role
        /// </summary>
        Task<bool> RemoveAllCategoriesFromRoleAsync(string roleName, CancellationToken ct = default);

        /// <summary>
        /// Get category IDs that a role can access
        /// Used by KitchenService for filtering
        /// </summary>
        Task<List<int>> GetCategoryIdsForRoleAsync(string roleName, CancellationToken ct = default);
    }
}