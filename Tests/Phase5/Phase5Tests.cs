using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Application.Services.Phase5;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Tests.OrderingChannels.OrderingChannelsTestSupport;

namespace Tests.Phase5
{
    internal sealed class FakeNotifier : IOwnerNotifier
    {
        public List<(string Title, string Text, string Channels)> Owner { get; } = new();
        public List<(string To, string Text)> Telegram { get; } = new();
        public List<(string To, string Text)> WhatsApp { get; } = new();
        public Task<NotifyResult> NotifyOwnersAsync(string title, string text, IEnumerable<string> channels, string? extra = null, CancellationToken ct = default)
        { Owner.Add((title, text, string.Join(",", channels))); return Task.FromResult(new NotifyResult(1, 0, new())); }
        public Task<bool> SendTelegramAsync(string chatId, string text, CancellationToken ct = default) { Telegram.Add((chatId, text)); return Task.FromResult(true); }
        public Task<bool> SendWhatsAppTextAsync(string phone, string text, CancellationToken ct = default) { WhatsApp.Add((phone, text)); return Task.FromResult(true); }
    }

    internal static class P5
    {
        public static Mock<IIntegrationSettingsService> Settings(Dictionary<string, string?>? values = null)
        {
            var m = new Mock<IIntegrationSettingsService>();
            values ??= new();
            m.Setup(s => s.GetRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string k, CancellationToken _) => values.TryGetValue(k, out var v) ? v : null);
            m.Setup(s => s.UpsertAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback((string k, string? v, string? a, CancellationToken _) => values[k] = v).Returns(Task.CompletedTask);
            return m;
        }

        public static void SeedStations(ApplicationDbContext db)
        {
            db.Add(new Room { Id = 1, Name = "PS5 Room" });
            db.Add(new Set { Id = 1, RoomId = 1, Name = "Station 1" });
            db.Add(new Set { Id = 2, RoomId = 1, Name = "Station 2" });
            db.Add(new Game { Id = 1, Name = "EA FC" });
            db.SaveChanges(); db.ChangeTracker.Clear();
        }
    }

    public class TournamentTests : IDisposable
    {
        private readonly ApplicationDbContext _db = NewDb();
        private readonly Mock<IWalletService> _wallet = new();
        private readonly TournamentService _svc;
        public TournamentTests()
        {
            P5.SeedStations(_db);
            _wallet.Setup(w => w.AdjustAsync(It.IsAny<int>(), It.IsAny<WalletAdjustRequestDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BaseResponse<WalletDto>(true, null, "ok"));
            _svc = new TournamentService(_db, User().Object, _wallet.Object, NullLogger<TournamentService>.Instance);
        }
        public void Dispose() => _db.Dispose();

        [Fact]
        public void Seed_order_and_first_round_put_byes_on_top_seeds()
        {
            TournamentService.SeedOrder(8).Should().Equal(1, 8, 4, 5, 2, 7, 3, 6);
            var r = TournamentService.BuildFirstRound(new[] { 11, 12, 13, 14, 15 }, out var rounds);
            rounds.Should().Be(3);
            r.Should().HaveCount(4);
            r[0].Should().Be((1, 0, (int?)11, (int?)null));           // seed 1 bye
            r.Count(m => m.p1 is null || m.p2 is null).Should().Be(3);  // 3 byes for 5 players in 8
        }

        [Fact]
        public void Prize_split_must_add_to_100() { TournamentService.ParseSplit("70,30").Should().Equal(70m, 30m); TournamentService.ParseSplit("70,20").Should().BeNull(); }

        [Fact]
        public async Task Full_tournament_with_byes_places_prizes_and_wallet_payout()
        {
            var t = (await _svc.CreateAsync(new TournamentUpsertDto("Friday Cup", "EA FC", DateTime.UtcNow.AddDays(1), 8, 5, 100, "50,30,20", null, "1"))).Data!;
            (await _svc.RegisterPublicAsync(t.PublicToken, new("Ali", "70111111"))).Error.Should().Be("NotFound"); // draft is hidden
            await _svc.SetStatusAsync(t.Id, TournamentStatuses.Open);
            foreach (var (n, i) in new[] { "Ali", "Bea", "Cyd", "Dan", "Eve" }.Select((n, i) => (n, i)))
                (await _svc.RegisterPublicAsync(t.PublicToken, new(n, $"7011111{i}"))).Success.Should().BeTrue();
            (await _svc.RegisterPublicAsync(t.PublicToken, new("ali", "70999999"))).Success.Should().BeFalse(); // duplicate name
            (await _svc.StartAsync(t.Id)).Error.Should().Be("Validation");     // nobody paid yet
            _db.AddRange(Enumerable.Range(100, 5).Select(i => new Domain.Identity.AppUser { Id = i, UserName = $"u{i}", StatusId = 1 })); _db.SaveChanges();
            var players = (await _svc.GetAsync(t.Id))!.Participants;
            (await _svc.UpdatePlayerAsync(t.Id, players[0].Id, new(players[0].DisplayName, null, 999, true, "Counter"))).Error.Should().Be("Validation");
            for (var i = 0; i < players.Count; i++)
                (await _svc.UpdatePlayerAsync(t.Id, players[i].Id, new(players[i].DisplayName, null, 100 + i, true, "Counter"))).Success.Should().BeTrue();

            var started = await _svc.StartAsync(t.Id);
            started.Success.Should().BeTrue(started.Message);
            var dto = started.Data!;
            dto.Rounds.Should().Be(3);
            dto.Matches.Count(m => m.Round == 1 && m.Status == "Done").Should().Be(3); // byes
            var ready = dto.Matches.Where(m => m.Status == "Ready").ToList();
            ready.Should().NotBeEmpty();
            ready.Where(m => m.SetId != null).Select(m => m.SetId).Should().OnlyHaveUniqueItems();

            // play until the end: player 1 always wins
            for (var guard = 0; guard < 10; guard++)
            {
                var cur = (await _svc.GetAsync(t.Id))!;
                if (cur.Status == TournamentStatuses.Completed) break;
                foreach (var m in cur.Matches.Where(m => m.Status == "Ready"))
                    (await _svc.ReportResultAsync(t.Id, m.Id, new(m.Player1Id!.Value, "2-1"))).Success.Should().BeTrue();
            }
            var done = (await _svc.GetAsync(t.Id))!;
            done.Status.Should().Be(TournamentStatuses.Completed);
            done.Participants.Count(p => p.FinalPlace == 1).Should().Be(1);
            done.Participants.Count(p => p.FinalPlace == 2).Should().Be(1);
            done.Participants.Single(p => p.FinalPlace == 1).PrizeAmount.Should().Be(50m);
            done.Participants.Single(p => p.FinalPlace == 2).PrizeAmount.Should().Be(30m);
            done.Participants.Where(p => p.FinalPlace == 3).Sum(p => p.PrizeAmount).Should().Be(20m);

            (await _svc.PayPrizesAsync(t.Id)).Success.Should().BeTrue();
            _wallet.Verify(w => w.AdjustAsync(It.IsAny<int>(), It.Is<WalletAdjustRequestDto>(r => r.Delta > 0 && !r.CashOut), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
            (await _svc.PayPrizesAsync(t.Id)).Error.Should().Be("Done");
        }

        [Fact]
        public async Task Result_must_name_one_of_the_two_players_and_public_view_hides_phones()
        {
            var t = (await _svc.CreateAsync(new TournamentUpsertDto("Duel", "Tekken", DateTime.UtcNow.AddDays(1), 2, 0, 0, "100", null, null))).Data!;
            await _svc.SetStatusAsync(t.Id, TournamentStatuses.Open);
            await _svc.RegisterPublicAsync(t.PublicToken, new("Ann", "70000001"));
            await _svc.RegisterPublicAsync(t.PublicToken, new("Bob", "70000002"));
            (await _svc.RegisterPublicAsync(t.PublicToken, new("Cat", "70000003"))).Message.Should().Contain("full");
            var s = (await _svc.StartAsync(t.Id)).Data!;
            var final = s.Matches.Single();
            (await _svc.ReportResultAsync(t.Id, final.Id, new(9999, null))).Error.Should().Be("Validation");
            (await _svc.ReportResultAsync(t.Id, final.Id, new(final.Player2Id!.Value, "3-0"))).Data!.Status.Should().Be("Completed");
            (await _svc.GetPublicAsync(t.PublicToken))!.Participants.Should().OnlyContain(p => p.Phone == null);
        }
    }

    public class BookingAndStationTests : IDisposable
    {
        private readonly ApplicationDbContext _db = NewDb();
        private readonly FakeNotifier _notify = new();
        private readonly BookingService _svc;
        public BookingAndStationTests()
        {
            P5.SeedStations(_db);
            _svc = new BookingService(_db, P5.Settings(new() { ["Booking.OpenHour"] = "0", ["Booking.CloseHour"] = "24", ["Venue.TimeZone"] = "UTC" }).Object, User().Object, _notify, NullLogger<BookingService>.Instance);
        }
        public void Dispose() => _db.Dispose();
        private static DateTime At(int hoursFromNow) { var n = DateTime.UtcNow.AddHours(hoursFromNow); return new DateTime(n.Year, n.Month, n.Day, n.Hour, 0, 0, DateTimeKind.Utc); }

        [Fact]
        public async Task Overlapping_bookings_are_refused_and_free_slots_reflect_them()
        {
            var a = await _svc.CreateAsync(new BookingCreateDto(1, At(3), 60, "Rami", "70123456", 2), false);
            a.Success.Should().BeTrue(a.Message);
            a.Data!.Status.Should().Be("Confirmed");
            (await _svc.CreateAsync(new BookingCreateDto(1, At(3).AddMinutes(30), 60, "Other", "70999999"), false)).Error.Should().Be("Taken");
            (await _svc.CreateAsync(new BookingCreateDto(2, At(3), 60, "Other", "70999999"), false)).Success.Should().BeTrue();
            var slots = (await _svc.AvailabilityAsync(1, At(3).Date, 60)).Data!;
            slots.Single(s => s.StartsOn == At(3)).Free.Should().BeFalse();
            _notify.Owner.Should().Contain(o => o.Title == "New booking");
        }

        [Fact]
        public async Task Validation_past_times_phone_and_running_session()
        {
            (await _svc.CreateAsync(new BookingCreateDto(1, At(-2), 60, "Rami", "70123456"), false)).Error.Should().Be("Validation");
            (await _svc.CreateAsync(new BookingCreateDto(1, At(3), 60, "Rami", "12"), false)).Error.Should().Be("Validation");
            (await _svc.CreateAsync(new BookingCreateDto(99, At(3), 60, "Rami", "70123456"), false)).Error.Should().Be("NotFound");
            _db.Add(new TransactionRecord { Id = 500, SetId = 2, GameId = 1, StatusId = 4, CreatedBy = "c", ExpectedEndOn = At(5) }); _db.SaveChanges();
            (await _svc.CreateAsync(new BookingCreateDto(2, At(3), 60, "Rami", "70123456"), false)).Error.Should().Be("Taken");
            (await _svc.CreateAsync(new BookingCreateDto(2, At(6), 60, "Rami", "70123456"), false)).Success.Should().BeTrue();
        }

        [Fact]
        public async Task Check_in_window_cancel_rules_and_no_show_job()
        {
            var soon = (await _svc.CreateAsync(new BookingCreateDto(1, DateTime.UtcNow.AddMinutes(20), 60, "Soon", "70000011"), false)).Data!;
            var later = (await _svc.CreateAsync(new BookingCreateDto(2, At(5), 60, "Later", "70000012"), false)).Data!;
            (await _svc.CheckInPublicAsync(later.Token)).Success.Should().BeFalse();
            (await _svc.CheckInPublicAsync(soon.Token)).Data!.Status.Should().Be("CheckedIn");
            _notify.Owner.Should().Contain(o => o.Title == "Guest arrived");
            (await _svc.CancelPublicAsync(soon.Token)).Success.Should().BeFalse();
            (await _svc.CancelPublicAsync(later.Token)).Data!.Status.Should().Be("Cancelled");
            var late = new StationBooking { SetId = 1, StartsOn = DateTime.UtcNow.AddHours(-2), EndsOn = DateTime.UtcNow.AddHours(-1), CustomerName = "x", Phone = "70000013", Token = "t-late", Status = "Confirmed" };
            _db.Add(late); _db.SaveChanges();
            (await _svc.MarkNoShowsAsync()).Should().Be(1);
            (await _db.Set<StationBooking>().AsNoTracking().SingleAsync(b => b.Token == "t-late")).Status.Should().Be("NoShow");
        }

        [Fact]
        public async Task Station_orders_need_a_running_game_and_reuse_one_open_invoice()
        {
            SeedMenu(_db);
            var calls = new List<(List<OrderItemRequest> items, string comment, bool open, int? channelId)>();
            var trx = TrxService(_db, calls);
            trx.Setup(t => t.AddItemsToOpenInvoice(It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, List<OrderItemRequest> items, string by, CancellationToken _) => new BaseResponse<TransactionDto>(true, null, "ok", Trx(id, 20m)));
            var svc = new StationOrderService(_db, trx.Object, P5.Settings().Object, _notify, NullLogger<StationOrderService>.Instance);
            var code = await svc.EnsureCodeAsync(1, false);
            (await svc.OrderAsync(code.Token, new(new() { new(1, 1) }, null))).Error.Should().Be("NoSession");
            _db.Add(new TransactionRecord { Id = 600, SetId = 1, GameId = 1, StatusId = 4, CreatedBy = "c" }); _db.SaveChanges();
            (await svc.OrderAsync(code.Token, new(new() { new(42, 1) }, null))).Error.Should().Be("Validation");
            var first = await svc.OrderAsync(code.Token, new(new() { new(1, 2) }, "no ice"));
            first.Success.Should().BeTrue(first.Message);
            calls.Single().comment.Should().StartWith("Station order · PS5 Room · Station 1");
            var inv = _db.Set<TransactionRecord>().First(t => t.Id == first.Data!.InvoiceId);
            inv.SetId = 1; inv.Comment = calls.Single().comment; _db.SaveChanges(); // the mocked CreateCoffeeShopOrder does not store these
            (await svc.OrderAsync(code.Token, new(new() { new(2, 1) }, null))).Success.Should().BeTrue();
            calls.Should().HaveCount(1); // second order went to AddItemsToOpenInvoice
            var info = (await svc.GetAsync(code.Token))!;
            info.SessionRunning.Should().BeTrue();
            info.Menu.Select(m => m.Name).Should().Contain("Latte");
            (await svc.GetAsync("nope")).Should().BeNull();
        }
    }

    public class OwnerAndParentTests : IDisposable
    {
        private readonly ApplicationDbContext _db = NewDb();
        private readonly FakeNotifier _notify = new();
        public void Dispose() => _db.Dispose();

        [Fact]
        public void Alert_hours_and_comparators()
        {
            AlertService.InHours(9, 17, 10).Should().BeTrue(); AlertService.InHours(9, 17, 18).Should().BeFalse();
            AlertService.InHours(22, 2, 23).Should().BeTrue(); AlertService.InHours(22, 2, 12).Should().BeFalse();
            AlertService.InHours(0, 0, 5).Should().BeTrue();
            AlertService.Triggered("Below", 50, 100).Should().BeTrue(); AlertService.Triggered("Above", 50, 100).Should().BeFalse();
        }

        [Fact]
        public async Task Alert_fires_once_per_cooldown_and_logs_the_event()
        {
            _db.Add(new Ingredient { Id = 1, Name = "Milk", Unit = "l", QuantityOnHand = 1, ReorderLevel = 5, IsActive = true });
            _db.Add(new Ingredient { Id = 2, Name = "Beans", Unit = "kg", QuantityOnHand = 9, ReorderLevel = 2, IsActive = true });
            _db.SaveChanges();
            var svc = new AlertService(_db, P5.Settings(new() { ["Venue.TimeZone"] = "UTC" }).Object, _notify, NullLogger<AlertService>.Instance);
            (await svc.CreateAsync(new AlertRuleUpsertDto("x", "Nope", "Above", 0, 30, 0, 0, "Telegram", null, 60))).Success.Should().BeFalse();
            var r = (await svc.CreateAsync(new AlertRuleUpsertDto("Low stock", AlertMetrics.LowStockCount, "Above", 0, 30, 0, 0, "Telegram,InApp", null, 60))).Data!;
            (await svc.MeasureAsync(AlertMetrics.LowStockCount, 30)).Should().Be(1);
            var run1 = await svc.RunAsync();
            run1.Fired.Should().Be(1);
            _notify.Owner.Single().Text.Should().Contain("Items at or below reorder level is 1");
            (await svc.RunAsync()).Fired.Should().Be(0); // cooldown
            (await svc.EventsAsync(10)).Should().ContainSingle(e => e.RuleId == r.Id);
        }

        [Fact]
        public async Task Sales_and_open_invoice_metrics()
        {
            _db.Add(new TransactionRecord { Id = 1, StatusId = 6, TotalPrice = 40, CreatedBy = "c", CreatedOn = DateTime.UtcNow, PaidOn = DateTime.UtcNow });
            _db.Add(new TransactionRecord { Id = 2, StatusId = 6, TotalPrice = 10, CreatedBy = "c", CreatedOn = DateTime.UtcNow.AddDays(-2), PaidOn = DateTime.UtcNow.AddDays(-2) });
            _db.Add(new TransactionRecord { Id = 3, StatusId = 7, TotalPrice = 5, CreatedBy = "c", CreatedOn = DateTime.UtcNow.AddHours(-3) });
            _db.SaveChanges();
            var svc = new AlertService(_db, P5.Settings(new() { ["Venue.TimeZone"] = "UTC" }).Object, _notify, NullLogger<AlertService>.Instance);
            (await svc.MeasureAsync(AlertMetrics.SalesToday, 30)).Should().Be(40);
            (await svc.MeasureAsync(AlertMetrics.LargeSale, 60)).Should().Be(40);
            (await svc.MeasureAsync(AlertMetrics.OpenInvoicesOlderThan, 60)).Should().Be(1);
            (await svc.MeasureAsync(AlertMetrics.OpenInvoicesOlderThan, 600)).Should().Be(0);
        }

        [Fact]
        public async Task Parent_daily_limit_blocks_spend_and_job_tells_the_parent()
        {
            _db.Add(new Domain.Identity.AppUser { Id = 7, UserName = "kid", DisplayName = "Kid", StatusId = 1 });
            _db.Add(new Wallet { Id = 1, UserId = 7, Balance = 20, IsActive = true });
            _db.SaveChanges();
            var svc = new ParentWalletService(_db, P5.Settings(new() { ["Venue.TimeZone"] = "UTC" }).Object, _notify, Mock.Of<IEmailSender>(), NullLogger<ParentWalletService>.Instance);
            (await svc.UpsertAsync(new ParentLinkUpsertDto(7, "Mom", null, null, null, 10, 5))).Error.Should().Be("Validation");
            var link = (await svc.UpsertAsync(new ParentLinkUpsertDto(7, "Mom", "+96170111222", "12345", null, 10, 5))).Data!;
            (await svc.CheckAsync(7, 8)).Allowed.Should().BeTrue();
            _db.Add(new WalletTransaction { WalletId = 1, Type = "Spend", Amount = 8, BalanceAfter = 12, CreatedBy = "c", CreatedOn = DateTime.UtcNow });
            _db.Set<Wallet>().First().Balance = 4; _db.SaveChanges();
            var (ok, msg) = await svc.CheckAsync(7, 3);
            ok.Should().BeFalse(); msg.Should().Contain("$10.00").And.Contain("$2.00 left");
            (await svc.CheckAsync(99, 1000)).Allowed.Should().BeTrue();

            _db.Add(new Game { Id = 3, Name = "FIFA" });
            _db.Add(new TransactionRecord { Id = 900, UserId = 7, GameId = 3, StatusId = 6, TotalPrice = 6, Hours = 1, CreatedBy = "c" });
            _db.SaveChanges();
            var sent = await svc.NotifySessionEndsAsync();
            sent.Should().Be(3); // spend + low balance + session end
            _notify.Telegram.Select(t => t.Text).Should().Contain(t => t.Contains("spent $8.00")).And.Contain(t => t.Contains("is low")).And.Contain(t => t.Contains("finished playing FIFA"));
            _notify.WhatsApp.Should().HaveCount(3);
            (await svc.NotifySessionEndsAsync()).Should().Be(0); // nothing new, low balance once a day
            var view = (await svc.PublicViewAsync(link.Token))!;
            view.ChildName.Should().Be("Kid"); view.RemainingToday.Should().Be(2m);
        }

        [Fact]
        public async Task Owner_bot_answers_only_allowed_chats_and_uses_ai()
        {
            var settings = P5.Settings(new() { [OwnerNotifier.AllowedTelegramKey] = "111", ["Anthropic.ApiKey"] = "k", ["Venue.TimeZone"] = "UTC" });
            var ai = new Mock<IAiChatService>();
            ai.Setup(a => a.SendAsync(It.IsAny<AiSendMessageRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AiSendMessageResponse(42, new List<AiMessageDto> { new(1, "assistant", "You sold $40 today.", null, null, null, DateTime.UtcNow) }, new List<int>()));
            var bot = new OwnerBotService(_db, settings.Object, _notify, ai.Object, Mock.Of<IHttpClientFactory>(), NullLogger<OwnerBotService>.Instance);
            (await bot.AnswerAsync("Telegram", "999", "Stranger", "sales?")).Should().Contain("Your chat id is 999");
            ai.Verify(a => a.SendAsync(It.IsAny<AiSendMessageRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            (await bot.AnswerAsync("Telegram", "111", "Owner", "/summary")).Should().Contain("Sales today: $0.00");
            (await bot.AnswerAsync("Telegram", "111", "Owner", "كم بعنا اليوم؟")).Should().Be("You sold $40 today.");
            (await _db.Set<OwnerBotChat>().SingleAsync(c => c.ChatId == "111")).ConversationId.Should().Be(42);

            await bot.HandleTelegramAsync("{\"message\":{\"chat\":{\"id\":111},\"from\":{\"first_name\":\"Owner\"},\"text\":\"/help\"}}");
            _notify.Telegram.Last().To.Should().Be("111");
        }

        [Fact]
        public async Task WhatsApp_webhook_signature_and_verify_token()
        {
            var settings = P5.Settings(new() { [OwnerBotService.WhatsAppAppSecretKey] = "appsecret", [OwnerBotService.WhatsAppVerifyKey] = "vt" });
            var bot = new OwnerBotService(_db, settings.Object, _notify, Mock.Of<IAiChatService>(), Mock.Of<IHttpClientFactory>(), NullLogger<OwnerBotService>.Instance);
            var body = "{\"entry\":[]}";
            var sig = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("appsecret"), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            (await bot.CheckWhatsAppSignatureAsync(sig, body)).Should().BeTrue();
            (await bot.CheckWhatsAppSignatureAsync("sha256=00", body)).Should().BeFalse();
            (await bot.VerifyWhatsAppAsync("subscribe", "vt", "abc")).Should().Be("abc");
            (await bot.VerifyWhatsAppAsync("subscribe", "bad", "abc")).Should().BeNull();
            await bot.HandleWhatsAppAsync("{\"entry\":[{\"changes\":[{\"value\":{\"contacts\":[{\"profile\":{\"name\":\"Sam\"}}],\"messages\":[{\"from\":\"96170123456\",\"text\":{\"body\":\"hi\"}}]}}]}]}");
            _notify.WhatsApp.Single().To.Should().Be("+96170123456");
            _notify.WhatsApp.Single().Text.Should().Contain("96170123456");
        }

        [Fact]
        public async Task Notifier_posts_to_telegram_api()
        {
            var handler = new Capture();
            var f = new Mock<IHttpClientFactory>(); f.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
            var n = new OwnerNotifier(_db, P5.Settings(new() { [OwnerNotifier.TelegramTokenKey] = "TOKEN", [OwnerNotifier.AllowedTelegramKey] = "111,222" }).Object, f.Object, Mock.Of<IEmailSender>(), NullLogger<OwnerNotifier>.Instance);
            var r = await n.NotifyOwnersAsync("Title", "Body", new[] { "Telegram", "WhatsApp" });
            r.Sent.Should().Be(2);
            r.Notes.Should().Contain("WhatsApp: no numbers");
            handler.Urls.Should().OnlyContain(u => u == "https://api.telegram.org/botTOKEN/sendMessage");
            handler.Bodies.Should().Contain(b => b.Contains("\"chat_id\":\"222\""));
        }

        private sealed class Capture : HttpMessageHandler
        {
            public List<string> Urls { get; } = new(); public List<string> Bodies { get; } = new();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            { Urls.Add(r.RequestUri!.ToString()); Bodies.Add(await r.Content!.ReadAsStringAsync(ct)); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") }; }
        }
    }

    public class AiToolsAndExportTests : IDisposable
    {
        private readonly ApplicationDbContext _db = NewDb();
        public void Dispose() => _db.Dispose();

        private AiToolsService Svc(Mock<IPurchaseService>? p = null, Dictionary<string, string?>? s = null, HttpMessageHandler? h = null)
        {
            var f = new Mock<IHttpClientFactory>(); if (h != null) f.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(h, false));
            return new AiToolsService(_db, P5.Settings(s ?? new() { ["Venue.TimeZone"] = "UTC" }).Object, new Application.Services.Ai.ClaudeApiClient(f.Object), (p ?? new Mock<IPurchaseService>()).Object, User().Object, NullLogger<AiToolsService>.Instance);
        }
        private static IFormFile File(string name, string content) { var b = Encoding.UTF8.GetBytes(content); return new FormFile(new MemoryStream(b), 0, b.Length, "file", name); }

        [Fact]
        public async Task Menu_csv_import_creates_categories_and_items_and_skips_duplicates()
        {
            SeedMenu(_db); // has Latte in "Coffee"
            var svc = Svc();
            var draft = await svc.ExtractAsync("menu", File("menu.csv", "name,category,price,kind\nLatte,Coffee,5\nMocha,Coffee,5.5\n\"Club, sandwich\",Food,8,Food\nCola,Drinks,2,Drinks\nBad,Food,abc\n"));
            draft.Success.Should().BeTrue(draft.Message);
            var applied = await svc.ApplyMenuAsync(draft.Data!.Id, new(50));
            applied.Data!.Created.Should().Be(3);
            applied.Data.Skipped.Should().Be(1);
            (await _db.Set<Item>().SingleAsync(i => i.Name == "Club, sandwich")).Quantity.Should().Be(50);
            (await _db.Set<Category>().SingleAsync(c => c.Name == "Drinks")).ItemType.Should().Be("Drinks");
            (await svc.ApplyMenuAsync(draft.Data.Id, new())).Error.Should().Be("Done");
        }

        [Fact]
        public async Task Photo_import_needs_the_ai_key_then_reads_claude_json_and_matches_ingredients()
        {
            (await Svc().ExtractAsync("invoice", File("inv.jpg", "x"))).Error.Should().Be("NotConfigured");
            _db.Add(new Ingredient { Id = 5, Name = "Whole Milk", Unit = "l", IsActive = true });
            _db.Add(new Supplier { Id = 3, Name = "Dairy Co", IsActive = true });
            _db.SaveChanges();
            var claude = new Canned("{\"content\":[{\"type\":\"text\",\"text\":\"Here: {\\\"supplier\\\":\\\"Dairy Co\\\",\\\"invoiceNumber\\\":\\\"A-9\\\",\\\"lines\\\":[{\\\"name\\\":\\\"Milk whole 1L\\\",\\\"quantity\\\":12,\\\"unitCost\\\":1.1}]}\"}]}");
            var purchases = new Mock<IPurchaseService>();
            purchases.Setup(p => p.CreateAsync(It.IsAny<PurchaseCreateDto>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PurchaseCreateDto d, string? a, CancellationToken _) => new PurchaseDto(77, d.SupplierId, null, d.PurchaseDate, d.InvoiceNumber, 13.2m, d.Notes, a, DateTime.UtcNow, new()));
            var svc = Svc(purchases, new() { ["Anthropic.ApiKey"] = "k", ["Venue.TimeZone"] = "UTC" }, claude);
            var d = await svc.ExtractAsync("invoice", File("inv.jpg", "fakejpeg"));
            d.Success.Should().BeTrue(d.Message);
            using var doc = JsonDocument.Parse(d.Data!.Json);
            doc.RootElement.GetProperty("supplierId").GetInt32().Should().Be(3);
            doc.RootElement.GetProperty("lines")[0].GetProperty("ingredientId").GetInt32().Should().Be(5);
            claude.LastBody.Should().Contain("\"type\":\"image\"").And.Contain("image/jpeg");
            (await svc.ApplyInvoiceAsync(d.Data.Id, new InvoiceApplyDto(3, null, null, "A-9", new() { new(999, 1, 1, null) }))).Error.Should().Be("Validation");
            var ok = await svc.ApplyInvoiceAsync(d.Data.Id, new InvoiceApplyDto(3, null, null, "A-9", new() { new(5, 12, 1.1m, null) }));
            ok.Data!.PurchaseId.Should().Be(77);
        }

        [Fact]
        public void Forecast_weights_recent_weeks_more_and_similarity_matches_words()
        {
            AiToolsService.Predict(w => w == 1 ? 10 : 0).Should().Be(Math.Round(10m * 8 / 36, 1));
            AiToolsService.Predict(_ => 4).Should().Be(4);
            AiToolsService.Similarity("Milk whole 1L", "Whole Milk").Should().Be(1);
            AiToolsService.Similarity("Tomatoes", "Cheese").Should().Be(0);
        }

        [Fact]
        public async Task Forecast_turns_item_sales_into_ingredient_purchases()
        {
            SeedMenu(_db);
            _db.Add(new Ingredient { Id = 1, Name = "Milk", Unit = "l", QuantityOnHand = 1, ReorderLevel = 1, IsActive = true });
            _db.Add(new RecipeLine { ItemId = 1, IngredientId = 1, Quantity = 0.2m });
            var id = 1000;
            for (var w = 1; w <= 8; w++)
                for (var d = 0; d < 7; d++)
                {
                    var on = DateTime.UtcNow.Date.AddDays(-7 * w + d);
                    if (on >= DateTime.UtcNow.Date) continue;
                    _db.Add(new TransactionRecord { Id = ++id, StatusId = 6, CreatedBy = "c", CreatedOn = on.AddHours(12), PaidOn = on.AddHours(12), TotalPrice = 10 });
                    _db.Add(new TransactionItem { TransactionRecordId = id, ItemId = 1, Quantity = 2 });
                }
            _db.SaveChanges();
            var f = await Svc().ForecastAsync(7);
            var latte = f.Items.Single(i => i.ItemId == 1);
            latte.Days.Should().HaveCount(7);
            latte.Total.Should().BeGreaterThan(10);
            var milk = f.Ingredients.Single();
            milk.Needed.Should().Be(Math.Round(latte.Total * 0.2m, 2));
            milk.SuggestedPurchase.Should().Be(Math.Max(0, Math.Round(milk.Needed + 1 - 1, 2)));
        }

        [Fact]
        public async Task Journal_exports_in_xero_quickbooks_and_odoo_formats()
        {
            _db.Add(new AccountType { Id = 1, TypeName = "Asset", NormalBalance = "Debit" });
            _db.Add(new Account { Id = 1, AccountNumber = "1000", AccountName = "Cash", AccountTypeId = 1 });
            _db.Add(new Account { Id = 2, AccountNumber = "4000", AccountName = "Sales, F&B", AccountTypeId = 1 });
            _db.Add(new JournalEntry { Id = 1, EntryNumber = "JE-1", EntryDate = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc), Description = "=SUM(A1)", IsPosted = true, ReferenceType = "Sale" });
            _db.Add(new JournalEntry { Id = 2, EntryNumber = "JE-2", EntryDate = new DateTime(2026, 11, 3, 11, 0, 0, DateTimeKind.Utc), Description = "void", IsPosted = true, IsVoided = true, ReferenceType = "Sale" });
            _db.Add(new JournalEntryLine { JournalEntryId = 1, AccountId = 1, DebitAmount = 12.5m, LineNumber = 1 });
            _db.Add(new JournalEntryLine { JournalEntryId = 1, AccountId = 2, CreditAmount = 12.5m, LineNumber = 2 });
            _db.Add(new JournalEntryLine { JournalEntryId = 2, AccountId = 1, DebitAmount = 99m, LineNumber = 1 });
            _db.SaveChanges();
            var svc = new AccountingExportService(_db);
            var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc); var to = from.AddDays(30);
            var xero = await svc.ExportAsync("xero", from, to);
            xero.Entries.Should().Be(1);
            xero.Csv.Should().Contain("03/11/2026").And.Contain(",1000,Tax Exempt,12.50").And.Contain(",4000,Tax Exempt,-12.50").And.Contain("'=SUM(A1)").And.NotContain("99.00");
            var qb = await svc.ExportAsync("quickbooks", from, to);
            qb.Csv.Should().Contain("JE-1,11/03/2026,1000 Cash,12.50,").And.Contain("\"4000 Sales, F&B\",,12.50");
            var odoo = (await svc.ExportAsync("odoo", from, to)).Csv.Split('\n');
            odoo[1].Should().StartWith("JE-1,2026-11-03,Miscellaneous Operations,1000");
            odoo[2].Should().StartWith(",,,4000");
            await FluentActions.Invoking(() => svc.ExportAsync("sap", from, to)).Should().ThrowAsync<ArgumentException>();
        }

        private sealed class Canned : HttpMessageHandler
        {
            private readonly string _json; public string LastBody { get; private set; } = "";
            public Canned(string json) { _json = json; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            { LastBody = await r.Content!.ReadAsStringAsync(ct); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json) }; }
        }
    }
}
