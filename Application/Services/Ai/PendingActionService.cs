using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Ai
{
    /// <summary>
    /// Admin gateway for AI-proposed actions. Approve → executes synchronously
    /// (with audit), Reject → marks rejected. Execution paths route by Type.
    /// </summary>
    public class PendingActionService : IPendingActionService
    {
        private readonly ApplicationDbContext _db;
        private readonly IWhatsAppService _wa;
        private readonly IIntegrationSettingsService _settings;

        public PendingActionService(
            ApplicationDbContext db,
            IWhatsAppService wa,
            IIntegrationSettingsService settings)
        {
            _db = db;
            _wa = wa;
            _settings = settings;
        }

        public async Task<PaginatedResponse<PendingActionDto>> ListAsync(PendingActionsFilterDto filter, CancellationToken ct = default)
        {
            var q = _db.PendingAiActions.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(filter.Status))
                q = q.Where(x => x.Status == filter.Status);

            var total = await q.CountAsync(ct);
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, 200);

            var rows = await q.OrderByDescending(x => x.ProposedOn).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new PendingActionDto(
                    x.Id, x.Type, x.Title, x.Summary, x.Payload, x.Status,
                    x.ProposedBy, x.ProposedOn, x.ConversationId,
                    x.DecidedBy, x.DecidedOn, x.ExecutionLog, x.ExecutedOn))
                .ToListAsync(ct);

            return new PaginatedResponse<PendingActionDto>(total, rows, page, pageSize);
        }

        public async Task<PendingActionDto?> GetAsync(int id, CancellationToken ct = default)
        {
            return await _db.PendingAiActions.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new PendingActionDto(
                    x.Id, x.Type, x.Title, x.Summary, x.Payload, x.Status,
                    x.ProposedBy, x.ProposedOn, x.ConversationId,
                    x.DecidedBy, x.DecidedOn, x.ExecutionLog, x.ExecutedOn))
                .FirstOrDefaultAsync(ct);
        }

        public async Task<ActionDecisionResultDto> ApproveAsync(int id, string actor, CancellationToken ct = default)
        {
            var row = await _db.PendingAiActions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null) return new ActionDecisionResultDto(false, "NotFound", "Action not found");
            if (row.Status != "Pending")
                return new ActionDecisionResultDto(false, row.Status, $"Already {row.Status}");

            row.Status = "Approved";
            row.DecidedBy = actor;
            row.DecidedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            try
            {
                var log = await ExecuteAsync(row, ct);

                row.Status = "Executed";
                row.ExecutionLog = log;
                row.ExecutedOn = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                return new ActionDecisionResultDto(true, "Executed", log);
            }
            catch (Exception ex)
            {
                row.Status = "Failed";
                row.ExecutionLog = ex.Message;
                row.ExecutedOn = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return new ActionDecisionResultDto(false, "Failed", ex.Message);
            }
        }

        public async Task<ActionDecisionResultDto> RejectAsync(int id, string actor, string? note, CancellationToken ct = default)
        {
            var row = await _db.PendingAiActions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null) return new ActionDecisionResultDto(false, "NotFound", "Action not found");
            if (row.Status != "Pending")
                return new ActionDecisionResultDto(false, row.Status, $"Already {row.Status}");

            row.Status = "Rejected";
            row.DecidedBy = actor;
            row.DecidedOn = DateTime.UtcNow;
            row.ExecutionLog = note;
            await _db.SaveChangesAsync(ct);
            return new ActionDecisionResultDto(true, "Rejected", note);
        }

        // ── execution router ───────────────────────────────────────────
        private async Task<string> ExecuteAsync(PendingAiAction row, CancellationToken ct)
        {
            return row.Type switch
            {
                "FlashTournament" => await ExecuteFlashTournament(row, ct),
                "CustomerPing"    => await ExecuteCustomerPing(row, ct),
                _                  => throw new InvalidOperationException($"Unknown action type {row.Type}")
            };
        }

        private async Task<string> ExecuteFlashTournament(PendingAiAction row, CancellationToken ct)
        {
            using var jd = JsonDocument.Parse(row.Payload);
            var p = jd.RootElement;

            var game = p.TryGetProperty("game", out var g) ? g.GetString() ?? "Tournament" : "Tournament";
            var startAt = p.TryGetProperty("start_at_iso", out var s) ? s.GetString() ?? "" : "";
            var entry = p.TryGetProperty("entry_fee", out var e) ? e.GetDecimal() : 0m;
            var prize = p.TryGetProperty("prize_pool", out var pr) ? pr.GetDecimal() : 0m;
            var hype = p.TryGetProperty("hype_message", out var h) ? h.GetString() ?? "" : "";

            var phones = ReadStringArray(p, "recipient_phones");
            var names = ReadStringArray(p, "recipient_names");
            if (phones.Count == 0) throw new InvalidOperationException("No recipients provided");

            var template = await _settings.GetRawAsync("WhatsApp.DefaultTemplate", ct) ?? "tournament_invite";

            var recipients = phones.Select((phone, i) =>
            {
                var name = i < names.Count ? names[i] : null;
                var body = hype.Replace("{{name}}", name ?? "player");
                // 4 template params expected: {{1}}=name, {{2}}=game, {{3}}=startAt, {{4}}=prize
                var perParams = new List<string> { name ?? "player", game, FriendlyTime(startAt), $"${prize}" };
                return new WhatsAppRecipient(phone, name, perParams);
            }).ToList();

            var result = await _wa.SendBlastAsync(row.Id, recipients, template, Array.Empty<string>(), ct);
            return $"Tournament '{game}' @ {startAt} — sent {result.Sent}, failed {result.Failed}." +
                   (result.Errors.Count == 0 ? "" : " Errors: " + string.Join("; ", result.Errors.Take(5)));
        }

        private async Task<string> ExecuteCustomerPing(PendingAiAction row, CancellationToken ct)
        {
            using var jd = JsonDocument.Parse(row.Payload);
            if (!jd.RootElement.TryGetProperty("recipients", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Payload missing recipients");

            var template = await _settings.GetRawAsync("WhatsApp.DefaultTemplate", ct) ?? "slot_reservation";

            var list = new List<WhatsAppRecipient>();
            foreach (var r in arr.EnumerateArray())
            {
                var phone = r.GetProperty("phone").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(phone)) continue;
                var name = r.TryGetProperty("name", out var n) ? n.GetString() : null;
                var msg = r.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                // 2 params: {{1}}=name, {{2}}=custom body
                list.Add(new WhatsAppRecipient(phone, name, new[] { name ?? "there", msg }));
            }

            var res = await _wa.SendBlastAsync(row.Id, list, template, Array.Empty<string>(), ct);
            return $"Pinged {res.Sent}, failed {res.Failed}." +
                   (res.Errors.Count == 0 ? "" : " Errors: " + string.Join("; ", res.Errors.Take(5)));
        }

        private static List<string> ReadStringArray(JsonElement obj, string key)
        {
            if (!obj.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array)
                return new List<string>();
            return v.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        private static string FriendlyTime(string iso)
        {
            if (DateTime.TryParse(iso, out var dt))
                return dt.ToString("ddd HH:mm");
            return iso;
        }
    }
}
