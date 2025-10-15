namespace Application.Services.SignalR
{
    public interface ISessionJobRehydrator
    {
        Task StartAsync(CancellationToken ct);
        Task StopAsync(CancellationToken ct);
    }
}
