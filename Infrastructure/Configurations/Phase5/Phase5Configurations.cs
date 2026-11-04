using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Phase5
{
    public class TournamentConfiguration : IEntityTypeConfiguration<Tournament>
    {
        public void Configure(EntityTypeBuilder<Tournament> b) { b.HasIndex(x => x.PublicToken).IsUnique(); b.HasIndex(x => x.StartsOn); }
    }
    public class TournamentParticipantConfiguration : IEntityTypeConfiguration<TournamentParticipant>
    {
        public void Configure(EntityTypeBuilder<TournamentParticipant> b)
        {
            b.HasIndex(x => x.TournamentId);
            b.HasOne<Tournament>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
        }
    }
    public class TournamentMatchConfiguration : IEntityTypeConfiguration<TournamentMatch>
    {
        public void Configure(EntityTypeBuilder<TournamentMatch> b)
        {
            b.HasIndex(x => new { x.TournamentId, x.Round, x.Slot }).IsUnique();
            b.HasOne<Tournament>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
        }
    }
    public class StationBookingConfiguration : IEntityTypeConfiguration<StationBooking>
    {
        public void Configure(EntityTypeBuilder<StationBooking> b)
        {
            b.HasIndex(x => x.Token).IsUnique();
            b.HasIndex(x => new { x.SetId, x.StartsOn });
            b.HasOne<Set>().WithMany().HasForeignKey(x => x.SetId).OnDelete(DeleteBehavior.Cascade);
        }
    }
    public class StationCodeConfiguration : IEntityTypeConfiguration<StationCode>
    {
        public void Configure(EntityTypeBuilder<StationCode> b)
        {
            b.HasIndex(x => x.Token).IsUnique();
            b.HasIndex(x => x.SetId).IsUnique();
            b.HasOne<Set>().WithMany().HasForeignKey(x => x.SetId).OnDelete(DeleteBehavior.Cascade);
        }
    }
    public class OwnerBotChatConfiguration : IEntityTypeConfiguration<OwnerBotChat>
    {
        public void Configure(EntityTypeBuilder<OwnerBotChat> b) { b.HasIndex(x => new { x.Channel, x.ChatId }).IsUnique(); }
    }
    public class ParentLinkConfiguration : IEntityTypeConfiguration<ParentLink>
    {
        public void Configure(EntityTypeBuilder<ParentLink> b)
        {
            b.HasIndex(x => x.ChildUserId).IsUnique();
            b.HasIndex(x => x.Token).IsUnique();
        }
    }
    public class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
    {
        public void Configure(EntityTypeBuilder<AlertRule> b) { b.HasIndex(x => x.IsActive); }
    }
    public class AlertEventConfiguration : IEntityTypeConfiguration<AlertEvent>
    {
        public void Configure(EntityTypeBuilder<AlertEvent> b)
        {
            b.HasIndex(x => new { x.RuleId, x.FiredOn });
            b.HasOne<AlertRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        }
    }
    public class AiImportDraftConfiguration : IEntityTypeConfiguration<AiImportDraft>
    {
        public void Configure(EntityTypeBuilder<AiImportDraft> b) { b.HasIndex(x => x.CreatedOn); }
    }
}
