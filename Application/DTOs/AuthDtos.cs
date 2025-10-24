namespace Application.DTOs
{
    public record RegisterRequest(string Email, string Password, string? DisplayName, string RoleName);
    public record LoginRequest(string Email, string Password);
    public record AuthResponse(bool Success, string? Token, string? Error);
    public record BaseResponse(bool Success, string? Error);

    public record UserDto(int Id, string? Email, string? DisplayName, List<string> Roles, int StatusId );
    public record UserUpdateDto(string? DisplayName, string? Email, List<string>? Roles, int? StatusId);


}
