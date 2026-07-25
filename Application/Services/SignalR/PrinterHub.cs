using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Application.Services.SignalR
{
    /// <summary>
    /// Hub the on-site print agent(s) connect to. Agents join the "Printers"
    /// group; the API pushes "PrintJob" messages to that group when a food/drink
    /// order is created (see PrintDispatchService).
    /// </summary>
    public class PrinterHub : Hub
    {
        public const string PrintersGroup = "Printers";

        private readonly ILogger<PrinterHub> _logger;

        public PrinterHub(ILogger<PrinterHub> logger)
        {
            _logger = logger;
        }

        /// <summary>Called by a print agent after connecting so it starts receiving jobs.</summary>
        public async Task JoinPrinters()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, PrintersGroup);
            _logger.LogInformation("Print agent {ConnectionId} joined the Printers group", Context.ConnectionId);
            await Clients.Caller.SendAsync("JoinedPrinters");
        }

        public async Task LeavePrinters()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, PrintersGroup);
            _logger.LogInformation("Print agent {ConnectionId} left the Printers group", Context.ConnectionId);
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Print agent connected: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Print agent disconnected: {ConnectionId}", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
