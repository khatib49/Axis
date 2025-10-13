namespace Domain.Entities
{
    public class Setting
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string Type { get; set; } = default!;

        public decimal Hours { get; set; } = 0;
        public decimal Price { get; set; } = 0;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; } = null;
        public string CreatedBy { get; set; } = default!;
        public string? ModifiedBy { get; set; } = null;


        public int GameId { get; set; }
        public Game Game { get; set; } = default!;
        public bool IsOffer { get; set; } = false;
        public bool IsOpenHour { get; set; } = false;



    }
}
