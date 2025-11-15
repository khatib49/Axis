namespace Application.DTOs.ResponseDto
{
    public class ClientUserResponse
    {
        public int Id { get; set; }
        public string PhoneNumber { get; set; } = default!;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }

        /// <summary>
        /// true  => user was created now  
        /// false => user already existed, returned as-is
        /// </summary>
        public bool IsNewlyCreated { get; set; }
    }
}
