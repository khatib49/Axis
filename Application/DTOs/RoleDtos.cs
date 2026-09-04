namespace Application.DTOs
{
    /// <summary>One entry of the page catalog, as shown in the permissions editor.</summary>
    public record PageDto(string Key, string Label, string Group, string Path);

    /// <summary>A role with the pages it may open. BuiltIn roles can't be deleted.</summary>
    public record RoleDto(string Name, bool BuiltIn, int Users, IReadOnlyList<string> Pages);

    public record RoleCreateRequest(string Name, IReadOnlyList<string>? Pages);

    public record RolePagesRequest(IReadOnlyList<string> Pages);
}
