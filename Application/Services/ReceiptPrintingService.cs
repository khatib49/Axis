using Application.DTOs;
using System.Text;

namespace Application.Services
{
    public interface IReceiptPrintingService
    {
        byte[] GenerateKitchenBarReceipt(KitchenBarOrderDto order, List<KitchenBarOrderDto>? additionalOrders = null);
        string GenerateReceiptText(KitchenBarOrderDto order, List<KitchenBarOrderDto>? additionalOrders = null);
    }

    public class ReceiptPrintingService : IReceiptPrintingService
    {
        // ESC/POS Commands
        private static class EscPos
        {
            public static readonly byte[] ESC = { 0x1B };
            public static readonly byte[] GS = { 0x1D };

            // Initialize printer
            public static readonly byte[] INIT = { 0x1B, 0x40 };

            // Text alignment
            public static readonly byte[] ALIGN_LEFT = { 0x1B, 0x61, 0x00 };
            public static readonly byte[] ALIGN_CENTER = { 0x1B, 0x61, 0x01 };
            public static readonly byte[] ALIGN_RIGHT = { 0x1B, 0x61, 0x02 };

            // Text size
            public static readonly byte[] TEXT_NORMAL = { 0x1B, 0x21, 0x00 };
            public static readonly byte[] TEXT_DOUBLE_HEIGHT = { 0x1B, 0x21, 0x10 };
            public static readonly byte[] TEXT_DOUBLE_WIDTH = { 0x1B, 0x21, 0x20 };
            public static readonly byte[] TEXT_DOUBLE_SIZE = { 0x1B, 0x21, 0x30 };

            // Text style
            public static readonly byte[] BOLD_ON = { 0x1B, 0x45, 0x01 };
            public static readonly byte[] BOLD_OFF = { 0x1B, 0x45, 0x00 };
            public static readonly byte[] UNDERLINE_ON = { 0x1B, 0x2D, 0x01 };
            public static readonly byte[] UNDERLINE_OFF = { 0x1B, 0x2D, 0x00 };

            // Line feed
            public static readonly byte[] LF = { 0x0A };
            public static readonly byte[] CRLF = { 0x0D, 0x0A };

            // Cut paper (partial cut)
            public static readonly byte[] CUT_PARTIAL = { 0x1B, 0x6D };
            public static readonly byte[] CUT_FULL = { 0x1D, 0x56, 0x00 };

            // Horizontal line
            public static string LINE_SINGLE = new string('-', 32);
            public static string LINE_DOUBLE = new string('=', 32);
        }

        public byte[] GenerateKitchenBarReceipt(
            KitchenBarOrderDto order,
            List<KitchenBarOrderDto>? additionalOrders = null)
        {
            var receipt = new List<byte>();

            // Initialize printer
            receipt.AddRange(EscPos.INIT);

            // Header - Station Name
            receipt.AddRange(EscPos.ALIGN_CENTER);
            receipt.AddRange(EscPos.TEXT_DOUBLE_SIZE);
            receipt.AddRange(EscPos.BOLD_ON);
            receipt.AddRange(Encoding.ASCII.GetBytes($"*** {order.Station.ToUpper()} ***"));
            receipt.AddRange(EscPos.LF);
            receipt.AddRange(EscPos.BOLD_OFF);
            receipt.AddRange(EscPos.TEXT_NORMAL);
            receipt.AddRange(EscPos.LF);

            // Date & Time
            receipt.AddRange(EscPos.ALIGN_LEFT);
            receipt.AddRange(Encoding.ASCII.GetBytes($"Date: {order.OrderedAt:dd/MM/yyyy}"));
            receipt.AddRange(EscPos.LF);
            receipt.AddRange(Encoding.ASCII.GetBytes($"Time: {order.OrderedAt:HH:mm:ss}"));
            receipt.AddRange(EscPos.LF);

            // Table & Guest Info
            if (!string.IsNullOrWhiteSpace(order.TableNumber))
            {
                receipt.AddRange(EscPos.BOLD_ON);
                receipt.AddRange(Encoding.ASCII.GetBytes($"Table: {order.TableNumber}"));
                receipt.AddRange(EscPos.BOLD_OFF);
                receipt.AddRange(EscPos.LF);
            }

            if (!string.IsNullOrWhiteSpace(order.GuestName))
            {
                receipt.AddRange(Encoding.ASCII.GetBytes($"Guest: {order.GuestName}"));
                receipt.AddRange(EscPos.LF);
            }

            receipt.AddRange(Encoding.ASCII.GetBytes($"By: {order.CreatedByUsername}"));
            receipt.AddRange(EscPos.LF);
            receipt.AddRange(EscPos.LF);

            // Separator line
            receipt.AddRange(Encoding.ASCII.GetBytes(EscPos.LINE_DOUBLE));
            receipt.AddRange(EscPos.LF);

            // Main order item
            receipt.AddRange(EscPos.TEXT_DOUBLE_HEIGHT);
            receipt.AddRange(EscPos.BOLD_ON);
            receipt.AddRange(Encoding.ASCII.GetBytes($"{order.Quantity}x {order.ItemName}"));
            receipt.AddRange(EscPos.LF);
            receipt.AddRange(EscPos.BOLD_OFF);
            receipt.AddRange(EscPos.TEXT_NORMAL);

            // Comment if exists
            if (!string.IsNullOrWhiteSpace(order.ItemComment))
            {
                receipt.AddRange(EscPos.LF);
                receipt.AddRange(Encoding.ASCII.GetBytes($"NOTE: {order.ItemComment}"));
                receipt.AddRange(EscPos.LF);
            }

            // Additional orders from same transaction
            if (additionalOrders != null && additionalOrders.Any())
            {
                receipt.AddRange(EscPos.LF);
                receipt.AddRange(Encoding.ASCII.GetBytes(EscPos.LINE_SINGLE));
                receipt.AddRange(EscPos.LF);
                receipt.AddRange(Encoding.ASCII.GetBytes("OTHER ITEMS:"));
                receipt.AddRange(EscPos.LF);

                foreach (var item in additionalOrders)
                {
                    receipt.AddRange(Encoding.ASCII.GetBytes($"{item.Quantity}x {item.ItemName}"));
                    receipt.AddRange(EscPos.LF);

                    if (!string.IsNullOrWhiteSpace(item.ItemComment))
                    {
                        receipt.AddRange(Encoding.ASCII.GetBytes($"   NOTE: {item.ItemComment}"));
                        receipt.AddRange(EscPos.LF);
                    }
                }
            }

            receipt.AddRange(EscPos.LF);
            receipt.AddRange(Encoding.ASCII.GetBytes(EscPos.LINE_DOUBLE));
            receipt.AddRange(EscPos.LF);

            // Order ID
            receipt.AddRange(EscPos.ALIGN_CENTER);
            receipt.AddRange(Encoding.ASCII.GetBytes($"Order #{order.Id}"));
            receipt.AddRange(EscPos.LF);
            receipt.AddRange(Encoding.ASCII.GetBytes($"Transaction #{order.TransactionId}"));
            receipt.AddRange(EscPos.LF);
            receipt.AddRange(EscPos.LF);

            // Footer spacing
            receipt.AddRange(EscPos.LF);
            receipt.AddRange(EscPos.LF);
            receipt.AddRange(EscPos.LF);

            // Cut paper
            receipt.AddRange(EscPos.CUT_PARTIAL);

            return receipt.ToArray();
        }

        public string GenerateReceiptText(
            KitchenBarOrderDto order,
            List<KitchenBarOrderDto>? additionalOrders = null)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine($"================================");
            sb.AppendLine($"       *** {order.Station.ToUpper()} ***       ");
            sb.AppendLine($"================================");
            sb.AppendLine();

            // Date & Time
            sb.AppendLine($"Date: {order.OrderedAt:dd/MM/yyyy}");
            sb.AppendLine($"Time: {order.OrderedAt:HH:mm:ss}");

            // Table & Guest
            if (!string.IsNullOrWhiteSpace(order.TableNumber))
                sb.AppendLine($"Table: {order.TableNumber}");

            if (!string.IsNullOrWhiteSpace(order.GuestName))
                sb.AppendLine($"Guest: {order.GuestName}");

            sb.AppendLine($"By: {order.CreatedByUsername}");
            sb.AppendLine();
            sb.AppendLine($"================================");

            // Main order
            sb.AppendLine($"{order.Quantity}x {order.ItemName}");

            if (!string.IsNullOrWhiteSpace(order.ItemComment))
            {
                sb.AppendLine($"NOTE: {order.ItemComment}");
            }

            // Additional orders
            if (additionalOrders != null && additionalOrders.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"--------------------------------");
                sb.AppendLine("OTHER ITEMS:");

                foreach (var item in additionalOrders)
                {
                    sb.AppendLine($"{item.Quantity}x {item.ItemName}");
                    if (!string.IsNullOrWhiteSpace(item.ItemComment))
                    {
                        sb.AppendLine($"   NOTE: {item.ItemComment}");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine($"================================");
            sb.AppendLine($"Order #{order.Id}");
            sb.AppendLine($"Transaction #{order.TransactionId}");
            sb.AppendLine();

            return sb.ToString();
        }
    }
}