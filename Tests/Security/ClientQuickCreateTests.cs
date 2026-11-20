using Application.DTOs.RequestDto;
using Application.Services;
using Domain.Identity;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tests.Security
{
    /// <summary>
    /// The cashier's "create client" (invoice and game-session screens) sends a
    /// phone and a name but no email. Identity is configured the same way as
    /// Program.cs, so these tests catch the validator rejecting blank emails.
    /// </summary>
    public class ClientQuickCreateTests
    {
        private static (UsersService Svc, UserManager<AppUser> Users) Build()
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

            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.Roles.Add(new AppRole { Id = 1, Name = "client", NormalizedName = "CLIENT" });
            db.SaveChanges();

            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var roles = sp.GetRequiredService<RoleManager<AppRole>>();
            return (new UsersService(null!, null!, null!, users, roles), users);
        }

        [Fact]
        public async Task Creates_client_with_phone_and_name_only()
        {
            var (svc, users) = Build();

            var res = await svc.CreateClient(new ClientUserCreateRequest
            {
                PhoneNumber = "03123456", FirstName = "Ali", LastName = "Haddad", Email = "",
            });

            res.IsNewlyCreated.Should().BeTrue();
            var saved = await users.FindByIdAsync(res.Id.ToString());
            saved!.Email.Should().BeNull();
            (await users.IsInRoleAsync(saved, "client")).Should().BeTrue();
        }

        [Fact]
        public async Task Several_clients_without_email_can_coexist()
        {
            var (svc, _) = Build();

            var a = await svc.CreateClient(new ClientUserCreateRequest { PhoneNumber = "70111111", FirstName = "A", Email = "" });
            var b = await svc.CreateClient(new ClientUserCreateRequest { PhoneNumber = "70222222", FirstName = "B", Email = null });

            a.IsNewlyCreated.Should().BeTrue();
            b.IsNewlyCreated.Should().BeTrue();
            a.Id.Should().NotBe(b.Id);
        }

        [Fact]
        public async Task Duplicate_email_is_still_rejected()
        {
            var (svc, _) = Build();

            await svc.CreateClient(new ClientUserCreateRequest { PhoneNumber = "70111111", FirstName = "A", Email = "same@x.com" });
            var act = () => svc.CreateClient(new ClientUserCreateRequest { PhoneNumber = "70222222", FirstName = "B", Email = "SAME@x.com" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Existing_phone_returns_existing_client()
        {
            var (svc, _) = Build();

            var a = await svc.CreateClient(new ClientUserCreateRequest { PhoneNumber = "70111111", FirstName = "A", Email = "" });
            var again = await svc.CreateClient(new ClientUserCreateRequest { PhoneNumber = " 70111111 ", FirstName = "A", Email = "" });

            again.IsNewlyCreated.Should().BeFalse();
            again.Id.Should().Be(a.Id);
        }
    }
}
