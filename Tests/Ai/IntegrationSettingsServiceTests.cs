using Application.DTOs;
using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.IRepositories;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
using Moq;
using Xunit;

namespace Tests.Ai
{
    /// <summary>
    /// Verifies the secrets-masking behaviour. Secrets must never come
    /// back in plaintext through ListAsync — the FE only ever shows the
    /// last 4 chars. GetRawAsync stays the internal escape hatch for
    /// other services that actually need the value.
    /// </summary>
    public class IntegrationSettingsServiceTests
    {
        // Real repository over EF InMemory: the service uses EF async
        // operators (ToListAsync / FirstOrDefaultAsync), which a List-backed
        // IQueryable cannot serve.
        private class FakeRepo : BaseRepository<IntegrationSetting>
        {
            private readonly ApplicationDbContext _db;
            private FakeRepo(ApplicationDbContext db) : base(db) => _db = db;
            public FakeRepo() : this(new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)) { }

            public void Seed(IntegrationSetting row) { _db.Add(row); _db.SaveChanges(); _db.ChangeTracker.Clear(); }
        }

        private static IntegrationSettingsService NewService(FakeRepo repo, Mock<IUnitOfWork> uow, Mock<IHttpClientFactory> http)
            => new(repo, uow.Object, http.Object);

        [Fact]
        public async Task ListAsync_masks_secret_values()
        {
            var repo = new FakeRepo();
            repo.Seed(new IntegrationSetting { Id = 1, Key = "Anthropic.ApiKey", Value = "sk-ant-abcd1234efgh", IsSecret = true });
            var svc = NewService(repo, new Mock<IUnitOfWork>(), new Mock<IHttpClientFactory>());

            var list = await svc.ListAsync();

            var row = list.Single();
            row.IsSet.Should().BeTrue();
            row.Value.Should().NotBeNull();
            row.Value.Should().NotContain("abcd");        // middle is hidden
            row.Value!.Should().EndWith("efgh");          // last 4 chars visible
            row.Value.Should().StartWith("•");            // mask prefix
        }

        [Fact]
        public async Task ListAsync_shows_non_secret_values_plain()
        {
            var repo = new FakeRepo();
            repo.Seed(new IntegrationSetting { Id = 1, Key = "Anthropic.Model", Value = "claude-sonnet-4-6", IsSecret = false });
            var svc = NewService(repo, new Mock<IUnitOfWork>(), new Mock<IHttpClientFactory>());

            var list = await svc.ListAsync();
            list.Single().Value.Should().Be("claude-sonnet-4-6");
            list.Single().IsSet.Should().BeTrue();
        }

        [Fact]
        public async Task ListAsync_marks_empty_as_not_set()
        {
            var repo = new FakeRepo();
            repo.Seed(new IntegrationSetting { Id = 1, Key = "WhatsApp.AccessToken", Value = null, IsSecret = true });
            var svc = NewService(repo, new Mock<IUnitOfWork>(), new Mock<IHttpClientFactory>());

            var list = await svc.ListAsync();
            list.Single().IsSet.Should().BeFalse();
            list.Single().Value.Should().BeNull();
        }

        [Fact]
        public async Task GetRawAsync_returns_full_value_for_internal_use()
        {
            var repo = new FakeRepo();
            repo.Seed(new IntegrationSetting { Id = 1, Key = "Anthropic.ApiKey", Value = "sk-ant-fullvalue", IsSecret = true });
            var svc = NewService(repo, new Mock<IUnitOfWork>(), new Mock<IHttpClientFactory>());

            var raw = await svc.GetRawAsync("Anthropic.ApiKey");
            raw.Should().Be("sk-ant-fullvalue");
        }

        [Fact]
        public async Task GetRawAsync_returns_null_when_value_is_empty()
        {
            var repo = new FakeRepo();
            repo.Seed(new IntegrationSetting { Id = 1, Key = "WhatsApp.AccessToken", Value = "", IsSecret = true });
            var svc = NewService(repo, new Mock<IUnitOfWork>(), new Mock<IHttpClientFactory>());

            (await svc.GetRawAsync("WhatsApp.AccessToken")).Should().BeNull();
        }
    }
}
