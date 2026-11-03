namespace AxisAPI.Controllers.Payments2
{
    /// <summary>
    /// Builds the API return endpoint Whish redirects the buyer to. The
    /// "{requestId}" placeholder is filled in by the payment service once the
    /// request row exists, so the return handler can look it up by id.
    /// </summary>
    public static class WhishReturnUrls
    {
        public const string Template = "{requestId}";

        public static (string success, string cancel) Build(HttpRequest request)
        {
            var fwdProto = request.Headers["X-Forwarded-Proto"].FirstOrDefault();
            var fwdHost = request.Headers["X-Forwarded-Host"].FirstOrDefault();
            var scheme = string.IsNullOrWhiteSpace(fwdProto) ? request.Scheme : fwdProto;
            var host = string.IsNullOrWhiteSpace(fwdHost) ? request.Host.Value : fwdHost;
            var baseUrl = $"{scheme}://{host}{request.PathBase}/api/payments/whish/return?requestId={Template}";
            return (baseUrl + "&result=success", baseUrl + "&result=cancel");
        }
    }
}
