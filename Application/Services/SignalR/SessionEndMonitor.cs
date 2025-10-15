using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.AspNetCore.SignalR;

namespace Application.Services.SignalR
{
    public class SessionEndMonitor : ISessionEndMonitor
    {
        private readonly IBaseRepository<TransactionRecord> _trxRepo;
        private readonly IUnitOfWork _uow;
        private readonly IHubContext<ReceptionHub> _hub;

        public SessionEndMonitor(
            IBaseRepository<TransactionRecord> trxRepo,
            IUnitOfWork uow,
            IHubContext<ReceptionHub> hub)
        {
            _trxRepo = trxRepo;
            _uow = uow;
            _hub = hub;
        }

        // This is what Hangfire calls
        public async Task EndIfOngoingAsync(int transactionId, int endedStatusId, CancellationToken ct = default)
        {
            var trx = await _trxRepo.GetByIdAsync(transactionId, asNoTracking: false, ct);
            if (trx is null) return;

            // already ended? no-op
            if (trx.StatusId == endedStatusId) return;

            // mark ended
            trx.StatusId = endedStatusId;
            trx.ModifiedOn = DateTime.UtcNow;
            trx.HangfireJobId = null;
            await _uow.SaveChangesAsync(ct);

            // notify reception (broadcast)
            await _hub.Clients.All.SendAsync("sessionEnded", new
            {
                transactionId = trx.Id,
                roomId = trx.RoomId,
                setId = trx.SetId,
                endedAtUtc = DateTime.UtcNow
            }, ct);
        }
    }
}
