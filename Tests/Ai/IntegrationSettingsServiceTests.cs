using System.Linq.Expressions;
using Application.DTOs;
using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.IRepositories;
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
        // Tiny in-memory repo backed by a List so we can exercise the
        // service without spinning up a real EF context.
        private class FakeRepo : IBaseRepository<IntegrationSetting>
        {
            public readonly List<IntegrationSetting> Rows = new();
            public int NextId = 1;

            public IQueryable<IntegrationSetting> Query(bool asNoTracking = true) => Rows.AsQueryable();
            public IQueryable<IntegrationSetting> QueryableAsync(Expression<Func<IntegrationSetting, bool>>? p = null, bool asNoTracking = true)
                => p == null ? Query() : Query().Where(p);

            public Task AddAsync(IntegrationSetting e, CancellationToken ct = default)
            {
                e.Id = NextId++; Rows.Add(e); return Task.CompletedTask;
            }
            public Task AddRangeAsync(IEnumerable<IntegrationSetting> es, CancellationToken ct = default)
            {
                foreach (var e in es) AddAsync(e, ct).GetAwaiter().GetResult();
                return Task.CompletedTask;
            }
            public void Update(IntegrationSetting e) { /* tracked in-place */ }
            public void UpdateRange(IEnumerable<IntegrationSetting> es) { }
            public void Remove(IntegrationSetting e) { Rows.Remove(e); }
            public void RemoveRange(IEnumerable<IntegrationSetting> es) { foreach (var e in es) Rows.Remove(e); }
            public void Attach(IntegrationSetting e) { }
            public Task<IntegrationSetting?> GetByIdAsync(int id, bool asNoTracking = true, CancellationToken ct = default)
                => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
            public Task<List<IntegrationSetting>> ListAsync(Expression<Func<IntegrationSetting, bool>>? p = null, bool asNoTracking = true, CancellationToken ct = default)
                => Task.FromResult((p == null ? Rows : Rows.Where(p.Compile())).ToList());
            public Task<int> CountAsync(Expression<Func<IntegrationSetting, bool>>? p = null, CancellationToken ct = default)
                => Task.FromResult(p == null ? Rows.Count : Rows.Count(p.Compile()));
        }

        private static IntegrationSettingsService NewService(FakeRepo repo, Mock<IUnitOfWork> uow, Mock<IHttpClientFactory> http)
            => new(repo, uow.Object, http.Object);

        [Fact]
        public async Task ListAsync_masks_secret_values()
        {
            var repo = new FakeRepo();
            repo.Rows.Add(new IntegrationSetting { Id = 1, Key = "Anthropic.ApiKey", Value = "sk-ant-abcd1234efgh", IsSecret = true });
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
            repo.Rows.Add(new IntegrationSetting { Id = 1, Key = "Anthropic.Model", Value = "claude-sonnet-4-6", IsSecret = false });
            var svc = NewService(repo, new Mock<IUnitOfWork>(), new Mock<IHttpClientFactory>());

            var list = await svc.ListAsync();
            list.Single().Value.Should().Be("claude-sonnet-4-6");
            list.Single().IsSet.Should().BeTrue();
        }

        [Fact]
        public async Task ListAsync_marks_empty_as_not_set()
        {
            var repo = new FakeRepo();
            repo.Rows.Add(new IntegrationSetting { Id = 1, Key = "WhatsApp.AccessToken", Value = null, IsSecret = true });
            var svc = NewService(repo, new Mock<IUnitOfWork>(), new Mock<IHttpClientFactory>());

            var list = await svc.ListAsync();
            list.Single().IsSet.Should().BeFalse();
            list.Single().Value.Should().BeNull();
        }

        [Fact]
        public async Task GetRawAsync_returns_full_value_for_internal_use()
        {
            var repo = new FakeRepo();
            repo.Rows.Add(new IntegrationSetting { Id = 1, Key = "Anthropic.ApiKey", Value = "sk-ant-fullvalue", IsSecret = true });
            var svc = NewService(repo, new Mock<IUnitOfWork>(), new Mock<IHttpClientFactory>());

            var raw = await svc.GetRawAsync("Anthropic.ApiKey");
            raw.Should().Be("sk-ant-fullvalue");
        }

        [Fact]
        public async Task GetRawAsync_returns_null_when_value_is_empty()
        {
            var repo = new FakeRepo();
            repo.Rows.Add(new IntegrationSetting { Id = 1, Key = "WhatsApp.AccessToken", Value = "", IsSecret = true });
            var svc = NewService(repo, new Mock<IUnitOfWork>(), new Mock<IHttpClientFactory>());

            (await svc.GetRawAsync("WhatsApp.AccessToken")).Should().BeNull();
        }
    }
}
