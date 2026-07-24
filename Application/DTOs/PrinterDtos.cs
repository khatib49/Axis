using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    // ---- CRUD DTOs ----
    public record PrinterDto(
        int Id,
        string Name,
        string Station,
        string ConnectionType,
        string Address,
        int CopyCount,
        bool IsEnabled,
        DateTime CreatedOn,
        DateTime? ModifiedOn);

    public record PrinterCreateDto(
        string Name,
        string Station,
        string ConnectionType,
        string Address,
        int CopyCount = 1);

    public record PrinterUpdateDto(
        string? Name,
        string? Station,
        string? ConnectionType,
        string? Address,
        int? CopyCount,
        bool? IsEnabled);

    public record PrinterListFilterDto(
        string? Station = null,
        bool IncludeDisabled = true);

    // ---- SignalR job pushed to the on-site print agent ----
    // PayloadBase64 is the raw ESC/POS byte stream, base64-encoded for JSON transport.
    public record PrintJobDto(
        int PrinterId,
        string PrinterName,
        string Station,
        string ConnectionType,
        string Address,
        int TransactionId,
        int Copies,
        string PayloadBase64);

    // ---- Input for ESC/POS station-ticket generation ----
    public record StationTicketLine(int Quantity, string ItemName, string? Comment);

    public record StationTicketDto(
        string Station,
        int TransactionId,
        DateTime OrderedAt,
        string CreatedByUsername,
        string? TableNumber,
        string? GuestName,
        string? Comment,
        List<StationTicketLine> Lines);
}
