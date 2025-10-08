using Domain.Identity;

namespace Domain.Entities
{
    public class UserCard
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public int CardId { get; set; }
        public Card Card { get; set; } = default!;
    }
}
