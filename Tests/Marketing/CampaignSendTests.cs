using Application.DTOs;
using Application.Services.Marketing;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.Marketing
{
    public class CampaignSendTests
    {
        [Fact]
        public async Task SendNow_counts_sent_failed_and_skipped_and_records_each_recipient()
        {
            using var h = new MarketingHarness();
            var ok = h.AddClient("ok@x.com", "+1", "Ola");
            var bad = h.AddClient("bad@x.com", "+2", "Bob");
            var unsub = h.AddClient("gone@x.com", "+3", "Gus");
            var noMail = h.AddClient(null, "+4", "Nia");
            foreach (var u in new[] { ok, bad, unsub, noMail }) h.AddSale(u, 2, 10m);
            await h.Unsub.AddAsync("GONE@x.com", "Manual");
            h.Email.ShouldFail = a => a == "bad@x.com";
            var seg = h.AddSegment("{\"lastVisitWithinDays\":30}");
            var c = h.AddCampaign(seg.Id, body: "Hello {{firstName}}, code {{code}}", code: "SAVE5");

            var r = await h.Campaigns.SendNowAsync(c.Id);
            r.Success.Should().BeTrue();
            r.Data!.AudienceCount.Should().Be(4);
            r.Data.Sent.Should().Be(1); r.Data.Failed.Should().Be(1); r.Data.Skipped.Should().Be(2);

            var saved = h.Db.Set<MktCampaign>().Single(x => x.Id == c.Id);
            saved.Status.Should().Be("Sent"); saved.SentOn.Should().NotBeNull();
            saved.AudienceCount.Should().Be(4); saved.SentCount.Should().Be(1); saved.FailedCount.Should().Be(1);

            var rows = h.Recipients(c.Id);
            rows.Should().HaveCount(4);
            rows.Single(x => x.UserId == ok).Status.Should().Be("Sent");
            rows.Single(x => x.UserId == bad).Should().Match<MktCampaignRecipient>(x => x.Status == "Failed" && x.Error == "mailbox unavailable");
            rows.Single(x => x.UserId == unsub).Should().Match<MktCampaignRecipient>(x => x.Status == "Skipped" && x.Error == "Unsubscribed");
            rows.Single(x => x.UserId == noMail).Should().Match<MktCampaignRecipient>(x => x.Status == "Skipped" && x.Error == "No address");

            h.Email.Sent.Should().HaveCount(1);
            h.Email.Sent[0].Body.Should().StartWith("Hello Ola, code SAVE5").And.Contain("Unsubscribe: https://axis.test/api/marketing/unsubscribe?token=");

            var stats = await h.Campaigns.StatsAsync(c.Id);
            stats.Data!.Sent.Should().Be(1); stats.Data.Failed.Should().Be(1); stats.Data.Skipped.Should().Be(2); stats.Data.Pending.Should().Be(0);
            var failedOnly = await h.Campaigns.RecipientsAsync(c.Id, "Failed", null, 1, 50);
            failedOnly.TotalCount.Should().Be(1); failedOnly.Data[0].Address.Should().Be("bad@x.com");
        }

        [Fact]
        public async Task SendNow_never_throws_when_a_sender_throws_or_is_not_configured()
        {
            using var h = new MarketingHarness();
            var u = h.AddClient("a@x.com", "+961", "Amy"); h.AddSale(u, 1, 5m);
            var seg = h.AddSegment();

            h.Email.Throw = true;
            var email = h.AddCampaign(seg.Id);
            var r1 = await h.Campaigns.SendNowAsync(email.Id);
            r1.Success.Should().BeTrue(); r1.Data!.Failed.Should().Be(1); r1.Data.Status.Should().Be("Sent");
            h.Recipients(email.Id).Single().Error.Should().Be("boom");

            h.Sms.Configured = false;
            var sms = h.AddCampaign(seg.Id, channel: "Sms", body: "Hi {{firstName}}", subject: null);
            var r2 = await h.Campaigns.SendNowAsync(sms.Id);
            r2.Data!.Failed.Should().Be(1);
            h.Recipients(sms.Id).Single().Error.Should().Be("SMS not configured");

            h.WhatsApp.Throw = true;
            var wa = h.AddCampaign(seg.Id, channel: "WhatsApp", body: "", subject: null, template: "promo_v1");
            var r3 = await h.Campaigns.SendNowAsync(wa.Id);
            r3.Data!.Failed.Should().Be(1);
            h.Recipients(wa.Id).Single().Error.Should().Contain("WhatsApp.PhoneNumberId");

            (await h.Campaigns.SendNowAsync(email.Id)).Success.Should().BeFalse("already sent");
            (await h.Campaigns.SendNowAsync(9999)).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Sms_and_WhatsApp_use_phone_and_WhatsApp_passes_firstName_param()
        {
            using var h = new MarketingHarness();
            var u = h.AddClient("a@x.com", "+96170", "Amy"); h.AddSale(u, 1, 5m);
            var seg = h.AddSegment();
            var sms = h.AddCampaign(seg.Id, channel: "Sms", body: "Hi {{firstName}}", subject: null);
            await h.Campaigns.SendNowAsync(sms.Id);
            h.Sms.Sent.Should().ContainSingle(x => x.To == "+96170" && x.Text == "Hi Amy");

            var wa = h.AddCampaign(seg.Id, channel: "WhatsApp", body: "", subject: null, template: "promo_v1");
            var r = await h.Campaigns.SendNowAsync(wa.Id);
            r.Data!.Sent.Should().Be(1);
            h.WhatsApp.Sent.Should().ContainSingle(x => x.Phone == "+96170" && x.Template == "promo_v1" && x.Params.SequenceEqual(new[] { "Amy" }));
        }

        [Fact]
        public async Task TestSend_records_an_IsTest_row_and_does_not_touch_campaign_status_or_counts()
        {
            using var h = new MarketingHarness();
            var seg = h.AddSegment();
            var c = h.AddCampaign(seg.Id);
            h.Email.Configured = false;

            var r = await h.Campaigns.TestSendAsync(c.Id, "client@axislb.com");
            r.Success.Should().BeTrue();
            r.Data!.Status.Should().Be("Failed"); r.Data.Error.Should().Be("SMTP not configured");
            var saved = h.Db.Set<MktCampaign>().Single(x => x.Id == c.Id);
            saved.Status.Should().Be("Draft"); saved.SentCount.Should().Be(0); saved.FailedCount.Should().Be(0);
            var row = h.Recipients(c.Id).Single();
            row.IsTest.Should().BeTrue(); row.UserId.Should().BeNull(); row.Address.Should().Be("client@axislb.com");
            (await h.Campaigns.StatsAsync(c.Id)).Data!.Failed.Should().Be(0, "test rows are excluded from stats");

            h.Email.Configured = true;
            (await h.Campaigns.TestSendAsync(c.Id, "client@axislb.com")).Data!.Status.Should().Be("Sent");
            h.Email.Sent.Single().Body.Should().StartWith("Hi Test");
            (await h.Campaigns.TestSendAsync(c.Id, "not-an-email")).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Create_validates_channel_subject_template_and_segment()
        {
            using var h = new MarketingHarness();
            var seg = h.AddSegment();
            (await h.Campaigns.CreateAsync(new CampaignCreateDto("x", "Fax", seg.Id, "b"))).Success.Should().BeFalse();
            (await h.Campaigns.CreateAsync(new CampaignCreateDto("x", "Email", seg.Id, "b"))).Error.Should().Contain("subject");
            (await h.Campaigns.CreateAsync(new CampaignCreateDto("x", "WhatsApp", seg.Id, ""))).Error.Should().Contain("WhatsAppTemplate");
            (await h.Campaigns.CreateAsync(new CampaignCreateDto("x", "Email", 999, "b", "s"))).Error.Should().Contain("Segment");
            var ok = await h.Campaigns.CreateAsync(new CampaignCreateDto("x", "email", seg.Id, "b", "s", ScheduledFor: DateTime.UtcNow.AddHours(1)));
            ok.Success.Should().BeTrue(); ok.Data!.Channel.Should().Be("Email"); ok.Data.Status.Should().Be("Scheduled"); ok.Data.SegmentName.Should().Be("All clients");
            (await h.Campaigns.CancelAsync(ok.Data.Id)).Data!.Status.Should().Be("Cancelled");
        }
    }

    public class DueCampaignTests
    {
        [Fact]
        public void IsDue_requires_Scheduled_status_and_a_past_ScheduledFor()
        {
            var now = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
            MarketingCampaignService.IsDue(new MktCampaign { Status = "Scheduled", ScheduledFor = now.AddMinutes(-1) }, now).Should().BeTrue();
            MarketingCampaignService.IsDue(new MktCampaign { Status = "Scheduled", ScheduledFor = now }, now).Should().BeTrue();
            MarketingCampaignService.IsDue(new MktCampaign { Status = "Scheduled", ScheduledFor = now.AddMinutes(1) }, now).Should().BeFalse();
            MarketingCampaignService.IsDue(new MktCampaign { Status = "Scheduled", ScheduledFor = null }, now).Should().BeFalse();
            MarketingCampaignService.IsDue(new MktCampaign { Status = "Draft", ScheduledFor = now.AddMinutes(-1) }, now).Should().BeFalse();
            MarketingCampaignService.IsDue(new MktCampaign { Status = "Sent", ScheduledFor = now.AddMinutes(-1) }, now).Should().BeFalse();
        }

        [Fact]
        public async Task SendDue_sends_only_due_scheduled_campaigns()
        {
            using var h = new MarketingHarness();
            var u = h.AddClient("a@x.com", "+1"); h.AddSale(u, 1, 5m);
            var seg = h.AddSegment();
            var now = DateTime.UtcNow;
            var due = h.AddCampaign(seg.Id, status: "Scheduled", scheduledFor: now.AddMinutes(-5));
            var future = h.AddCampaign(seg.Id, status: "Scheduled", scheduledFor: now.AddHours(1));
            var draft = h.AddCampaign(seg.Id, status: "Draft", scheduledFor: now.AddMinutes(-5));

            (await h.Campaigns.SendDueAsync(now)).Should().Be(1);
            h.Db.Set<MktCampaign>().Single(c => c.Id == due.Id).Status.Should().Be("Sent");
            h.Db.Set<MktCampaign>().Single(c => c.Id == future.Id).Status.Should().Be("Scheduled");
            h.Db.Set<MktCampaign>().Single(c => c.Id == draft.Id).Status.Should().Be("Draft");
            (await h.Campaigns.SendDueAsync(now)).Should().Be(0, "already sent");
        }
    }
}
