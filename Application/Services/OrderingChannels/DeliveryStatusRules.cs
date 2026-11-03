using Domain.Entities;

namespace Application.Services.OrderingChannels
{
    /// <summary>
    /// Allowed status transitions for delivery / takeaway orders.
    ///   Delivery: Pending → Preparing → Ready → OutForDelivery → Delivered
    ///   Takeaway: Pending → Preparing → Ready → Delivered (handed over at the counter)
    /// Pending may skip straight to Ready (no kitchen step). Cancelled is reachable
    /// from any non-terminal status through Cancel, never through Advance.
    /// </summary>
    public static class DeliveryStatusRules
    {
        public static IReadOnlyList<string> AllowedNext(string type, string from)
        {
            var takeaway = string.Equals(type, DeliveryOrderTypes.Takeaway, StringComparison.OrdinalIgnoreCase);
            return from switch
            {
                DeliveryStatuses.Pending => new[] { DeliveryStatuses.Preparing, DeliveryStatuses.Ready },
                DeliveryStatuses.Preparing => new[] { DeliveryStatuses.Ready },
                DeliveryStatuses.Ready => takeaway
                    ? new[] { DeliveryStatuses.Delivered }
                    : new[] { DeliveryStatuses.OutForDelivery },
                DeliveryStatuses.OutForDelivery => takeaway
                    ? Array.Empty<string>()
                    : new[] { DeliveryStatuses.Delivered },
                _ => Array.Empty<string>(),
            };
        }

        public static bool CanAdvance(string type, string from, string to)
            => AllowedNext(type, from).Contains(to, StringComparer.Ordinal);

        /// <summary>The "happy path" next status, or null when the order is finished.</summary>
        public static string? DefaultNext(string type, string from)
        {
            var next = AllowedNext(type, from);
            return next.Count == 0 ? null : next[0];
        }

        public static bool CanCancel(string from) => !DeliveryStatuses.IsTerminal(from);

        public static bool IsKnown(string status) => DeliveryStatuses.All.Contains(status, StringComparer.Ordinal);
    }
}
