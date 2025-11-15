using Application.DTOs;
using Application.DTOs.RequestDto;
using Application.DTOs.ResponseDto;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Domain.Identity;
using Infrastructure.IRepositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

            // --- Basic profile updates ---
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
                user.DisplayName = request.DisplayName;

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                user.Email = request.Email;
                user.UserName = request.Email;
            }

            if (request.StatusId.HasValue)
                user.StatusId = request.StatusId.Value;

            // --- Save user details ---
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return false;

            // --- Update password (if provided) ---
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                // Remove old password if it exists (Identity requires one at a time)
                var hasPassword = await _userManager.HasPasswordAsync(user);
                IdentityResult passResult;
                if (hasPassword)
                {
                    await _userManager.RemovePasswordAsync(user);
                    passResult = await _userManager.AddPasswordAsync(user, request.Password);
                }
                else
                {
                    passResult = await _userManager.AddPasswordAsync(user, request.Password);
                }

                if (!passResult.Succeeded)
                    return false;
            }

            // --- Update roles ---
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

            // --- Return user dto (if needed) ---
            var updatedRoles = (await _userManager.GetRolesAsync(user)).ToList();
            _mapper.ToDto(user, updatedRoles);

            return true;
        }

        public async Task<ClientUserResponse> CreateClient(ClientUserCreateRequest request, CancellationToken ct = default)
        {
            var phone = request.PhoneNumber.Trim();

            // 1) Check if user already exists by phone
            var existingUser = await _userManager.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.PhoneNumber == phone, ct);

            if (existingUser != null)
            {
                return new ClientUserResponse
                {
                    Id = existingUser.Id,
                    PhoneNumber = existingUser.PhoneNumber!,
                    FirstName = existingUser.FirstName,
                    LastName = existingUser.LastName,
                    DisplayName = existingUser.DisplayName,
                    IsNewlyCreated = false
                };
            }

            // 2) Create new client user
            var user = new AppUser
            {
                UserName = phone,                // use phone as username
                PhoneNumber = phone,
                FirstName = request.FirstName,
                LastName = request.LastName,
                DisplayName = $"{request.FirstName} {request.LastName}".Trim(),
                StatusId = (int)UserStatus.Active
            };

            // No password for now (e.g. login via OTP / external auth)
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to create client user: {errors}");
            }

            // 3) Assign "Client" role (make sure it exists in your seed)
            var roleResult = await _userManager.AddToRoleAsync(user, "Client");
            if (!roleResult.Succeeded)
            {
                var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to assign Client role: {errors}");
            }

            return new ClientUserResponse
            {
                Id = user.Id,
                PhoneNumber = user.PhoneNumber!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                IsNewlyCreated = true
            };
        }

    }
}
