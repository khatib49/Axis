namespace Application.DTOs
{
    public record RegisterRequest(string Email, string Password, string? DisplayName, string RoleName);
    public record LoginRequest(string Email, string Password);
    public record AuthResponse(bool Success, string? Token, string? Error);
    public record BaseResponse(bool Success, string? Error);
    public record UserDto(Guid Id, string? Email, string? DisplayName, List<string> Roles);
}
