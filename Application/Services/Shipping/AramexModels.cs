using System.Globalization;
using System.Text.RegularExpressions;

namespace Application.Services.Shipping
{
    /// <summary>Credentials + fixed codes for one Aramex environment.</summary>
    public sealed record AramexCreds(
        string Environment, string BaseUrl,
        string UserName, string Password, string AccountNumber, string AccountPin, string AccountEntity,
        string AccountCountryCode, int Source)
    {
        public object ToClientInfo() => new
        {
            UserName, Password, Version = "v1.0",
            AccountNumber, AccountPin, AccountEntity, AccountCountryCode, Source,
        };
    }

    /// <summary>A party (shipper / consignee) as Aramex wants it.</summary>
    public sealed record AramexParty(
        string? AccountNumber,
        string Line1, string? Line2, string City, string CountryCode,
        string PersonName, string CompanyName, string Phone, string Cell, string Email,
        string? Reference = null)
    {
        public object AddressJson() => new
        {
            Line1, Line2 = Line2 ?? "", Line3 = "", City, StateOrProvinceCode = "", PostCode = "", CountryCode,
            Longitude = 0, Latitude = 0,
        };

        public object ContactJson() => new
        {
            Department = "", PersonName, Title = "", CompanyName,
            PhoneNumber1 = Phone, PhoneNumber1Ext = "", PhoneNumber2 = "", PhoneNumber2Ext = "", FaxNumber = "",
            CellPhone = string.IsNullOrWhiteSpace(Cell) ? Phone : Cell, EmailAddress = Email, Type = "",
        };

        public object ToJson() => new
        {
            Reference1 = Reference ?? "", Reference2 = "",
            AccountNumber = AccountNumber ?? "",
            PartyAddress = AddressJson(),
            Contact = ContactJson(),
        };
    }

    public sealed record AramexShipmentRequest(
        string Reference, string ForeignHawb,
        AramexParty Shipper, AramexParty Consignee,
        decimal WeightKg, int Pieces,
        string ProductGroup, string ProductType, string PaymentType,
        string DescriptionOfGoods, string GoodsOriginCountry,
        decimal? CodAmount, string CodCurrency,
        string? Comments, string? PickupGuid, int LabelReportId);

    public sealed record AramexNotification(string Code, string Message)
    {
        public override string ToString() => string.IsNullOrEmpty(Code) ? Message : $"{Code}: {Message}";
    }

    public sealed record AramexShipmentResult(
        bool Ok, string? AwbNumber, string? LabelUrl, byte[]? LabelBytes,
        List<AramexNotification> Notifications, string RawRequest, string RawResponse)
    {
        public string ErrorText => string.Join(" · ", Notifications.Select(n => n.ToString()));
    }

    public sealed record AramexRateResult(bool Ok, decimal Amount, string Currency, List<AramexNotification> Notifications, string RawResponse)
    {
        public string ErrorText => string.Join(" · ", Notifications.Select(n => n.ToString()));
    }

    public sealed record AramexTrackingUpdate(string Awb, string Code, string Description, DateTime At, string? Location, string? Comments, string? ProblemCode);

    public sealed record AramexTrackingResult(bool Ok, Dictionary<string, List<AramexTrackingUpdate>> ByAwb, List<AramexNotification> Notifications, string RawResponse)
    {
        public string ErrorText => string.Join(" · ", Notifications.Select(n => n.ToString()));
    }

    public sealed record AramexPickupRequest(
        AramexParty PickupParty, string PickupLocation, DateTime PickupDate, DateTime ReadyTime, DateTime LastPickupTime, DateTime ClosingTime,
        string Reference, string ProductGroup, string ProductType, string PaymentType,
        int NumberOfShipments, int NumberOfPieces, decimal TotalWeightKg, string? Comments);

    public sealed record AramexPickupResult(bool Ok, string? PickupId, string? PickupGuid, List<AramexNotification> Notifications, string RawRequest, string RawResponse)
    {
        public string ErrorText => string.Join(" · ", Notifications.Select(n => n.ToString()));
    }

    /// <summary>WCF JSON dates: "/Date(1481176272000)/" or "/Date(1481176272000+0300)/".</summary>
    public static class WcfDate
    {
        private static readonly Regex Rx = new(@"/Date\((-?\d+)([+-]\d{4})?\)/", RegexOptions.Compiled);

        public static string Format(DateTime utc) =>
            $"/Date({new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds()})/";

        public static DateTime? Parse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var m = Rx.Match(s);
            if (m.Success && long.TryParse(m.Groups[1].Value, out var ms))
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            // Some endpoints answer ISO-8601 instead.
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        }
    }

    /// <summary>Maps Aramex tracking updates to our Shipment.Status.</summary>
    public static class AramexStatusMap
    {
        public static string ToShipmentStatus(string? code, string? description, string current)
        {
            var c = (code ?? "").Trim().ToUpperInvariant();
            var d = (description ?? "").Trim().ToLowerInvariant();

            if (c == "SH014" || (d.Contains("delivered") && !d.Contains("not delivered") && !d.Contains("undelivered") && !d.Contains("attempt")))
                return "Delivered";
            if (c is "SH034" or "SH035" or "SH036" || d.Contains("returned to shipper") || d.Contains("return to shipper") || d.Contains("rts"))
                return "Returned";
            if (d.Contains("out for delivery") || d.Contains("with courier") || c is "SH007" or "SH008")
                return "OutForDelivery";
            if (c is "SH003" or "SH004" || d.Contains("picked up") || d.Contains("collected") || d.Contains("received at origin"))
                return current is "InTransit" or "OutForDelivery" ? current : "PickedUp";
            if (c is "SH005" or "SH006" or "SH009" or "SH010" || d.Contains("departed") || d.Contains("arrived") || d.Contains("in transit") || d.Contains("received at"))
                return "InTransit";
            if (c == "SH001" || d.Contains("record created") || d.Contains("shipment created"))
                return current == "Created" ? "Created" : current;
            // Unknown code — never regress a delivered/returned shipment.
            return current is "Delivered" or "Returned" ? current : (current == "Created" ? "InTransit" : current);
        }
    }
}
