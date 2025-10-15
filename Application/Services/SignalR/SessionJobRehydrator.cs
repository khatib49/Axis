using Domain.Entities;
using Hangfire;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Application.Services.SignalR
{
    public class SessionJobRehydrator : IHostedService
    {
        private readonly IBaseRepository<TransactionRecord> _repo;
        private readonly IUnitOfWork _uow;
        private readonly IServiceProvider _sp;

        public SessionJobRehydrator(IBaseRepository<TransactionRecord> repo, IUnitOfWork uow, IServiceProvider sp)
        {
            _repo = repo; _uow = uow; _sp = sp;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            // find ongoing with an end time
            var ongoing = await _repo.Query().AsNoTracking()
                .Where(t => t.StatusId == 1 /* ongoing */ && t.ExpectedEndOn != null)
                .Select(t => new { t.Id, t.ExpectedEndOn, t.HangfireJobId })
                .ToListAsync(ct);

            foreach (var t in ongoing)
                {
                    if (t.ExpectedEndOn! <= now)
                    {
                    // already overdue -> end now
                    BackgroundJob.Enqueue<SessionEndMonitor>(x => x.EndIfOngoingAsync(t.Id, 3, CancellationToken.None));
                }
                else if (string.IsNullOrWhiteSpace(t.HangfireJobId))
                {
                    // no job stored -> schedule a new one
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
