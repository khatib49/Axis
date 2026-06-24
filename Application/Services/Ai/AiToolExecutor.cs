using System.Data;
using System.Text.Json;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Application.Services.Ai
{
    /// <summary>
    /// Executes the tools defined in AiToolRegistry. Data tools return a
    /// JSON string (which we feed back to Claude as the tool result).
    /// Proposal tools create a PendingAiAction and return its id.
    ///
    /// Critically: this class never writes to anything but PendingAiActions
    /// (and the chat tables). It cannot mutate business data — that requires
    /// admin approval flowing through PendingActionsService.
    /// </summary>
    public class AiToolExecutor
    {
        private readonly ApplicationDbContext _db;
        private static readonly JsonSerializerOptions _jsonOut = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        // Hard ceiling for run_select_query results — keep token usage sane.
        private const int MaxFreeQueryRows = 200;

        public AiToolExecutor(ApplicationDbContext db) => _db = db;

        public async Task<string> ExecuteAsync(
            string toolName,
            JsonElement input,
            int? conversationId,
            string actor,
            CancellationToken ct = default)
        {
            try
            {
                return toolName switch
                {
                    "get_revenue_summary"  => await GetRevenueSummary(input, ct),
                    "get_top_items"        => await GetTopItems(input, ct),
                    "get_low_stock"        => await GetLowStock(ct),
                    "get_occupancy_now"    => await GetOccupancyNow(ct),
                    "get_recent_players"   => await GetRecentPlayers(input, ct),
                    "get_due_regulars"     => await GetDueRegulars(input, ct),
                    "get_expense_summary"  => await GetExpenseSummary(input, ct),
                    "run_select_query"     => await RunSelectQuery(input, ct),

                    "propose_flash_tournament" => await ProposeFlashTournament(input, conversationId, actor, ct),
                    "propose_customer_ping"    => await ProposeCustomerPing(input, conversationId, actor, ct),

                    _ => JsonSerializer.Serialize(new { error = $"Unknown tool '{toolName}'" })
                };
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        // ── Data tools ─────────────────────────────────────────────────
        private async Task<string> GetRevenueSummary(JsonElement input, CancellationToken ct)
        {
            var from = ParseDate(input, "from") ?? DateTime.UtcNow.Date.AddDays(-30);
            var to   = (ParseDate(input, "to")  ?? DateTime.UtcNow.Date).Date.AddDays(1);
            var channel = GetStr(input, "channel");

            var q = _db.Transactions.AsNoTracking()
                .Where(t => t.CreatedOn >= from && t.CreatedOn < to);

            if (!string.IsNullOrWhiteSpace(channel))
            {
                q = q.Where(t => t.Channel != null && t.Channel.Name == channel);
            }

            // Net = sum of TotalPrice (the recorded settle amount).
            var totals = await q.GroupBy(_ => 1)
                .Select(g => new {
                    Count = g.Count(),
                    Net   = g.Sum(x => (decimal?)x.TotalPrice ?? 0m),
                })
                .FirstOrDefaultAsync(ct);

            return JsonSerializer.Serialize(new {
                from = from.ToString("yyyy-MM-dd"),
                to   = to.AddDays(-1).ToString("yyyy-MM-dd"),
                channel,
                transaction_count = totals?.Count ?? 0,
                net_revenue = totals?.Net ?? 0m
            }, _jsonOut);
        }

        private async Task<string> GetTopItems(JsonElement input, CancellationToken ct)
        {
            var from = ParseDate(input, "from") ?? DateTime.UtcNow.Date.AddDays(-30);
            var to   = (ParseDate(input, "to")  ?? DateTime.UtcNow.Date).Date.AddDays(1);
            var by   = (GetStr(input, "by") ?? "revenue").ToLowerInvariant();
            var limit = Math.Clamp(GetInt(input, "limit") ?? 10, 1, 50);

            var q = _db.TransactionItems.AsNoTracking()
                .Where(ti => ti.TransactionRecord != null
                    && ti.TransactionRecord.CreatedOn >= from
                    && ti.TransactionRecord.CreatedOn < to);

            // Item-level price comes from the Item itself — we don't snapshot
            // it on the line. Margin reporting elsewhere uses the same source.
            var grouped = q.GroupBy(ti => new { ti.ItemId, ti.Item!.Name, ti.Item.Price })
                .Select(g => new {
                    g.Key.ItemId,
                    name = g.Key.Name,
                    quantity = g.Sum(x => (int?)x.Quantity ?? 0),
                    revenue  = (decimal)g.Key.Price * g.Sum(x => (int?)x.Quantity ?? 0),
                });

            var rows = by == "quantity"
                ? await grouped.OrderByDescending(x => x.quantity).Take(limit).ToListAsync(ct)
                : await grouped.OrderByDescending(x => x.revenue).Take(limit).ToListAsync(ct);

            return JsonSerializer.Serialize(new { sorted_by = by, rows }, _jsonOut);
        }

        private async Task<string> GetLowStock(CancellationToken ct)
        {
            var rows = await _db.Set<Ingredient>().AsNoTracking()
                .Where(i => i.IsActive
                    && i.ReorderLevel.HasValue
                    && i.QuantityOnHand <= i.ReorderLevel.Value)
                .OrderBy(i => i.QuantityOnHand)
                .Select(i => new {
                    i.Id, i.Name, i.Unit, on_hand = i.QuantityOnHand,
                    reorder = i.ReorderLevel,
                    negative = i.QuantityOnHand < 0
                })
                .Take(100)
                .ToListAsync(ct);

            return JsonSerializer.Serialize(new { count = rows.Count, rows }, _jsonOut);
        }

        private async Task<string> GetOccupancyNow(CancellationToken ct)
        {
            // Room has no IsActive flag — count all rooms.
            var totalRooms  = await _db.Set<Room>().AsNoTracking().CountAsync(ct);
            var activeSessions = await _db.Set<GameSession>().AsNoTracking()
                .Where(s => s.Status == "Active" && s.EndTime == null)
                .CountAsync(ct);

            var since = DateTime.UtcNow.AddDays(-14);
            var topGame = await _db.Set<GameSession>().AsNoTracking()
                .Where(s => s.StartTime >= since && s.Game != null)
                .GroupBy(s => s.Game!.Name)
                .Select(g => new { game = g.Key, plays = g.Count() })
                .OrderByDescending(x => x.plays)
                .FirstOrDefaultAsync(ct);

            var pct = totalRooms == 0 ? 0 : (int)Math.Round(100.0 * activeSessions / totalRooms);

            return JsonSerializer.Serialize(new {
                total_rooms = totalRooms,
                active_sessions = activeSessions,
                occupancy_pct = pct,
                top_game_last_14d = topGame?.game,
                top_game_play_count = topGame?.plays ?? 0
            }, _jsonOut);
        }

        private async Task<string> GetRecentPlayers(JsonElement input, CancellationToken ct)
        {
            var game        = GetStr(input, "game");
            var withinDays  = Math.Clamp(GetInt(input, "within_days") ?? 30, 1, 365);
            var limit       = Math.Clamp(GetInt(input, "limit") ?? 30, 1, 200);
            var since       = DateTime.UtcNow.AddDays(-withinDays);

            // Pull recent loyalty customers — their phone is the primary
            // hook for invites. We approximate "played this game" via
            // recent sessions joined on the user.
            //
            // IMPORTANT: do NOT 'await using' this connection — it is the
            // SAME connection EF holds for the request, and disposing it
            // here kills SaveChanges later in the chat loop.
            var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
            await conn.OpenSafelyAsync(ct);

            var sql = @"
                SELECT DISTINCT lc.phone_number, lc.name, lc.last_updated
                FROM loyalty_customers lc
                WHERE lc.last_updated >= @since
                ORDER BY lc.last_updated DESC
                LIMIT @lim";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("since", since);
            cmd.Parameters.AddWithValue("lim", limit);

            var list = new List<object>();
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    list.Add(new {
                        phone = r.GetString(0),
                        name  = r.IsDBNull(1) ? null : r.GetString(1),
                        last_visit = r.GetDateTime(2)
                    });
                }
            }

            return JsonSerializer.Serialize(new {
                game,
                within_days = withinDays,
                count = list.Count,
                customers = list
            }, _jsonOut);
        }

        private async Task<string> GetDueRegulars(JsonElement input, CancellationToken ct)
        {
            var withinHours = Math.Clamp(GetInt(input, "within_hours") ?? 24, 1, 168);
            var limit       = Math.Clamp(GetInt(input, "limit") ?? 20, 1, 100);

            // Heuristic: customer's avg gap between visits in last 90d.
            // If their (last_visit + avg_gap) falls inside the next
            // window, they're "due".
            //
            // Don't dispose — see the comment in get_recent_players.
            var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
            await conn.OpenSafelyAsync(ct);

            var sql = @"
                SELECT phone_number, name, last_updated
                FROM loyalty_customers
                WHERE last_updated < NOW() - INTERVAL '7 days'
                  AND last_updated > NOW() - INTERVAL '90 days'
                ORDER BY last_updated ASC
                LIMIT @lim";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("lim", limit);

            var list = new List<object>();
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    list.Add(new {
                        phone = r.GetString(0),
                        name  = r.IsDBNull(1) ? null : r.GetString(1),
                        last_visit = r.GetDateTime(2)
                    });
                }
            }

            return JsonSerializer.Serialize(new {
                within_hours = withinHours,
                count = list.Count,
                regulars = list
            }, _jsonOut);
        }

        private async Task<string> GetExpenseSummary(JsonElement input, CancellationToken ct)
        {
            var from = ParseDate(input, "from") ?? DateTime.UtcNow.Date.AddDays(-30);
            var to   = (ParseDate(input, "to")  ?? DateTime.UtcNow.Date).Date.AddDays(1);

            var rows = await _db.Set<Expense>().AsNoTracking()
                .Where(e => e.FromDate < to && e.ToDate >= from)
                .GroupBy(e => e.Category!.Name)
                .Select(g => new {
                    category = g.Key,
                    total = g.Sum(x => x.Amount),
                    count = g.Count()
                })
                .OrderByDescending(x => x.total)
                .ToListAsync(ct);

            return JsonSerializer.Serialize(new {
                from = from.ToString("yyyy-MM-dd"),
                to   = to.AddDays(-1).ToString("yyyy-MM-dd"),
                categories = rows,
                grand_total = rows.Sum(r => r.total)
            }, _jsonOut);
        }

        private async Task<string> RunSelectQuery(JsonElement input, CancellationToken ct)
        {
            var sql = GetStr(input, "sql")?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("sql is required");

            SqlSafetyGate.EnsureSelectOnly(sql);

            // Don't dispose — see the comment in get_recent_players.
            var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
            await conn.OpenSafelyAsync(ct);

            await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 20 };
            await using var r = await cmd.ExecuteReaderAsync(ct);

            var cols = new string[r.FieldCount];
            for (int i = 0; i < r.FieldCount; i++) cols[i] = r.GetName(i);

            var rows = new List<Dictionary<string, object?>>();
            while (await r.ReadAsync(ct) && rows.Count < MaxFreeQueryRows)
            {
                var dict = new Dictionary<string, object?>(r.FieldCount);
                for (int i = 0; i < r.FieldCount; i++)
                    dict[cols[i]] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add(dict);
            }

            return JsonSerializer.Serialize(new {
                columns = cols,
                row_count = rows.Count,
                truncated = rows.Count >= MaxFreeQueryRows,
                rows
            }, _jsonOut);
        }

        // ── Proposal tools ─────────────────────────────────────────────
        private async Task<string> ProposeFlashTournament(
            JsonElement input, int? convId, string actor, CancellationToken ct)
        {
            var game = GetStr(input, "game") ?? "Tournament";
            var startAt = GetStr(input, "start_at_iso") ?? DateTime.UtcNow.AddHours(2).ToString("o");
            var entry = GetDecimal(input, "entry_fee") ?? 0m;
            var prize = GetDecimal(input, "prize_pool") ?? 0m;
            var reason = GetStr(input, "reason");

            var action = new PendingAiAction
            {
                Type = "FlashTournament",
                Title = $"Flash {game} tournament @ {startAt}",
                Summary = reason ?? $"Entry ${entry}, prize ${prize}",
                Payload = input.GetRawText(),
                ProposedBy = actor,
                ProposedOn = DateTime.UtcNow,
                ConversationId = convId,
                Status = "Pending"
            };
            _db.PendingAiActions.Add(action);
            await _db.SaveChangesAsync(ct);

            return JsonSerializer.Serialize(new {
                proposal_id = action.Id,
                status = "pending_approval",
                message = "Tournament proposal created. Awaiting admin approval before any WhatsApp messages are sent."
            }, _jsonOut);
        }

        private async Task<string> ProposeCustomerPing(
            JsonElement input, int? convId, string actor, CancellationToken ct)
        {
            int count = 0;
            if (input.TryGetProperty("recipients", out var recArr) && recArr.ValueKind == JsonValueKind.Array)
                count = recArr.GetArrayLength();

            var action = new PendingAiAction
            {
                Type = "CustomerPing",
                Title = $"Ping {count} customer(s)",
                Summary = GetStr(input, "reason"),
                Payload = input.GetRawText(),
                ProposedBy = actor,
                ProposedOn = DateTime.UtcNow,
                ConversationId = convId,
                Status = "Pending"
            };
            _db.PendingAiActions.Add(action);
            await _db.SaveChangesAsync(ct);

            return JsonSerializer.Serialize(new {
                proposal_id = action.Id,
                status = "pending_approval"
            }, _jsonOut);
        }

        // ── helpers ────────────────────────────────────────────────────
        private static DateTime? ParseDate(JsonElement input, string name)
        {
            if (!input.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Null) return null;
            if (!DateTime.TryParse(v.GetString(), out var d)) return null;
            // Npgsql 'timestamp with time zone' requires Kind=Utc on every
            // DateTime parameter. Inputs without a zone come back as
            // Unspecified — force-treat them as UTC dates.
            return d.Kind switch
            {
                DateTimeKind.Utc         => d,
                DateTimeKind.Local       => d.ToUniversalTime(),
                _                        => DateTime.SpecifyKind(d, DateTimeKind.Utc),
            };
        }

        private static string? GetStr(JsonElement input, string name)
            => input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int? GetInt(JsonElement input, string name)
            => input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : (int?)null;

        private static decimal? GetDecimal(JsonElement input, string name)
            => input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : (decimal?)null;
    }

    internal static class NpgsqlConnExt
    {
        // The DbConnection might already be opened by EF; only open if needed.
        public static async Task OpenSafelyAsync(this NpgsqlConnection conn, CancellationToken ct)
        {
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        }
    }
}
