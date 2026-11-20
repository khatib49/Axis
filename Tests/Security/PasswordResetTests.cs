using System.Text.RegularExpressions;
using Application.IServices;
using Application.Services;
using Domain.Identity;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tests.Marketing;
using Xunit;

namespace Tests.Security
{
    public class PasswordResetTests
    {
        private const string OldPass = "OldPass123";

        private sealed class Harness
        {
            public UserManager<AppUser> Users = null!;
            public FakeEmail Email = new();
            public PasswordResetService Svc = null!;
            public AppUser Staff = null!;
        }

        private static async Task<Harness> Build(int status = 1)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            var dbName = Guid.NewGuid().ToString();
            services.AddDbContext<ApplicationDbContext>(o => o
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            AxisAPI.Utils.IdentitySetup.AddAxisIdentity(services);
            var sp = services.BuildServiceProvider();

            var h = new Harness { Users = sp.GetRequiredService<UserManager<AppUser>>() };
            h.Staff = new AppUser { UserName = "cashier@axislb.com", Email = "cashier@axislb.com", DisplayName = "Cash Ier", StatusId = status };
            (await h.Users.CreateAsync(h.Staff, OldPass)).Succeeded.Should().BeTrue();

            var settings = new Mock<IIntegrationSettingsService>();
            settings.Setup(s => s.GetRawAsync("Auth.PasswordResetBaseUrl", It.IsAny<CancellationToken>())).ReturnsAsync("https://axis.test/");
            h.Svc = new PasswordResetService(h.Users, h.Email, settings.Object, NullLogger<PasswordResetService>.Instance);
            return h;
        }

        private static (string Email, string Token) LinkFrom(string body)
        {
            var m = Regex.Match(body, @"https://axis\.test/reset-password\?email=([^&""]+)&amp;token=([A-Za-z0-9_\-]+)");
            m.Success.Should().BeTrue("the email should contain the reset link");
            return (Uri.UnescapeDataString(m.Groups[1].Value), m.Groups[2].Value);
        }

        [Fact]
        public async Task Emailed_link_resets_the_password_once()
        {
            var h = await Build();

            await h.Svc.RequestAsync(" Cashier@AxisLB.com ");

            h.Email.Sent.Should().ContainSingle();
            h.Email.Sent[0].To.Should().Be("cashier@axislb.com");
            var (email, token) = LinkFrom(h.Email.Sent[0].Body);

            (await h.Svc.ResetAsync(email, token, "NewPass456")).Success.Should().BeTrue();
            var user = await h.Users.FindByEmailAsync(email);
            (await h.Users.CheckPasswordAsync(user!, "NewPass456")).Should().BeTrue();
            (await h.Users.CheckPasswordAsync(user!, OldPass)).Should().BeFalse();

            var reuse = await h.Svc.ResetAsync(email, token, "Another789");
            reuse.Success.Should().BeFalse();
            reuse.Error.Should().Contain("invalid or has expired");
        }

        [Fact]
        public async Task Wrong_or_garbled_token_is_rejected()
        {
            var h = await Build();

            (await h.Svc.ResetAsync("cashier@axislb.com", "not-a-token", "NewPass456")).Success.Should().BeFalse();
            (await h.Svc.ResetAsync("cashier@axislb.com", "%%%", "NewPass456")).Success.Should().BeFalse();
            (await h.Users.CheckPasswordAsync(h.Staff, OldPass)).Should().BeTrue();
        }

        [Fact]
        public async Task Weak_password_reports_the_rule()
        {
            var h = await Build();
            await h.Svc.RequestAsync("cashier@axislb.com");
            var (email, token) = LinkFrom(h.Email.Sent[0].Body);

            var res = await h.Svc.ResetAsync(email, token, "short");

            res.Success.Should().BeFalse();
            res.Error.Should().Contain("at least 8");
        }

        [Fact]
        public async Task Unknown_email_sends_nothing()
        {
            var h = await Build();

            await h.Svc.RequestAsync("nobody@axislb.com");
            await h.Svc.RequestAsync("");

            h.Email.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task Inactive_account_gets_no_link()
        {
            var h = await Build(status: 2);

            await h.Svc.RequestAsync("cashier@axislb.com");

            h.Email.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task Reset_clears_a_lockout()
        {
            var h = await Build();
            await h.Users.SetLockoutEnabledAsync(h.Staff, true);
            await h.Users.SetLockoutEndDateAsync(h.Staff, DateTimeOffset.UtcNow.AddHours(1));
            await h.Svc.RequestAsync("cashier@axislb.com");
            var (email, token) = LinkFrom(h.Email.Sent[0].Body);

            (await h.Svc.ResetAsync(email, token, "NewPass456")).Success.Should().BeTrue();

            (await h.Users.IsLockedOutAsync(h.Staff)).Should().BeFalse();
        }
    }
}
