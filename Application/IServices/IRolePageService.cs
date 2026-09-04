using Application.DTOs;

namespace Application.IServices
{
    public interface IRolePageService
    {
        /// <summary>Every page that can be granted, for the permissions editor.</summary>
        IReadOnlyList<PageDto> Catalog();

        /// <summary>All roles with their pages and user counts.</summary>
        Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);

        /// <summary>Creates an Identity role and grants it the given pages.</summary>
        Task<RoleDto> CreateAsync(string name, IReadOnlyList<string>? pages, CancellationToken ct = default);

        /// <summary>Replaces the pages a role may open. Not allowed for admin.</summary>
        Task<RoleDto> SetPagesAsync(string name, IReadOnlyList<string> pages, CancellationToken ct = default);

        /// <summary>Deletes a custom role that no user holds.</summary>
        Task DeleteAsync(string name, CancellationToken ct = default);

        /// <summary>Union of the pages the given roles may open (admin → every page).</summary>
        Task<IReadOnlyCollection<string>> GetPagesForRolesAsync(IEnumerable<string> roles, CancellationToken ct = default);
    }
}
