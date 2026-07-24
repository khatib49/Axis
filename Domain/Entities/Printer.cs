using System;

namespace Domain.Entities
{
    /// <summary>
    /// A physical receipt/ticket printer at the venue. The cloud API cannot reach
    /// these directly (they live on the venue LAN); instead the API dispatches
    /// print jobs over SignalR and an on-site print agent forwards the bytes to
    /// the printer described here.
    /// </summary>
    public class Printer
    {
        public int Id { get; set; }

        /// <summary>Friendly name, e.g. "Kitchen Printer".</summary>
        public string Name { get; set; } = default!;

        /// <summary>Which station's items route here: "Kitchen" or "Bar".</summary>
        public string Station { get; set; } = default!;

        /// <summary>How the on-site agent reaches the printer: "Network" or "Usb".</summary>
        public string ConnectionType { get; set; } = "Network";

        /// <summary>
        /// For "Network": "ip:port" (e.g. "192.168.1.50:9100").
        /// For "Usb": the Windows printer name (e.g. "EPSON TM-T20").
        /// </summary>
        public string Address { get; set; } = default!;

        /// <summary>How many copies of each ticket to print. Defaults to 1.</summary>
        public int CopyCount { get; set; } = 1;

        /// <summary>Disabled printers are skipped by the dispatcher and hidden from cashiers.</summary>
        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }
}
