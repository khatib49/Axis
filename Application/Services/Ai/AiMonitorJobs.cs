using Application.DTOs;
using Application.IServices;

namespace Application.Services.Ai
{
    /// <summary>
    /// Background jobs that ask Claude to look at live state and propose
    /// actions on its own. They only WRITE PendingAiActions — never execute.
    /// Toggled via IntegrationSettings keys so admin can pause them.
    ///
    /// Wired in Program.cs:
    ///   RecurringJob.AddOrUpdate&lt;AiMonitorJobs&gt;(
    ///       "ai-occupancy-monitor",
    ///       j => j.RunOccupancyMonitorAsync(CancellationToken.None),
    ///       "*/30 * * * *"); // every 30 minutes
    ///
    ///   RecurringJob.AddOrUpdate&lt;AiMonitorJobs&gt;(
    ///       "ai-pattern-monitor",
    ///       j => j.RunPatternMonitorAsync(CancellationToken.None),
    ///       "0 10 * * *"); // daily 10am UTC
    /// </summary>
    public class AiMonitorJobs
    {
        private readonly IIntegrationSettingsService _settings;
        private readonly IAiChatService _chat;

        public AiMonitorJobs(IIntegrationSettingsService settings, IAiChatService chat)
        {
            _settings = settings;
            _chat = chat;
        }

        public async Task RunOccupancyMonitorAsync(CancellationToken ct = default)
        {
            var enabled = await _settings.GetRawAsync("AiMonitor.OccupancyEnabled", ct);
            if (!IsTrue(enabled)) return;

            var thresholdRaw = await _settings.GetRawAsync("AiMonitor.OccupancyThresholdPct", ct);
            var threshold = int.TryParse(thresholdRaw, out var t) ? t : 40;

            var prompt = $@"
The occupancy monitor just woke you up. Do the following:
1. Call get_occupancy_now to see current occupancy.
2. If occupancy is at or above {threshold}%, do NOTHING — just reply 'occupancy OK, no action'.
3. If occupancy is BELOW {threshold}%:
   a. Call get_recent_players for the top game in the last 14d (within_days=14, limit=20)
   b. Build a Flash Tournament proposal: start time 2 hours from now, entry fee $5, prize pool $30, and a personalised hype message.
   c. Call propose_flash_tournament with the recipient phones and names.
   d. Reply with a one-sentence summary of what you proposed.
Do this autonomously without asking me anything.
";

            await _chat.SendAsync(
                new AiSendMessageRequest(null, prompt),
                actor: "ai-monitor:occupancy",
                ct: ct);
        }

        public async Task RunPatternMonitorAsync(CancellationToken ct = default)
        {
            var enabled = await _settings.GetRawAsync("AiMonitor.PatternsEnabled", ct);
            if (!IsTrue(enabled)) return;

            var prompt = @"
The customer-pattern monitor just woke you up. Find regulars who are 'due' for a visit and propose personalised 'we saved your spot' pings.

1. Call get_due_regulars (within_hours=48, limit=10)
2. If the list is empty, reply 'no due regulars today, skipping'.
3. Otherwise, for each customer write a short personalised WhatsApp message referencing their name and a friendly slot (e.g. 'Tonight 8pm, your usual room is open — want it?').
4. Call propose_customer_ping with the full list as a single proposal.
5. Reply with a one-sentence summary.
Do this autonomously without asking me anything.
";

            await _chat.SendAsync(
                new AiSendMessageRequest(null, prompt),
                actor: "ai-monitor:patterns",
                ct: ct);
        }

        private static bool IsTrue(string? s)
            => !string.IsNullOrWhiteSpace(s) &&
               (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");
    }
}
