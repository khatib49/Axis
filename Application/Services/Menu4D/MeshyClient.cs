using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Application.Services.Menu4D
{
    public record PhotoTo3DTask(string Status, int Progress, string? GlbUrl, string? UsdzUrl, string? Error);

    /// <summary>A service that turns one photo into a textured 3D model (GLB, plus USDZ for iPhone AR).</summary>
    public interface IPhotoTo3DClient
    {
        string Provider { get; }
        /// <summary>Starts a job; returns the provider's task id.</summary>
        Task<string> CreateAsync(string apiKey, string baseUrl, string imageDataUri, CancellationToken ct = default);
        Task<PhotoTo3DTask> GetAsync(string apiKey, string baseUrl, string taskId, CancellationToken ct = default);
        Task<byte[]> DownloadAsync(string url, CancellationToken ct = default);
    }

    /// <summary>
    /// Meshy image-to-3D (https://www.meshy.ai). POST /openapi/v1/image-to-3d returns
    /// {"result": "&lt;task id&gt;"}; GET /openapi/v1/image-to-3d/{id} returns status
    /// (PENDING, IN_PROGRESS, SUCCEEDED, FAILED, CANCELED), progress and model_urls.
    /// Bodies are sent with a fixed length (some gateways refuse chunked uploads).
    /// </summary>
    public class MeshyClient : IPhotoTo3DClient
    {
        public const string DefaultBaseUrl = "https://api.meshy.ai";
        private readonly IHttpClientFactory _http;
        public MeshyClient(IHttpClientFactory http) { _http = http; }
        public string Provider => "Meshy";

        private HttpClient Client(string apiKey, string baseUrl)
        {
            var c = _http.CreateClient();
            c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            c.Timeout = TimeSpan.FromSeconds(100);
            c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            return c;
        }

        public async Task<string> CreateAsync(string apiKey, string baseUrl, string imageDataUri, CancellationToken ct = default)
        {
            using var c = Client(apiKey, baseUrl);
            var body = JsonSerializer.Serialize(new
            {
                image_url = imageDataUri,
                ai_model = "latest",
                topology = "triangle",
                target_polycount = 30000,   // light enough for phones on mobile data
                should_remesh = true,
                should_texture = true,
                enable_pbr = true,
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await c.PostAsync("openapi/v1/image-to-3d", content, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Meshy refused the job ({(int)resp.StatusCode}): {Short(raw)}");
            var id = JsonNode.Parse(raw)?["result"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(id) ? throw new InvalidOperationException("Meshy did not return a task id.") : id;
        }

        public async Task<PhotoTo3DTask> GetAsync(string apiKey, string baseUrl, string taskId, CancellationToken ct = default)
        {
            using var c = Client(apiKey, baseUrl);
            var resp = await c.GetAsync($"openapi/v1/image-to-3d/{Uri.EscapeDataString(taskId)}", ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Meshy status check failed ({(int)resp.StatusCode}): {Short(raw)}");
            var n = JsonNode.Parse(raw) ?? throw new InvalidOperationException("Empty answer from Meshy.");
            var status = (n["status"]?.GetValue<string>() ?? "").ToUpperInvariant();
            var progress = n["progress"] is JsonValue pv && pv.TryGetValue<int>(out var p) ? p : 0;
            var urls = n["model_urls"];
            var err = n["task_error"]?["message"]?.GetValue<string>();
            return new PhotoTo3DTask(status, progress, urls?["glb"]?.GetValue<string>(), urls?["usdz"]?.GetValue<string>(), string.IsNullOrWhiteSpace(err) ? null : err);
        }

        public async Task<byte[]> DownloadAsync(string url, CancellationToken ct = default)
        {
            using var c = _http.CreateClient();
            c.Timeout = TimeSpan.FromMinutes(3);
            return await c.GetByteArrayAsync(url, ct);
        }

        private static string Short(string s) => s.Length > 300 ? s[..300] : s;
    }
}
