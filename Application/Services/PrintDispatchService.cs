using Application.DTOs;
using Application.Services.SignalR;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public interface IPrintDispatchService
    {
        /// <summary>
        /// Builds one ESC/POS ticket per station for the given transaction and pushes a
        /// print job to every enabled printer registered for that station, via the
        /// "Printers" SignalR group. Never throws — a printing failure must not roll back a sale.
        /// </summary>
        Task DispatchOrderTicketsAsync(int transactionId, string createdBy, string? tableNumber = null,
            string? guestName = null, CancellationToken ct = default);

        /// <summary>Sends a small test ticket to a single printer. Returns false if the printer id is unknown.</summary>
        Task<bool> DispatchTestAsync(int printerId, CancellationToken ct = default);
    }

    public class PrintDispatchService : IPrintDispatchService
    {
        private readonly IBaseRepository<Printer> _repoPrinter;
        private readonly IBaseRepository<TransactionItem> _repoTrxItem;
        private readonly IBaseRepository<TransactionRecord> _repoTrx;
        private readonly IReceiptPrintingService _receipts;
        private readonly IHubContext<PrinterHub> _hub;
        private readonly ILogger<PrintDispatchService> _logger;

        public PrintDispatchService(
            IBaseRepository<Printer> repoPrinter,
            IBaseRepository<TransactionItem> repoTrxItem,
            IBaseRepository<TransactionRecord> repoTrx,
            IReceiptPrintingService receipts,
            IHubContext<PrinterHub> hub,
            ILogger<PrintDispatchService> logger)
        {
            _repoPrinter = repoPrinter;
            _repoTrxItem = repoTrxItem;
            _repoTrx = repoTrx;
            _receipts = receipts;
            _hub = hub;
            _logger = logger;
        }

        // Same mapping used when creating KitchenBarOrders.
        private static string? StationFor(string? itemType) => itemType switch
        {
            "Food" => "Kitchen",
            "Drinks" => "Bar",
            "Tobacco" => "Bar",
            _ => null
        };

        public async Task DispatchOrderTicketsAsync(int transactionId, string createdBy, string? tableNumber = null,
            string? guestName = null, CancellationToken ct = default)
        {
            try
            {
                // Load enabled printers first — if none are configured there is nothing to do.
                var printers = await _repoPrinter.Query()
                    .Where(p => p.IsEnabled)
                    .ToListAsync(ct);

                if (printers.Count == 0)
                {
                    _logger.LogInformation(
                        "Print dispatch skipped for Trx {Trx}: no enabled printers configured.", transactionId);
                    return;
                }

                var items = await _repoTrxItem.Query()
                    .Include(ti => ti.Item)
                        .ThenInclude(i => i.Category)
                    .Where(ti => ti.TransactionRecordId == transactionId)
                    .ToListAsync(ct);

                var trx = await _repoTrx.GetByIdAsync(transactionId, asNoTracking: true, ct);
                var orderedAt = trx?.CreatedOn ?? DateTime.UtcNow;
                var comment = trx?.Comment;

                // Group the order's lines by destination station.
                var byStation = new Dictionary<string, List<StationTicketLine>>(StringComparer.OrdinalIgnoreCase);
                foreach (var ti in items)
                {
                    var station = StationFor(ti.Item?.Category?.ItemType);
                    if (station is null) continue;

                    if (!byStation.TryGetValue(station, out var lines))
                        byStation[station] = lines = new List<StationTicketLine>();

                    lines.Add(new StationTicketLine(ti.Quantity, ti.Item!.Name, null));
                }

                if (byStation.Count == 0)
                {
                    _logger.LogInformation(
                        "Print dispatch skipped for Trx {Trx}: no kitchen/bar items on the order.", transactionId);
                    return;
                }

                var dispatched = 0;
                foreach (var (station, lines) in byStation)
                {
                    var stationPrinters = printers
                        .Where(p => string.Equals(p.Station, station, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (stationPrinters.Count == 0)
                    {
                        _logger.LogInformation(
                            "No enabled printer registered for station {Station} (Trx {Trx}).", station, transactionId);
                        continue;
                    }

                    var ticket = new StationTicketDto(
                        station, transactionId, orderedAt, createdBy ?? "", tableNumber, guestName, comment, lines);
                    var payload = Convert.ToBase64String(_receipts.GenerateStationTicket(ticket));

                    foreach (var p in stationPrinters)
                    {
                        var job = new PrintJobDto(
                            p.Id, p.Name, p.Station, p.ConnectionType, p.Address,
                            transactionId, p.CopyCount < 1 ? 1 : p.CopyCount, payload);

                        await _hub.Clients.Group(PrinterHub.PrintersGroup).SendAsync("PrintJob", job, ct);
                        dispatched++;
                    }
                }

                _logger.LogInformation(
                    "Dispatched {Count} print job(s) for Trx {Trx} across {Stations} station(s).",
                    dispatched, transactionId, byStation.Count);
            }
            catch (Exception ex)
            {
                // Printing must never break order creation.
                _logger.LogWarning(ex, "Print dispatch failed for Trx {Trx}; order still completed.", transactionId);
            }
        }

        public async Task<bool> DispatchTestAsync(int printerId, CancellationToken ct = default)
        {
            var p = await _repoPrinter.GetByIdAsync(printerId, asNoTracking: true, ct);
            if (p is null) return false;

            var ticket = new StationTicketDto(
                Station: p.Station,
                TransactionId: 0,
                OrderedAt: DateTime.UtcNow,
                CreatedByUsername: "TEST",
                TableNumber: null,
                GuestName: null,
                Comment: "*** TEST PRINT ***",
                Lines: new List<StationTicketLine>
                {
                    new(1, $"Test ticket for {p.Name}", $"{p.ConnectionType} @ {p.Address}")
                });

            var payload = Convert.ToBase64String(_receipts.GenerateStationTicket(ticket));
            var job = new PrintJobDto(
                p.Id, p.Name, p.Station, p.ConnectionType, p.Address, 0, 1, payload);

            await _hub.Clients.Group(PrinterHub.PrintersGroup).SendAsync("PrintJob", job, ct);
            _logger.LogInformation("Dispatched TEST print job to printer {Printer} (id {Id}).", p.Name, p.Id);
            return true;
        }
    }
}
