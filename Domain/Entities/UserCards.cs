using Domain.Identity;

namespace Domain.Entities
{
    public class UserCard
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public Guid CardId { get; set; }
        public Card Card { get; set; } = default!;
    }
}
