namespace Application.Common
{
    /// <summary>
    /// Who is calling, read from the JWT and request headers. Shared by the
    /// new modules so each one does not re-implement claim parsing.
    /// </summary>
    public interface ICurrentUserAccessor
    {
        /// <summary>Identity user id (NameIdentifier / sub), or null when anonymous.</summary>
        int? UserId { get; }
        /// <summary>User name / email for audit columns. "system" when anonymous.</summary>
        string UserName { get; }
        /// <summary>Role claims of the caller.</summary>
        IReadOnlyList<string> Roles { get; }
        /// <summary>Optional X-Branch-Id header the dashboard sends once a branch is selected.</summary>
        int? BranchId { get; }
        bool IsInRole(string role);
    }
}
