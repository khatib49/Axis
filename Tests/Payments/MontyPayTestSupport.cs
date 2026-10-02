using Application.IServices;
using Application.Services.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Tests.Payments
{
    /// <summary>Settings + provider wiring shared by the MontyPay tests. Values are test-only.</summary>
    internal static class MontyPayTestSupport
    {
        public const string SandboxPassword = "sandbox-test-password";
        public const string ProductionPassword = "production-test-password";

        public static Dictionary<string, string?> Settings(string hashAlgorithm = "md5", bool withProduction = false)
        {
            var s = new Dictionary<string, string?>
            {
                ["MontyPay.Environment"] = "sandbox",
                ["MontyPay.Sandbox.CheckoutUrl"] = "https://checkout.example.test",
                ["MontyPay.Sandbox.MerchantKey"] = "sandbox-key-0000",
                ["MontyPay.Sandbox.Password"] = SandboxPassword,
                ["MontyPay.HashAlgorithm"] = hashAlgorithm,
                ["Payments.PublicBaseUrl"] = "https://www.example.test",
            };
            if (withProduction)
            {
                s["MontyPay.Production.CheckoutUrl"] = "https://checkout.example.test";
                s["MontyPay.Production.MerchantKey"] = "production-key-0000";
                s["MontyPay.Production.Password"] = ProductionPassword;
            }
            return s;
        }

        public static Mock<IIntegrationSettingsService> SettingsMock(Dictionary<string, string?> values)
        {
            var m = new Mock<IIntegrationSettingsService>();
            m.Setup(x => x.GetRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string k, CancellationToken _) => values.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) ? v : null);
            return m;
        }

        public static MontyPayProvider Provider(Dictionary<string, string?> values) =>
            new(SettingsMock(values).Object, new Mock<IHttpClientFactory>().Object, NullLogger<MontyPayProvider>.Instance);

        /// <summary>A callback exactly as MontyPay would sign it.</summary>
        public static Dictionary<string, string> SignedCallback(
            string orderNumber, string amount, string currency, string description,
            string type = "sale", string status = "success", string orderStatus = "settled",
            string? id = null, string password = SandboxPassword, bool sha256 = false)
        {
            id ??= Guid.NewGuid().ToString();
            var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = id,
                ["order_number"] = orderNumber,
                ["order_amount"] = amount,
                ["order_currency"] = currency,
                ["order_description"] = description,
                ["order_status"] = orderStatus,
                ["type"] = type,
                ["status"] = status,
                ["card"] = "411111****1111",
                ["date"] = "2026-10-02 10:00:00",
            };
            form["hash"] = MontyPayProvider.ComputeHash(id + orderNumber + amount + currency + description, password, sha256);
            return form;
        }

        public static string ToRaw(IReadOnlyDictionary<string, string> form) =>
            string.Join("&", form.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value).Replace("%20", "+")}"));
    }
}
