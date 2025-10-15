namespace Application.Services.SignalR
{
    public interface ISessionEndMonitor
    {
        Task EndIfOngoingAsync(int transactionId, int endedStatusId, CancellationToken ct = default);
    }
}
