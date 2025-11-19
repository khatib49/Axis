namespace Application.DTOs.RequestDto
{
    public class ClientUserUpdateRequest
    {
        public string? PhoneNumber { get; set; }   // optional; if sent, we validate + update
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
    }
}
