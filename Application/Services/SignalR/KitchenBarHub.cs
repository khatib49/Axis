using Application.DTOs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Application.Services.SignalR
{
    public class KitchenBarHub : Hub
    {
        private readonly ILogger<KitchenBarHub> _logger;

        public KitchenBarHub(ILogger<KitchenBarHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Join a station group (Kitchen or Bar)
        /// </summary>
        public async Task JoinStation(string station)
        {
            if (station != "Kitchen" && station != "Bar")
            {
                _logger.LogWarning("Invalid station join attempt: {Station}", station);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, station);
            _logger.LogInformation(
                "User {Username} joined {Station} group",
                Context.User?.Identity?.Name, station);

            await Clients.Caller.SendAsync("JoinedStation", station);
        }

        /// <summary>
        /// Leave a station group
        /// </summary>
        public async Task LeaveStation(string station)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, station);
            _logger.LogInformation(
                "User {Username} left {Station} group",
                Context.User?.Identity?.Name, station);
        }

        /// <summary>
        /// Broadcast new order to appropriate station
        /// </summary>
        public async Task NotifyNewOrder(string station, KitchenBarOrderDto order)
        {
            await Clients.Group(station).SendAsync("NewOrder", order);
            _logger.LogInformation(
                "New order {OrderId} notified to {Station}",
                order.Id, station);
        }

        /// <summary>
        /// Broadcast order status change
        /// </summary>
        public async Task NotifyOrderStatusChanged(string station, int orderId, string status)
        {
            await Clients.Group(station).SendAsync("OrderStatusChanged", new
            {
                OrderId = orderId,
                Status = status,
                Timestamp = DateTime.UtcNow
            });

            _logger.LogInformation(
                "Order {OrderId} status changed to {Status} in {Station}",
                orderId, status, station);
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation(
                "Client connected: {ConnectionId}, User: {Username}",
                Context.ConnectionId, Context.User?.Identity?.Name);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation(
                "Client disconnected: {ConnectionId}, User: {Username}",
                Context.ConnectionId, Context.User?.Identity?.Name);

            await base.OnDisconnectedAsync(exception);
        }
    }
}
