using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class RoleCategoryController : ControllerBase
    {
        private readonly IRoleCategoryService _roleCategoryService;

        public RoleCategoryController(IRoleCategoryService roleCategoryService)
        {
            _roleCategoryService = roleCategoryService;
        }

        /// <summary>
        /// Get all role-category mappings
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<RoleCategoryDto>>> GetAllMappings(CancellationToken ct)
        {
            var mappings = await _roleCategoryService.GetAllMappingsAsync(ct);
            return Ok(mappings);
        }

        /// <summary>
        /// Get configuration for all roles (dashboard view)
        /// Shows what categories each role can see
        /// </summary>
        [HttpGet("configurations")]
        public async Task<ActionResult<AllRoleConfigurationsDto>> GetAllConfigurations(CancellationToken ct)
        {
            var configurations = await _roleCategoryService.GetAllRoleConfigurationsAsync(ct);
            return Ok(configurations);
        }

        /// <summary>
        /// Get configuration for a specific role
        /// Shows assigned and available categories
        /// </summary>
        [HttpGet("configurations/{roleName}")]
        public async Task<ActionResult<RoleConfigurationDto>> GetRoleConfiguration(
            [FromRoute] string roleName,
            CancellationToken ct)
        {
            var config = await _roleCategoryService.GetRoleConfigurationAsync(roleName, ct);
            return Ok(config);
        }

        /// <summary>
        /// Assign a single category to a role
        /// </summary>
        [HttpPost("assign")]
        public async Task<ActionResult<RoleCategoryDto>> AssignCategoryToRole(
            [FromBody] RoleCategoryCreateDto dto,
            CancellationToken ct)
        {
            var userName = User?.Identity?.Name ?? "admin";
            var result = await _roleCategoryService.AssignCategoryToRoleAsync(dto, userName, ct);
            return Ok(result);
        }

        /// <summary>
        /// Bulk assign categories to a role (replaces all existing)
        /// Use this to configure a role's categories all at once
        /// </summary>
        [HttpPost("bulk-assign")]
        public async Task<ActionResult<List<RoleCategoryDto>>> BulkAssignCategoriesToRole(
            [FromBody] RoleCategoryBulkAssignDto dto,
            CancellationToken ct)
        {
            var userName = User?.Identity?.Name ?? "admin";
            var result = await _roleCategoryService.BulkAssignCategoriesToRoleAsync(dto, userName, ct);
            return Ok(result);
        }

        /// <summary>
        /// Remove a specific category from a role
        /// </summary>
        [HttpDelete("{mappingId}")]
        public async Task<ActionResult> RemoveCategoryFromRole(
            [FromRoute] int mappingId,
            CancellationToken ct)
        {
            var success = await _roleCategoryService.RemoveCategoryFromRoleAsync(mappingId, ct);

            if (!success)
            {
                return NotFound(new { message = "Mapping not found" });
            }

            return Ok(new { message = "Category removed from role successfully" });
        }

        /// <summary>
        /// Remove all categories from a role
        /// </summary>
        [HttpDelete("role/{roleName}")]
        public async Task<ActionResult> RemoveAllCategoriesFromRole(
            [FromRoute] string roleName,
            CancellationToken ct)
        {
            var success = await _roleCategoryService.RemoveAllCategoriesFromRoleAsync(roleName, ct);

            if (!success)
            {
                return NotFound(new { message = "No mappings found for this role" });
            }

            return Ok(new { message = $"All categories removed from {roleName} role" });
        }
    }
}