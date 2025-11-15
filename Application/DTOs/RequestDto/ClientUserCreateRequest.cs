namespace Application.DTOs.RequestDto
{
    public class ClientUserCreateRequest
    {
        public string PhoneNumber { get; set; } = default!;
        public string FirstName { get; set; } = default!;
        public string LastName { get; set; } = default!;
    }
}
