using System.Security.Cryptography;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase5
{
    public interface ITournamentService
    {
        Task<List<TournamentSummaryDto>> ListAsync(bool upcomingOnly, CancellationToken ct = default);
        Task<TournamentDto?> GetAsync(int id, CancellationToken ct = default);
        Task<TournamentDto?> GetPublicAsync(string token, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> CreateAsync(TournamentUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> UpdateAsync(int id, TournamentUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> SetStatusAsync(int id, string status, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> RegisterPublicAsync(string token, TournamentRegisterDto dto, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> AddPlayerAsync(int id, TournamentAddPlayerDto dto, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> UpdatePlayerAsync(int id, int participantId, TournamentAddPlayerDto dto, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> RemovePlayerAsync(int id, int participantId, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> StartAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> ReportResultAsync(int id, int matchId, TournamentResultDto dto, CancellationToken ct = default);
        Task<BaseResponse<TournamentDto>> PayPrizesAsync(int id, CancellationToken ct = default);
    }

    /// <summary>
    /// Single-elimination tournaments: public sign-up, seeded bracket with byes,
    /// results that advance winners, free stations assigned to ready matches,
    /// places and prize split, prizes paid into the winners' wallets.
    /// </summary>
    public class TournamentService : ITournamentService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly IWalletService _wallet;
        private readonly ILogger<TournamentService> _log;

        public TournamentService(ApplicationDbContext db, ICurrentUserAccessor user, IWalletService wallet, ILogger<TournamentService> log)
        { _db = db; _user = user; _wallet = wallet; _log = log; }

        public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();

        public async Task<List<TournamentSummaryDto>> ListAsync(bool upcomingOnly, CancellationToken ct = default)
        {
            var q = _db.Set<Tournament>().AsNoTracking();
            if (upcomingOnly) q = q.Where(t => t.Status == TournamentStatuses.Open || t.Status == TournamentStatuses.Running);
            var list = await q.OrderByDescending(t => t.StartsOn).Take(200).ToListAsync(ct);
            var ids = list.Select(t => t.Id).ToList();
            var counts = await _db.Set<TournamentParticipant>().AsNoTracking().Where(p => ids.Contains(p.TournamentId))
                .GroupBy(p => p.TournamentId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
            return list.Select(t => new TournamentSummaryDto(t.Id, t.Name, t.GameTitle, t.StartsOn, t.Status, counts.GetValueOrDefault(t.Id), t.MaxPlayers, t.EntryFee, t.PrizePool, t.PublicToken)).ToList();
        }

        public async Task<TournamentDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return t is null ? null : await ToDtoAsync(t, includePhones: true, ct);
        }

        public async Task<TournamentDto?> GetPublicAsync(string token, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicToken == token && x.Status != TournamentStatuses.Draft, ct);
            return t is null ? null : await ToDtoAsync(t, includePhones: false, ct);
        }

        private async Task<TournamentDto> ToDtoAsync(Tournament t, bool includePhones, CancellationToken ct)
        {
            var ps = await _db.Set<TournamentParticipant>().AsNoTracking().Where(p => p.TournamentId == t.Id).OrderBy(p => p.Seed ?? int.MaxValue).ThenBy(p => p.Id).ToListAsync(ct);
            var ms = await _db.Set<TournamentMatch>().AsNoTracking().Where(m => m.TournamentId == t.Id).OrderBy(m => m.Round).ThenBy(m => m.Slot).ToListAsync(ct);
            var setIds = ms.Where(m => m.SetId != null).Select(m => m.SetId!.Value).Distinct().ToList();
            var sets = await _db.Set<Set>().AsNoTracking().Where(s => setIds.Contains(s.Id)).Select(s => new { s.Id, s.Name, Room = s.Room.Name }).ToDictionaryAsync(s => s.Id, ct);
            var names = ps.ToDictionary(p => p.Id, p => p.DisplayName);
            string? N(int? id) => id is int i && names.TryGetValue(i, out var n) ? n : null;
            return new TournamentDto(t.Id, t.Name, t.GameTitle, t.Rules, t.StartsOn, t.MaxPlayers, t.EntryFee, t.PrizePool, t.PrizeSplit, t.RoomIds, t.Status, t.PublicToken, t.PrizesPaid,
                ms.Count == 0 ? 0 : ms.Max(m => m.Round),
                ps.Select(p => new TournamentParticipantDto(p.Id, p.DisplayName, includePhones ? p.Phone : null, includePhones ? p.UserId : null, p.Paid, p.PaidVia, p.Seed, p.FinalPlace, p.PrizeAmount)).ToList(),
                ms.Select(m => new TournamentMatchDto(m.Id, m.Round, m.Slot, m.Player1Id, N(m.Player1Id), m.Player2Id, N(m.Player2Id), m.WinnerId, m.Score, m.SetId,
                    m.SetId is int s && sets.TryGetValue(s, out var st) ? $"{st.Room} · {st.Name}" : null, m.Status)).ToList());
        }

        private static string? Validate(TournamentUpsertDto d)
        {
            if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Trim().Length > 150) return "Name is required (max 150).";
            if (string.IsNullOrWhiteSpace(d.GameTitle) || d.GameTitle.Trim().Length > 120) return "Game is required (max 120).";
            if (d.MaxPlayers is < 2 or > 256) return "Players must be between 2 and 256.";
            if (d.EntryFee < 0 || d.PrizePool < 0) return "Fee and prize pool cannot be negative.";
            var split = ParseSplit(d.PrizeSplit ?? "70,30");
            if (split is null) return "Prize split must be percentages like 70,30 that add up to 100.";
            if (d.RoomIds is { Length: > 0 } && OwnerNotifier.SplitList(d.RoomIds).Any(x => !int.TryParse(x, out _))) return "Room ids must be numbers.";
            return null;
        }

        public static List<decimal>? ParseSplit(string s)
        {
            var parts = OwnerNotifier.SplitList(s);
            var vals = new List<decimal>();
            foreach (var p in parts) { if (!decimal.TryParse(p, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) || v < 0) return null; vals.Add(v); }
            return vals.Count == 0 || vals.Sum() != 100m ? null : vals;
        }

        private static void Apply(Tournament t, TournamentUpsertDto d)
        {
            t.Name = d.Name.Trim(); t.GameTitle = d.GameTitle.Trim(); t.StartsOn = DateTime.SpecifyKind(d.StartsOn, DateTimeKind.Utc);
            t.MaxPlayers = d.MaxPlayers; t.EntryFee = Math.Round(d.EntryFee, 2); t.PrizePool = Math.Round(d.PrizePool, 2);
            t.PrizeSplit = string.Join(",", ParseSplit(d.PrizeSplit ?? "70,30")!); t.Rules = string.IsNullOrWhiteSpace(d.Rules) ? null : d.Rules.Trim();
            t.RoomIds = string.IsNullOrWhiteSpace(d.RoomIds) ? null : string.Join(",", OwnerNotifier.SplitList(d.RoomIds));
        }

        public async Task<BaseResponse<TournamentDto>> CreateAsync(TournamentUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto);
            if (err is not null) return Fail("Validation", err);
            var t = new Tournament { PublicToken = NewToken(), CreatedBy = _user.UserName, BranchId = _user.BranchId };
            Apply(t, dto);
            _db.Add(t);
            await _db.SaveChangesAsync(ct);
            return Ok((await GetAsync(t.Id, ct))!, "Tournament created.");
        }

        public async Task<BaseResponse<TournamentDto>> UpdateAsync(int id, TournamentUpsertDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Fail("NotFound", "Tournament not found.");
            if (t.Status is TournamentStatuses.Running or TournamentStatuses.Completed) return Fail("Locked", "A running or finished tournament cannot be edited.");
            var err = Validate(dto);
            if (err is not null) return Fail("Validation", err);
            var count = await _db.Set<TournamentParticipant>().CountAsync(p => p.TournamentId == id, ct);
            if (dto.MaxPlayers < count) return Fail("Validation", $"{count} players already signed up.");
            Apply(t, dto);
            await _db.SaveChangesAsync(ct);
            return Ok((await GetAsync(id, ct))!, "Saved.");
        }

        public async Task<BaseResponse<TournamentDto>> SetStatusAsync(int id, string status, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Fail("NotFound", "Tournament not found.");
            var allowed = (t.Status, status) switch
            {
                (TournamentStatuses.Draft, TournamentStatuses.Open) => true,
                (TournamentStatuses.Open, TournamentStatuses.Draft) => true,
                (TournamentStatuses.Draft or TournamentStatuses.Open or TournamentStatuses.Running, TournamentStatuses.Cancelled) => true,
                _ => false,
            };
            if (!allowed) return Fail("Transition", $"Cannot move from {t.Status} to {status}.");
            t.Status = status;
            await _db.SaveChangesAsync(ct);
            return Ok((await GetAsync(id, ct))!, $"Tournament is now {status}.");
        }

        public async Task<BaseResponse<TournamentDto>> RegisterPublicAsync(string token, TournamentRegisterDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().FirstOrDefaultAsync(x => x.PublicToken == token, ct);
            if (t is null || t.Status == TournamentStatuses.Draft) return Fail("NotFound", "Tournament not found.");
            var r = await AddInternalAsync(t, new TournamentAddPlayerDto(dto.DisplayName, dto.Phone, null, t.EntryFee == 0, t.EntryFee == 0 ? "Free" : null), true, ct);
            if (r is not null) return Fail("Validation", r);
            var dtoOut = (await GetPublicAsync(token, ct))!;
            return Ok(dtoOut, t.EntryFee > 0 ? $"You are in! Pay the ${t.EntryFee:0.00} entry fee at the counter before {t.StartsOn:HH:mm}." : "You are in!");
        }

        public async Task<BaseResponse<TournamentDto>> AddPlayerAsync(int id, TournamentAddPlayerDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Fail("NotFound", "Tournament not found.");
            var r = await AddInternalAsync(t, dto, false, ct);
            return r is null ? Ok((await GetAsync(id, ct))!, "Player added.") : Fail("Validation", r);
        }

        private async Task<string?> AddInternalAsync(Tournament t, TournamentAddPlayerDto dto, bool isPublic, CancellationToken ct)
        {
            if (t.Status is not (TournamentStatuses.Open or TournamentStatuses.Draft) || (isPublic && t.Status != TournamentStatuses.Open)) return "Sign-up is closed.";
            var name = (dto.DisplayName ?? "").Trim();
            if (name.Length is < 2 or > 80) return "Player name must be 2-80 characters.";
            var phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            if (isPublic && (phone is null || phone.Count(char.IsDigit) < 7)) return "A phone number is required.";
            var existing = await _db.Set<TournamentParticipant>().Where(p => p.TournamentId == t.Id).ToListAsync(ct);
            if (existing.Count >= t.MaxPlayers) return "The tournament is full.";
            if (existing.Any(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))) return "That player name is taken in this tournament.";
            if (phone is not null && existing.Any(p => p.Phone == phone)) return "This phone number is already signed up.";
            if (dto.UserId is int uid && !await _db.Users.AnyAsync(u => u.Id == uid, ct)) return "Client account not found.";
            _db.Add(new TournamentParticipant { TournamentId = t.Id, DisplayName = name, Phone = phone, UserId = dto.UserId, Paid = dto.Paid, PaidVia = dto.Paid ? dto.PaidVia ?? "Counter" : null });
            await _db.SaveChangesAsync(ct);
            return null;
        }

        public async Task<BaseResponse<TournamentDto>> UpdatePlayerAsync(int id, int participantId, TournamentAddPlayerDto dto, CancellationToken ct = default)
        {
            var p = await _db.Set<TournamentParticipant>().FirstOrDefaultAsync(x => x.Id == participantId && x.TournamentId == id, ct);
            if (p is null) return Fail("NotFound", "Player not found.");
            if (dto.UserId is int uid && !await _db.Users.AnyAsync(u => u.Id == uid, ct)) return Fail("Validation", "Client account not found.");
            p.Paid = dto.Paid; p.PaidVia = dto.Paid ? dto.PaidVia ?? "Counter" : null; p.UserId = dto.UserId;
            if (!string.IsNullOrWhiteSpace(dto.Phone)) p.Phone = dto.Phone.Trim();
            await _db.SaveChangesAsync(ct);
            return Ok((await GetAsync(id, ct))!, "Player updated.");
        }

        public async Task<BaseResponse<TournamentDto>> RemovePlayerAsync(int id, int participantId, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Fail("NotFound", "Tournament not found.");
            if (t.Status is TournamentStatuses.Running or TournamentStatuses.Completed) return Fail("Locked", "Players cannot be removed once the bracket started.");
            var p = await _db.Set<TournamentParticipant>().FirstOrDefaultAsync(x => x.Id == participantId && x.TournamentId == id, ct);
            if (p is null) return Fail("NotFound", "Player not found.");
            _db.Remove(p);
            await _db.SaveChangesAsync(ct);
            return Ok((await GetAsync(id, ct))!, "Player removed.");
        }

        /// <summary>Bracket size = next power of two; seeds pair 1 vs last, 2 vs second-last; empty spots are byes.</summary>
        public static List<(int round, int slot, int? p1, int? p2)> BuildFirstRound(IReadOnlyList<int> orderedPlayers, out int rounds)
        {
            var n = orderedPlayers.Count;
            var size = 1; while (size < n) size <<= 1;
            rounds = (int)Math.Log2(size);
            var seeds = SeedOrder(size);
            var list = new List<(int, int, int?, int?)>();
            for (var i = 0; i < size / 2; i++)
            {
                int? P(int seed) => seed <= n ? orderedPlayers[seed - 1] : null;
                list.Add((1, i, P(seeds[2 * i]), P(seeds[2 * i + 1])));
            }
            return list;
        }

        /// <summary>Standard bracket seed order (1,8,4,5,2,7,3,6 for 8) so top seeds meet late.</summary>
        public static int[] SeedOrder(int size)
        {
            var order = new List<int> { 1 };
            while (order.Count < size)
            {
                var m = order.Count * 2 + 1;
                order = order.SelectMany(s => new[] { s, m - s }).ToList();
            }
            return order.ToArray();
        }

        public async Task<BaseResponse<TournamentDto>> StartAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Fail("NotFound", "Tournament not found.");
            if (t.Status is not (TournamentStatuses.Open or TournamentStatuses.Draft)) return Fail("Transition", $"The tournament is {t.Status}.");
            var players = await _db.Set<TournamentParticipant>().Where(p => p.TournamentId == id && (p.Paid || t.EntryFee == 0)).ToListAsync(ct);
            if (players.Count < 2) return Fail("Validation", "At least 2 paid players are needed to start.");
            var rng = new Random(id);
            var ordered = players.OrderBy(p => p.Seed ?? int.MaxValue).ThenBy(_ => rng.Next()).ToList();
            for (var i = 0; i < ordered.Count; i++) ordered[i].Seed = i + 1;

            var first = BuildFirstRound(ordered.Select(p => p.Id).ToList(), out var rounds);
            foreach (var (round, slot, p1, p2) in first)
                _db.Add(new TournamentMatch { TournamentId = id, Round = round, Slot = slot, Player1Id = p1, Player2Id = p2, Status = "Pending" });
            for (var r = 2; r <= rounds; r++)
                for (var s = 0; s < (1 << (rounds - r)); s++)
                    _db.Add(new TournamentMatch { TournamentId = id, Round = r, Slot = s, Status = "Pending" });
            t.Status = TournamentStatuses.Running;
            await _db.SaveChangesAsync(ct);

            // Byes: a lone player goes straight through.
            var byes = await _db.Set<TournamentMatch>().Where(m => m.TournamentId == id && m.Round == 1 && (m.Player1Id == null || m.Player2Id == null)).ToListAsync(ct);
            foreach (var m in byes)
            {
                m.WinnerId = m.Player1Id ?? m.Player2Id; m.Status = "Done"; m.Score = "bye"; m.CompletedOn = DateTime.UtcNow;
                await AdvanceAsync(id, m, ct);
            }
            await RefreshReadyAsync(id, ct);
            await _db.SaveChangesAsync(ct);
            return Ok((await GetAsync(id, ct))!, $"Bracket of {first.Count * 2} with {ordered.Count} players started.");
        }

        private async Task AdvanceAsync(int id, TournamentMatch done, CancellationToken ct)
        {
            var next = await _db.Set<TournamentMatch>().FirstOrDefaultAsync(m => m.TournamentId == id && m.Round == done.Round + 1 && m.Slot == done.Slot / 2, ct);
            if (next is null) return;
            if (done.Slot % 2 == 0) next.Player1Id = done.WinnerId; else next.Player2Id = done.WinnerId;
        }

        /// <summary>Marks matches with both players as Ready and gives them a free station.</summary>
        private async Task RefreshReadyAsync(int id, CancellationToken ct)
        {
            await _db.SaveChangesAsync(ct);
            var t = await _db.Set<Tournament>().AsNoTracking().FirstAsync(x => x.Id == id, ct);
            var matches = await _db.Set<TournamentMatch>().Where(m => m.TournamentId == id && m.Status == "Pending" && m.Player1Id != null && m.Player2Id != null).OrderBy(m => m.Round).ThenBy(m => m.Slot).ToListAsync(ct);
            if (matches.Count == 0) return;
            var roomIds = OwnerNotifier.SplitList(t.RoomIds).Select(int.Parse).ToList();
            var setsQ = _db.Set<Set>().AsNoTracking();
            if (roomIds.Count > 0) setsQ = setsQ.Where(s => roomIds.Contains(s.RoomId));
            var sets = await setsQ.OrderBy(s => s.RoomId).ThenBy(s => s.Id).Select(s => s.Id).ToListAsync(ct);
            var busyMatch = await _db.Set<TournamentMatch>().AsNoTracking().Where(m => m.SetId != null && (m.Status == "Ready" || m.Status == "Playing")).Select(m => m.SetId!.Value).ToListAsync(ct);
            var busySession = await StationBusy.RunningSetIdsAsync(_db, ct);
            var free = new Queue<int>(sets.Where(s => !busyMatch.Contains(s) && !busySession.Contains(s)));
            foreach (var m in matches)
            {
                m.Status = "Ready";
                if (free.Count > 0) m.SetId = free.Dequeue();
            }
        }

        public async Task<BaseResponse<TournamentDto>> ReportResultAsync(int id, int matchId, TournamentResultDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Fail("NotFound", "Tournament not found.");
            if (t.Status != TournamentStatuses.Running) return Fail("Transition", "The tournament is not running.");
            var m = await _db.Set<TournamentMatch>().FirstOrDefaultAsync(x => x.Id == matchId && x.TournamentId == id, ct);
            if (m is null) return Fail("NotFound", "Match not found.");
            if (m.Status == "Done") return Fail("Done", "This match already has a result.");
            if (m.Player1Id is null || m.Player2Id is null) return Fail("Validation", "Both players are not known yet.");
            if (dto.WinnerId != m.Player1Id && dto.WinnerId != m.Player2Id) return Fail("Validation", "The winner must be one of the two players.");
            m.WinnerId = dto.WinnerId; m.Score = string.IsNullOrWhiteSpace(dto.Score) ? null : dto.Score.Trim(); m.Status = "Done"; m.CompletedOn = DateTime.UtcNow; m.SetId ??= null;
            var rounds = await _db.Set<TournamentMatch>().Where(x => x.TournamentId == id).MaxAsync(x => x.Round, ct);
            if (m.Round == rounds) await FinishAsync(t, m, rounds, ct);
            else { await AdvanceAsync(id, m, ct); await RefreshReadyAsync(id, ct); }
            await _db.SaveChangesAsync(ct);
            return Ok((await GetAsync(id, ct))!, m.Round == rounds ? "Final recorded: the tournament is complete." : "Result recorded.");
        }

        private async Task FinishAsync(Tournament t, TournamentMatch final, int rounds, CancellationToken ct)
        {
            var ps = await _db.Set<TournamentParticipant>().Where(p => p.TournamentId == t.Id).ToListAsync(ct);
            var matches = await _db.Set<TournamentMatch>().AsNoTracking().Where(x => x.TournamentId == t.Id && x.Status == "Done").ToListAsync(ct);
            matches.RemoveAll(x => x.Id == final.Id); matches.Add(final);
            // Place = 1 for the champion; losers of round r share place 2^(rounds-r)+1.
            foreach (var p in ps) p.FinalPlace = null;
            ps.First(p => p.Id == final.WinnerId).FinalPlace = 1;
            foreach (var x in matches.Where(x => x.Score != "bye"))
            {
                var loser = x.WinnerId == x.Player1Id ? x.Player2Id : x.Player1Id;
                var lp = ps.FirstOrDefault(p => p.Id == loser);
                if (lp is not null) lp.FinalPlace = (1 << (rounds - x.Round)) + 1;
            }
            var split = ParseSplit(t.PrizeSplit) ?? new List<decimal> { 100 };
            foreach (var p in ps) p.PrizeAmount = 0;
            for (var place = 1; place <= split.Count; place++)
            {
                var winners = ps.Where(p => p.FinalPlace == place).ToList();
                // Shared places (two semi-final losers) split the money of the places they cover.
                if (winners.Count == 0) continue;
                var covered = split.Skip(place - 1).Take(winners.Count).Sum();
                foreach (var w in winners) w.PrizeAmount = Math.Round(t.PrizePool * covered / 100m / winners.Count, 2);
            }
            t.Status = TournamentStatuses.Completed;
        }

        public async Task<BaseResponse<TournamentDto>> PayPrizesAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<Tournament>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Fail("NotFound", "Tournament not found.");
            if (t.Status != TournamentStatuses.Completed) return Fail("Transition", "Prizes are paid after the final.");
            if (t.PrizesPaid) return Fail("Done", "Prizes were already paid.");
            var winners = await _db.Set<TournamentParticipant>().Where(p => p.TournamentId == id && p.PrizeAmount > 0).ToListAsync(ct);
            var missing = winners.Where(w => w.UserId is null).Select(w => w.DisplayName).ToList();
            if (missing.Count > 0) return Fail("Validation", $"Link a client account to {string.Join(", ", missing)} first (prizes go to the wallet).");
            foreach (var w in winners)
            {
                var r = await _wallet.AdjustAsync(w.UserId!.Value, new WalletAdjustRequestDto(w.PrizeAmount, false, $"Tournament prize: {t.Name} (place {w.FinalPlace})"), _user.UserName, ct);
                if (!r.Success) return Fail("Wallet", $"Could not pay {w.DisplayName}: {r.Message}");
            }
            t.PrizesPaid = true;
            await _db.SaveChangesAsync(ct);
            return Ok((await GetAsync(id, ct))!, $"Paid {winners.Count} prize(s) into wallets.");
        }

        private static BaseResponse<TournamentDto> Fail(string e, string m) => new(false, e, m);
        private static BaseResponse<TournamentDto> Ok(TournamentDto d, string m) => new(true, null, m, d);
    }

    /// <summary>Which stations have a game session running now.</summary>
    public static class StationBusy
    {
        public static readonly int[] RunningStatuses = { 4, 7 };
        public static async Task<HashSet<int>> RunningSetIdsAsync(ApplicationDbContext db, CancellationToken ct)
            => (await db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.SetId != null && t.GameId != null && RunningStatuses.Contains(t.StatusId))
                .Select(t => t.SetId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
    }
}
