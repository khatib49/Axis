using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class RoleCategoryService : IRoleCategoryService
    {
        private readonly IBaseRepository<RoleCategory> _roleCategoryRepo;
        private readonly IBaseRepository<Category> _categoryRepo;
        private readonly IUnitOfWork _uow;

        public RoleCategoryService(
            IBaseRepository<RoleCategory> roleCategoryRepo,
            IBaseRepository<Category> categoryRepo,
            IUnitOfWork uow)
        {
            _roleCategoryRepo = roleCategoryRepo;
            _categoryRepo = categoryRepo;
            _uow = uow;
        }

        public async Task<List<RoleCategoryDto>> GetAllMappingsAsync(CancellationToken ct = default)
        {
            var mappings = await _roleCategoryRepo.Query()
                .Include(rc => rc.Category)
                .Where(rc => rc.IsActive)
                .OrderBy(rc => rc.RoleName)
                .ThenBy(rc => rc.Category.Name)
                .ToListAsync(ct);

            return mappings.Select(rc => new RoleCategoryDto(
                Id: rc.Id,
                RoleName: rc.RoleName,
                CategoryId: rc.CategoryId,
                CategoryName: rc.Category.Name,
                CategoryType: rc.Category.Type,
                IsActive: rc.IsActive,
                CreatedOn: rc.CreatedOn,
                CreatedBy: rc.CreatedBy
            )).ToList();
        }

        public async Task<List<RoleCategoryDto>> GetMappingsByRoleAsync(string roleName, CancellationToken ct = default)
        {
            var mappings = await _roleCategoryRepo.Query()
                .Include(rc => rc.Category)
                .Where(rc => rc.RoleName.ToLower() == roleName.ToLower())
                .Where(rc => rc.IsActive)
                .OrderBy(rc => rc.Category.Name)
                .ToListAsync(ct);

            return mappings.Select(rc => new RoleCategoryDto(
                Id: rc.Id,
                RoleName: rc.RoleName,
                CategoryId: rc.CategoryId,
                CategoryName: rc.Category.Name,
                CategoryType: rc.Category.Type,
                IsActive: rc.IsActive,
                CreatedOn: rc.CreatedOn,
                CreatedBy: rc.CreatedBy
            )).ToList();
        }

        public async Task<RoleConfigurationDto> GetRoleConfigurationAsync(string roleName, CancellationToken ct = default)
        {
            // Get assigned categories
            var assignedCategoryIds = await _roleCategoryRepo.Query()
                .Where(rc => rc.RoleName.ToLower() == roleName.ToLower())
                .Where(rc => rc.IsActive)
                .Select(rc => rc.CategoryId)
                .ToListAsync(ct);

            var assignedCategories = await _categoryRepo.Query()
                .Where(c => assignedCategoryIds.Contains(c.Id))
                .Select(c => new CategorySummaryDto(c.Id, c.Name, c.Type, c.ItemType))
                .ToListAsync(ct);

            // Get available categories (item categories not assigned)
            var availableCategories = await _categoryRepo.Query()
                .Where(c => c.Type.ToLower() == "item") // Only item categories
                .Where(c => !assignedCategoryIds.Contains(c.Id))
                .Select(c => new CategorySummaryDto(c.Id, c.Name, c.Type, c.ItemType))
                .ToListAsync(ct);

            return new RoleConfigurationDto(
                RoleName: roleName,
                AssignedCategories: assignedCategories,
                AvailableCategories: availableCategories
            );
        }

        public async Task<AllRoleConfigurationsDto> GetAllRoleConfigurationsAsync(CancellationToken ct = default)
        {
            var roles = new[] { "chef", "bartender" }; // Add more roles as needed
            var configurations = new List<RoleConfigurationDto>();

            foreach (var role in roles)
            {
                var config = await GetRoleConfigurationAsync(role, ct);
                configurations.Add(config);
            }

            return new AllRoleConfigurationsDto(Roles: configurations);
        }

        public async Task<RoleCategoryDto> AssignCategoryToRoleAsync(
            RoleCategoryCreateDto dto,
            string createdBy,
            CancellationToken ct = default)
        {
            // Check if mapping already exists
            var existing = await _roleCategoryRepo.Query()
                .FirstOrDefaultAsync(rc =>
                    rc.RoleName.ToLower() == dto.RoleName.ToLower() &&
                    rc.CategoryId == dto.CategoryId, ct);

            if (existing != null)
            {
                if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    existing.ModifiedOn = DateTime.UtcNow;
                    existing.ModifiedBy = createdBy;
                    _roleCategoryRepo.Update(existing);
                    await _uow.SaveChangesAsync(ct);
                }

                var category = await _categoryRepo.Query()
                    .FirstAsync(c => c.Id == existing.CategoryId, ct);

                return new RoleCategoryDto(
                    Id: existing.Id,
                    RoleName: existing.RoleName,
                    CategoryId: existing.CategoryId,
                    CategoryName: category.Name,
                    CategoryType: category.Type,
                    IsActive: existing.IsActive,
                    CreatedOn: existing.CreatedOn,
                    CreatedBy: existing.CreatedBy
                );
            }

            // Create new mapping
            var roleCategory = new RoleCategory
            {
                RoleName = dto.RoleName.ToLower(),
                CategoryId = dto.CategoryId,
                IsActive = true,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = createdBy
            };

            await _roleCategoryRepo.AddAsync(roleCategory, ct);
            await _uow.SaveChangesAsync(ct);

            var newCategory = await _categoryRepo.Query()
                .FirstAsync(c => c.Id == roleCategory.CategoryId, ct);

            return new RoleCategoryDto(
                Id: roleCategory.Id,
                RoleName: roleCategory.RoleName,
                CategoryId: roleCategory.CategoryId,
                CategoryName: newCategory.Name,
                CategoryType: newCategory.Type,
                IsActive: roleCategory.IsActive,
                CreatedOn: roleCategory.CreatedOn,
                CreatedBy: roleCategory.CreatedBy
            );
        }

        public async Task<List<RoleCategoryDto>> BulkAssignCategoriesToRoleAsync(
            RoleCategoryBulkAssignDto dto,
            string createdBy,
            CancellationToken ct = default)
        {
            // Remove all existing mappings for this role
            var existing = await _roleCategoryRepo.Query()
                .Where(rc => rc.RoleName.ToLower() == dto.RoleName.ToLower())
                .ToListAsync(ct);

            foreach (var mapping in existing)
            {
                _roleCategoryRepo.Remove(mapping);
            }

            // Add new mappings
            var newMappings = new List<RoleCategory>();
            foreach (var categoryId in dto.CategoryIds)
            {
                newMappings.Add(new RoleCategory
                {
                    RoleName = dto.RoleName.ToLower(),
                    CategoryId = categoryId,
                    IsActive = true,
                    CreatedOn = DateTime.UtcNow,
                    CreatedBy = createdBy
                });
            }

            foreach (var mapping in newMappings)
            {
                await _roleCategoryRepo.AddAsync(mapping, ct);
            }

            await _uow.SaveChangesAsync(ct);

            // Return the new mappings
            return await GetMappingsByRoleAsync(dto.RoleName, ct);
        }

        public async Task<bool> RemoveCategoryFromRoleAsync(int mappingId, CancellationToken ct = default)
        {
            var mapping = await _roleCategoryRepo.Query()
                .FirstOrDefaultAsync(rc => rc.Id == mappingId, ct);

            if (mapping == null)
                return false;

            _roleCategoryRepo.Remove(mapping);
            await _uow.SaveChangesAsync(ct);

            return true;
        }

        public async Task<bool> RemoveAllCategoriesFromRoleAsync(string roleName, CancellationToken ct = default)
        {
            var mappings = await _roleCategoryRepo.Query()
                .Where(rc => rc.RoleName.ToLower() == roleName.ToLower())
                .ToListAsync(ct);

            if (!mappings.Any())
                return false;

            foreach (var mapping in mappings)
            {
                _roleCategoryRepo.Remove(mapping);
            }

            await _uow.SaveChangesAsync(ct);

            return true;
        }

        public async Task<List<int>> GetCategoryIdsForRoleAsync(string roleName, CancellationToken ct = default)
        {
            return await _roleCategoryRepo.Query()
                .Where(rc => rc.RoleName.ToLower() == roleName.ToLower())
                .Where(rc => rc.IsActive)
                .Select(rc => rc.CategoryId)
                .ToListAsync(ct);
        }
    }
}