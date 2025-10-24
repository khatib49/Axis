using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Domain.Identity;
using Infrastructure.IRepositories;
using Microsoft.AspNetCore.Identity;

namespace Application.Services
{
    public class UsersService : IUsersService
    {
        private readonly IBaseRepository<AppUser> _repo;
        private readonly UserManager<AppUser> _userManager;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;
        public UsersService(IBaseRepository<AppUser> repo, IUnitOfWork uow, DomainMapper mapper, UserManager<AppUser> userManager)
        {
            _repo = repo; _uow = uow; _mapper = mapper; _userManager = userManager;
        }
        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _repo.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<UserDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: true, ct);
            if (e is null) return null;

            var roles = (await _userManager.GetRolesAsync(e)).ToList();
            return _mapper.ToDto(e, roles);
        }

        public async Task<PaginatedResponse<UserDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            var list = await _repo.ListAsync(null, asNoTracking: true, ct);
            var totalCount = list.Count;

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

            var result = new List<UserDto>();

            foreach (var u in pagedList)
            {
                var roles = (await _userManager.GetRolesAsync(u)).ToList();
                result.Add(_mapper.ToDto(u, roles));
            }

            return new PaginatedResponse<UserDto>(totalCount, result, pagination.Page, pagination.PageSize);
        }
        public async Task<bool> UpdateAsync(int Id, UserUpdateDto request, CancellationToken ct = default)
        {
            var user = await _userManager.FindByIdAsync(Id.ToString());
            if (user == null)
                return false;

            if (!string.IsNullOrWhiteSpace(request.DisplayName))
                user.DisplayName = request.DisplayName;

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                user.Email = request.Email;
                user.UserName = request.Email;
            }
            if (user.StatusId == (int)UserStatus.Deleted)
                return false;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return false;

            if (request.Roles is not null)
            {
                var currentRoles = await _userManager.GetRolesAsync(user);

                var toRemove = currentRoles.Except(request.Roles, StringComparer.OrdinalIgnoreCase).ToList();
                if (toRemove.Any())
                    await _userManager.RemoveFromRolesAsync(user, toRemove);

                var toAdd = request.Roles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
                if (toAdd.Any())
                    await _userManager.AddToRolesAsync(user, toAdd);
            }

            var updatedRoles = (await _userManager.GetRolesAsync(user)).ToList();
            _mapper.ToDto(user, updatedRoles);
            return true;
        }
    }
}
