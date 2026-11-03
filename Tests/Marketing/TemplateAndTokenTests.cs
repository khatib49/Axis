using Application.Services.Marketing;
using FluentAssertions;
using Xunit;

namespace Tests.Marketing
{
    public class TemplateRenderingTests
    {
        [Fact]
        public void Placeholders_render_case_insensitively_and_unknown_ones_render_empty()
        {
            var values = MarketingTemplate.Values("Jane Doe", "Jane", new DateTime(2026, 10, 20), "WELCOME10", "https://u/x");
            MarketingTemplate.Render("Hi {{firstName}} ({{ NAME }}), last seen {{lastVisit}}. Code {{code}}. {{nope}}!", values)
                .Should().Be("Hi Jane (Jane Doe), last seen 2026-10-20. Code WELCOME10. !");
            MarketingTemplate.Render("{{unsubscribeUrl}}", values).Should().Be("https://u/x");
            MarketingTemplate.Render(null, values).Should().Be("");
            MarketingTemplate.Has("x {{ UnsubscribeUrl }}", "unsubscribeUrl").Should().BeTrue();
            MarketingTemplate.Has("x", "unsubscribeUrl").Should().BeFalse();
        }
    }

    public class UnsubscribeTokenTests
    {
        [Fact]
        public async Task Token_round_trips_and_tampering_is_rejected()
        {
            using var h = new MarketingHarness();
            var token = h.Unsub.CreateToken("Client@AxisLB.com ");
            h.Unsub.ParseToken(token).Should().Be("client@axislb.com");
            h.Unsub.ParseToken(token + "x").Should().BeNull();
            h.Unsub.ParseToken("abc").Should().BeNull();
            h.Unsub.ParseToken(null).Should().BeNull();
            h.Unsub.BuildUnsubscribeUrl("a@b.c").Should().StartWith("https://axis.test/api/marketing/unsubscribe?token=");

            (await h.Unsub.UnsubscribeByTokenAsync("garbage")).Should().BeFalse();
            (await h.Unsub.UnsubscribeByTokenAsync(token)).Should().BeTrue();
            (await h.Unsub.UnsubscribeByTokenAsync(token)).Should().BeTrue("idempotent");
            (await h.Unsub.ListAsync()).Should().HaveCount(1);
            (await h.Unsub.IsUnsubscribedAsync("CLIENT@axislb.com")).Should().BeTrue();
        }
    }
}
