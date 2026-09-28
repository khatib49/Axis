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

        /// <summary>
        /// Prints ONLY the lines just added to an already-open invoice (with
        /// the guest name and an "ADDED ITEMS" banner), so the kitchen never
        /// re-cooks the original order. Never throws.
        /// </summary>
        Task DispatchAddedItemsTicketsAsync(int transactionId,
            IReadOnlyList<(int itemId, int quantity, string? addOnNote)> addedLines,
            string createdBy, string? tableNumber = null, string? guestName = null,
            CancellationToken ct = default);
    }

    public class PrintDispatchService : IPrintDispatchService
    {
        private readonly IBaseRepository<Printer> _repoPrinter;
        private readonly IBaseRepository<TransactionItem> _repoTrxItem;
        private readonly IBaseRepository<TransactionItemAddOn> _repoTrxItemAddOn;
        private readonly IBaseRepository<TransactionItemVariant> _repoTrxItemVariant;
        private readonly IBaseRepository<TransactionRecord> _repoTrx;
        private readonly IReceiptPrintingService _receipts;
        private readonly IHubContext<PrinterHub> _hub;
        private readonly ILogger<PrintDispatchService> _logger;

        public PrintDispatchService(
            IBaseRepository<Printer> repoPrinter,
            IBaseRepository<TransactionItem> repoTrxItem,
            IBaseRepository<TransactionItemAddOn> repoTrxItemAddOn,
            IBaseRepository<TransactionItemVariant> repoTrxItemVariant,
            IBaseRepository<TransactionRecord> repoTrx,
            IReceiptPrintingService receipts,
            IHubContext<PrinterHub> hub,
            ILogger<PrintDispatchService> logger)
        {
            _repoPrinter = repoPrinter;
            _repoTrxItem = repoTrxItem;
            _repoTrxItemAddOn = repoTrxItemAddOn;
            _repoTrxItemVariant = repoTrxItemVariant;
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
                var persons = trx?.numberOfPersons ?? 0;

                // Add-ons per line — the kitchen must see "+1x Oat Milk" or
                // the drink comes out wrong.
                var addOnsByItem = await _repoTrxItemAddOn.Query()
                    .AsNoTracking()
                    .Where(a => a.TransactionRecordId == transactionId)
                    .GroupBy(a => a.ItemId)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => string.Join(", ", g.OrderBy(x => x.Id).Select(x => $"+{x.Quantity}x {x.Name}")),
                        ct);

                // Colour / type split per line ("2× Black, 1× Green").
                var variantsByItem = await _repoTrxItemVariant.Query()
                    .AsNoTracking()
                    .Where(v => v.TransactionRecordId == transactionId)
                    .GroupBy(v => v.ItemId)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => string.Join(", ", g.OrderBy(x => x.Id).Select(x => $"{x.Quantity}× {x.Name}")),
                        ct);

                // Group the order's lines by destination station.
                var byStation = new Dictionary<string, List<StationTicketLine>>(StringComparer.OrdinalIgnoreCase);
                foreach (var ti in items)
                {
                    var station = StationFor(ti.Item?.Category?.ItemType);
                    if (station is null) continue;

                    if (!byStation.TryGetValue(station, out var lines))
                        byStation[station] = lines = new List<StationTicketLine>();

                    addOnsByItem.TryGetValue(ti.ItemId, out var addOnNote);
                    if (variantsByItem.TryGetValue(ti.ItemId, out var variantNote))
                        addOnNote = string.IsNullOrEmpty(addOnNote) ? variantNote : $"{variantNote} · {addOnNote}";
                    lines.Add(new StationTicketLine(ti.Quantity, ti.Item!.Name, addOnNote));
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
                        station, transactionId, orderedAt, createdBy ?? "", tableNumber, guestName, comment, lines,
                        Persons: persons);
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

        public async Task DispatchAddedItemsTicketsAsync(int transactionId,
            IReadOnlyList<(int itemId, int quantity, string? addOnNote)> addedLines,
            string createdBy, string? tableNumber = null, string? guestName = null,
            CancellationToken ct = default)
        {
            try
            {
                if (addedLines == null || addedLines.Count == 0) return;

                var printers = await _repoPrinter.Query()
                    .Where(p => p.IsEnabled)
                    .ToListAsync(ct);
                if (printers.Count == 0) return;

                // Resolve the added items' names + stations. Quantities come
                // from the CALLER (the delta), never from the invoice — the
                // whole point is not re-printing what the kitchen already has.
                var itemIds = addedLines.Select(l => l.itemId).Distinct().ToList();
                var itemsById = await _repoTrxItem.Query()
                    .AsNoTracking()
                    .Where(ti => ti.TransactionRecordId == transactionId && itemIds.Contains(ti.ItemId))
                    .Include(ti => ti.Item)
                        .ThenInclude(i => i.Category)
                    .Select(ti => new { ti.ItemId, ti.Item!.Name, ItemType = ti.Item.Category != null ? ti.Item.Category.ItemType : null })
                    .ToDictionaryAsync(x => x.ItemId, ct);

                var trx = await _repoTrx.GetByIdAsync(transactionId, asNoTracking: true, ct);
                var persons = trx?.numberOfPersons ?? 0;

                var byStation = new Dictionary<string, List<StationTicketLine>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (itemId, quantity, addOnNote) in addedLines)
                {
                    if (quantity <= 0 || !itemsById.TryGetValue(itemId, out var info)) continue;
                    var station = StationFor(info.ItemType);
                    if (station is null) continue;

                    if (!byStation.TryGetValue(station, out var lines))
                        byStation[station] = lines = new List<StationTicketLine>();

                    lines.Add(new StationTicketLine(quantity, info.Name, addOnNote));
                }

                if (byStation.Count == 0) return;

                var dispatched = 0;
                foreach (var (station, lines) in byStation)
                {
                    var stationPrinters = printers
                        .Where(p => string.Equals(p.Station, station, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (stationPrinters.Count == 0) continue;

                    var ticket = new StationTicketDto(
                        station, transactionId, DateTime.UtcNow, createdBy ?? "", tableNumber, guestName,
                        Comment: null, Lines: lines, Persons: persons, IsAddition: true);
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
                    "Dispatched {Count} ADDED-ITEMS ticket(s) for Trx {Trx} across {Stations} station(s).",
                    dispatched, transactionId, byStation.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Added-items print dispatch failed for Trx {Trx}; items were still added.", transactionId);
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
