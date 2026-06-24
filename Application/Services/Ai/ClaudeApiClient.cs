using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.Services.Ai
{
    /// <summary>
    /// Thin wrapper around Anthropic's /v1/messages API. Supports tool use
    /// (function calling). Stays provider-agnostic at the boundary so
    /// switching to OpenAI/etc. is a single class swap.
    /// </summary>
    public class ClaudeApiClient
    {
        private readonly IHttpClientFactory _httpFactory;

        public ClaudeApiClient(IHttpClientFactory httpFactory)
        {
            _httpFactory = httpFactory;
        }

        public async Task<ClaudeResponse> CreateMessageAsync(
            string apiKey,
            string model,
            string systemPrompt,
            IEnumerable<ClaudeMessage> messages,
            IEnumerable<ClaudeTool>? tools,
            int maxTokens = 2048,
            CancellationToken ct = default)
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(120);
            http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var body = new
            {
                model,
                max_tokens = maxTokens,
                system = systemPrompt,
                messages = messages.ToArray(),
                tools = tools?.ToArray()
            };

            var resp = await http.PostAsJsonAsync(
                "https://api.anthropic.com/v1/messages",
                body,
                _jsonOptions,
                ct);

            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Anthropic API error {(int)resp.StatusCode}: {raw}");

            return JsonSerializer.Deserialize<ClaudeResponse>(raw, _jsonOptions)
                ?? throw new InvalidOperationException("Empty response from Anthropic API");
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }

    // ── Wire types (match Anthropic API exactly) ──────────────────────────
    public class ClaudeMessage
    {
        [JsonPropertyName("role")]    public string Role { get; set; } = default!;       // 'user' | 'assistant'
        [JsonPropertyName("content")] public JsonElement Content { get; set; }            // string or array
    }

    public class ClaudeTool
    {
        [JsonPropertyName("name")]         public string Name { get; set; } = default!;
        [JsonPropertyName("description")]  public string Description { get; set; } = default!;
        [JsonPropertyName("input_schema")] public JsonElement InputSchema { get; set; }
    }

    public class ClaudeResponse
    {
        [JsonPropertyName("id")]         public string? Id { get; set; }
        [JsonPropertyName("model")]      public string? Model { get; set; }
        [JsonPropertyName("role")]       public string? Role { get; set; }
        [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
        [JsonPropertyName("content")]    public List<ClaudeContentBlock> Content { get; set; } = new();
        [JsonPropertyName("usage")]      public ClaudeUsage? Usage { get; set; }
    }

    public class ClaudeContentBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = default!; // 'text' | 'tool_use'
        [JsonPropertyName("text")] public string? Text { get; set; }

        // tool_use
        [JsonPropertyName("id")]    public string? Id { get; set; }
        [JsonPropertyName("name")]  public string? Name { get; set; }
        [JsonPropertyName("input")] public JsonElement? Input { get; set; }
    }

    public class ClaudeUsage
    {
        [JsonPropertyName("input_tokens")]  public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    }
}
