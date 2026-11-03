using Application.Common;
using Application.IServices;
using Application.Services.Marketing;
using Domain.Entities;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Marketing
{
    internal sealed class FakeUser : ICurrentUserAccessor
    {
        public int? UserId { get; set; } = 1;
        public string UserName { get; set; } = "admin@axislb.com";
        public IReadOnlyList<string> Roles { get; set; } = new List<string> { "admin" };
        public int? BranchId { get; set; }
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class FakeEmail : IEmailSender
    {
        public bool Configured = true;
        public Func<string, bool> ShouldFail = _ => false;
        public bool Throw;
        public readonly List<(string To, string Subject, string Body, bool IsHtml)> Sent = new();
        public Task<EmailSendResult> SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml, EmailAttachment? attachment = null, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("boom");
            var addr = to.First();
            if (!Configured) return Task.FromResult(new EmailSendResult(false, false, "SMTP not configured"));
            if (ShouldFail(addr)) return Task.FromResult(new EmailSendResult(false, true, "mailbox unavailable"));
            Sent.Add((addr, subject, body, isHtml));
            return Task.FromResult(new EmailSendResult(true, true, null));
        }
    }

    internal sealed class FakeSms : ISmsSender
    {
        public bool Configured = true;
        public readonly List<(string To, string Text)> Sent = new();
        public Task<SmsSendResult> SendAsync(string to, string text, CancellationToken ct = default)
        {
            if (!Configured) return Task.FromResult(new SmsSendResult(false, false, "SMS not configured"));
            Sent.Add((to, text));
            return Task.FromResult(new SmsSendResult(true, true, null));
        }
    }

    internal sealed class FakeWhatsApp : IWhatsAppService
    {
        public bool Throw;
        public readonly List<(string Phone, string Template, List<string> Params)> Sent = new();
        public Task<WhatsAppBlastResult> SendBlastAsync(int? pendingActionId, IEnumerable<WhatsAppRecipient> recipients, string templateName, IEnumerable<string> templateParams, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("WhatsApp.PhoneNumberId not set");
            var list = recipients.ToList();
            foreach (var r in list) Sent.Add((r.Phone, templateName, (r.PerRecipientParams ?? templateParams).ToList()));
            return Task.FromResult(new WhatsAppBlastResult(list.Count, 0, Array.Empty<string>()));
        }
    }

    /// <summary>In-memory context + full service wiring with fake senders.</summary>
    internal sealed class MarketingHarness : IDisposable
    {
        public readonly ApplicationDbContext Db;
        public readonly FakeUser User = new();
        public readonly FakeEmail Email = new();
        public readonly FakeSms Sms = new();
        public readonly FakeWhatsApp WhatsApp = new();
        public readonly MarketingUnsubscribeService Unsub;
        public readonly MarketingSegmentService Segments;
        public readonly MarketingCampaignService Campaigns;
        public static readonly DateTime Now = DateTime.UtcNow;

        public MarketingHarness()
        {
            MarketingCampaignService.BatchDelay = TimeSpan.Zero;
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "unit-test-key-0123456789", ["Marketing:PublicBaseUrl"] = "https://axis.test",
            }).Build();
            Unsub = new MarketingUnsubscribeService(Db, cfg);
            Segments = new MarketingSegmentService(Db, User);
            Campaigns = new MarketingCampaignService(Db, Segments, Unsub, Email, Sms, WhatsApp, User, NullLogger<MarketingCampaignService>.Instance);
            Db.Roles.Add(new AppRole { Id = 1, Name = "client", NormalizedName = "CLIENT" });
            Db.Roles.Add(new AppRole { Id = 2, Name = "admin", NormalizedName = "ADMIN" });
            Db.SaveChanges();
        }

        private int _nextUser = 100;
        public int AddClient(string? email, string? phone, string first = "Jane", string last = "Doe", bool client = true)
        {
            var id = _nextUser++;
            Db.Users.Add(new AppUser { Id = id, UserName = email ?? $"u{id}", Email = email, PhoneNumber = phone, FirstName = first, LastName = last, DisplayName = $"{first} {last}", StatusId = 1 });
            Db.UserRoles.Add(new IdentityUserRole<int> { UserId = id, RoleId = client ? 1 : 2 });
            Db.SaveChanges();
            return id;
        }

        public void AddSale(int userId, int daysAgo, decimal total, int status = 6, bool viaPaidOn = true, int? branchId = null)
        {
            var when = Now.AddDays(-daysAgo);
            Db.Set<TransactionRecord>().Add(new TransactionRecord
            {
                UserId = userId, StatusId = status, TotalPrice = total, CreatedBy = "test", BranchId = branchId,
                CreatedOn = viaPaidOn ? when.AddDays(-30) : when, PaidOn = viaPaidOn ? when : null,
            });
            Db.SaveChanges();
        }

        public MktSegment AddSegment(string rulesJson = "{}", string name = "All clients")
        {
            var s = new MktSegment { Name = name, RulesJson = rulesJson, CreatedBy = "test" };
            Db.Add(s); Db.SaveChanges(); return s;
        }

        public MktCampaign AddCampaign(int segmentId, string channel = "Email", string body = "Hi {{firstName}}", string? subject = "Hello", string? template = null, string status = "Draft", DateTime? scheduledFor = null, string? code = null)
        {
            var c = new MktCampaign { Name = "C", Channel = channel, SegmentId = segmentId, Body = body, Subject = subject, WhatsAppTemplate = template, Status = status, ScheduledFor = scheduledFor, PromoCode = code, CreatedBy = "test" };
            Db.Add(c); Db.SaveChanges(); return c;
        }

        public List<MktCampaignRecipient> Recipients(int campaignId) => Db.Set<MktCampaignRecipient>().Where(r => r.CampaignId == campaignId).OrderBy(r => r.Id).ToList();

        public void Dispose() => Db.Dispose();
    }
}
