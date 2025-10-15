using Domain.Entities;
using Hangfire;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Application.Services.SignalR
{
    public class SessionJobRehydrator : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SessionJobRehydrator(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            // Create a scope so we can resolve scoped services safely
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IBaseRepository<TransactionRecord>>();

            var now = DateTime.UtcNow;

            var ongoing = await repo.Query() // no AsNoTracking() required here
                .Where(t => t.StatusId == 1 && t.ExpectedEndOn != null)
                .Select(t => new { t.Id, t.ExpectedEndOn, t.HangfireJobId })
                .ToListAsync(ct);

            foreach (var t in ongoing)
            {
                if (t.ExpectedEndOn! <= now)
                {
                    BackgroundJob.Enqueue<SessionEndMonitor>(
                        x => x.EndIfOngoingAsync(t.Id, 3, CancellationToken.None));
                }
                else if (string.IsNullOrWhiteSpace(t.HangfireJobId))
                {
                    var delay = t.ExpectedEndOn!.Value - now;
                    if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

                    BackgroundJob.Schedule<SessionEndMonitor>(
                        x => x.EndIfOngoingAsync(t.Id, 3, CancellationToken.None),
                        delay
                    );
                }
            }
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
