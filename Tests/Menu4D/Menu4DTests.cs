using System.Net;
using System.Text;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Application.Services.Ai;
using Application.Services;
using Application.Services.Menu4D;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Tests.OrderingChannels.OrderingChannelsTestSupport;

namespace Tests.Menu4D
{
    internal sealed class FakeTo3D : IPhotoTo3DClient
    {
        public string Provider => "Fake";
        public List<string> Created { get; } = new();
        public Dictionary<string, PhotoTo3DTask> Tasks { get; } = new();
        public byte[] Glb { get; set; } = Encoding.ASCII.GetBytes("glTF....fake");
        public bool FailCreate { get; set; }
        public Task<string> CreateAsync(string apiKey, string baseUrl, string imageDataUri, CancellationToken ct = default)
        {
            if (FailCreate) throw new InvalidOperationException("Meshy refused the job (402): no credits");
            var id = $"task-{Created.Count + 1}";
            Created.Add(imageDataUri);
            Tasks[id] = new PhotoTo3DTask("IN_PROGRESS", 10, null, null, null);
            return Task.FromResult(id);
        }
        public Task<PhotoTo3DTask> GetAsync(string apiKey, string baseUrl, string taskId, CancellationToken ct = default) => Task.FromResult(Tasks[taskId]);
        public Task<byte[]> DownloadAsync(string url, CancellationToken ct = default) => Task.FromResult(Glb);
    }

    internal sealed class FakeFiles : IMediaFileReader
    {
        public Task<(byte[] Bytes, string MediaType)?> ReadAsync(string pathOrUrl, CancellationToken ct = default) =>
            Task.FromResult<(byte[] Bytes, string MediaType)?>(pathOrUrl.Contains("missing") ? null : (new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3 }, "image/jpeg"));
    }

    internal sealed class FakeMedia : IMediaStorageService
    {
        public List<string> Saved { get; } = new();
        public List<byte[]> SavedBytes { get; } = new();
        public List<string?> Deleted { get; } = new();
        public Task<string> SaveAsync(IFormFile file, string subfolder, MediaKind kind, CancellationToken ct = default)
        {
            var p = $"media/{subfolder}/{Saved.Count + 1}{Path.GetExtension(file.FileName)}"; Saved.Add(p);
            using var ms = new MemoryStream(); file.CopyTo(ms); SavedBytes.Add(ms.ToArray());
            return Task.FromResult(p);
        }
        public Task DeleteAsync(string? relativePath) { Deleted.Add(relativePath); return Task.CompletedTask; }
    }

    /// <summary>Returns a canned Anthropic /v1/messages answer.</summary>
    internal sealed class ClaudeHandler : HttpMessageHandler
    {
        public string AnswerText { get; set; } = "";
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var payload = System.Text.Json.JsonSerializer.Serialize(new { id = "m1", type = "message", role = "assistant", content = new[] { new { type = "text", text = AnswerText } }, stop_reason = "end_turn" });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }

    public class Menu4DTests : IDisposable
    {
        private readonly ApplicationDbContext _db = NewDb();
        private readonly Dictionary<string, string?> _settings = new() { ["Anthropic.ApiKey"] = "sk-test", ["Menu4D.Meshy.ApiKey"] = "msy-test" };
        private readonly FakeTo3D _to3d = new();
        private readonly FakeMedia _media = new();
        private readonly ClaudeHandler _claudeHandler = new();
        private readonly Menu4DService _svc;

        public Menu4DTests()
        {
            var settings = new Mock<IIntegrationSettingsService>();
            settings.Setup(s => s.GetRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string k, CancellationToken _) => _settings.TryGetValue(k, out var v) ? v : null);
            var http = new Mock<IHttpClientFactory>();
            http.Setup(h => h.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_claudeHandler, disposeHandler: false));
            _svc = new Menu4DService(_db, settings.Object, new ClaudeApiClient(http.Object), _to3d, new FakeFiles(), _media, User(name: "admin@axislb.com").Object, NullLogger<Menu4DService>.Instance);

            _db.Add(new Category { Id = 1, Name = "Burgers", Type = "item", ItemType = "Food" });
            _db.Add(new Item { Id = 1, Name = "Classic Burger", Type = "Beef patty, cheddar, pickles, brioche bun", CategoryId = 1, StatusId = 1, ImagePath = "images/items/burger.jpg", Price = 10 });
            _db.Add(new Item { Id = 2, Name = "Iced Latte", Type = "Espresso, milk, ice", CategoryId = 1, StatusId = 1, ImagePath = "images/items/latte.jpg", Price = 5 });
            _db.Add(new Item { Id = 3, Name = "No Photo", Type = "x", CategoryId = 1, StatusId = 1, ImagePath = null, Price = 1 });
            _db.Add(new Item { Id = 4, Name = "Broken Photo", Type = "x", CategoryId = 1, StatusId = 1, ImagePath = "images/items/missing.jpg", Price = 1 });
            _db.SaveChanges(); _db.ChangeTracker.Clear();
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Analyze_labels_the_photo_and_keeps_points_on_the_picture()
        {
            _claudeHandler.AnswerText = "Here you go:\n{\"parts\":[{\"name\":\"Brioche bun\",\"detail\":\"Soft toasted brioche.\",\"x\":48,\"y\":20},{\"name\":\"Beef patty\",\"detail\":\"Juicy beef patty.\",\"x\":\"52\",\"y\":140},{\"name\":\"\",\"x\":1,\"y\":1}]}";
            var r = await _svc.AnalyzeAsync(1);
            r.Success.Should().BeTrue(r.Message);
            r.Data!.Parts.Should().HaveCount(2);
            r.Data.Parts[0].Name.Should().Be("Brioche bun");
            r.Data.Parts[1].Y.Should().Be(100, "positions are clamped to the photo");
            r.Data.PartsPublished.Should().BeFalse("staff publish after checking");
            _claudeHandler.LastBody.Should().Contain("Beef patty, cheddar").And.Contain("\"type\":\"image\"");
        }

        [Fact]
        public async Task Analyze_needs_an_ai_key_and_a_readable_photo()
        {
            _settings["Anthropic.ApiKey"] = null;
            (await _svc.AnalyzeAsync(1)).Error.Should().Be("NotConfigured");
            _settings["Anthropic.ApiKey"] = "sk";
            (await _svc.AnalyzeAsync(3)).Error.Should().Be("Validation");
            (await _svc.AnalyzeAsync(4)).Error.Should().Be("Validation");
            _claudeHandler.AnswerText = "sorry, I cannot";
            (await _svc.AnalyzeAsync(1)).Error.Should().Be("AiFailed");
        }

        [Fact]
        public async Task Update_cleans_parts_and_publishing_shows_them_publicly()
        {
            var parts = new List<Dish4DPart>
            {
                new("Cheddar", "Melted cheddar", 50, 45, "0.01m 0.05m 0.0m", "0m 1m 0m"),
                new("Pickles", null, -20, 300, "nonsense", null),
            };
            (await _svc.UpdateAsync(1, new Item4DUpdateDto(parts, false, 14))).Success.Should().BeTrue();
            (await _svc.ListPublicAsync()).Single().Parts.Should().BeEmpty("not published yet, only the real size is public");

            var r = await _svc.UpdateAsync(1, new Item4DUpdateDto(parts, true, 14));
            r.Data!.Parts[1].X.Should().Be(0); r.Data.Parts[1].Y.Should().Be(100);
            r.Data.Parts[1].Pos3D.Should().BeNull("invalid 3D positions are dropped");
            r.Data.Parts[0].Pos3D.Should().Be("0.01m 0.05m 0.0m");
            var pub = (await _svc.ListPublicAsync()).Single();
            pub.Parts.Should().HaveCount(2); pub.RealSizeCm.Should().Be(14);

            (await _svc.UpdateAsync(1, new Item4DUpdateDto(parts, true, 500))).Error.Should().Be("Validation");
        }

        [Fact]
        public async Task Publish_all_publishes_only_items_with_parts()
        {
            await _svc.UpdateAsync(1, new Item4DUpdateDto(new() { new("Bun", null, 10, 10) }, false, null));
            await _svc.UpdateAsync(2, new Item4DUpdateDto(new(), false, null));
            (await _svc.PublishAllAsync()).Queued.Should().Be(1);
            (await _svc.StatusAsync()).Published.Should().Be(1);
        }

        [Fact]
        public async Task Photo_to_3D_runs_queue_then_saves_the_model_switched_off()
        {
            (await _svc.QueueModelAsync(1)).Success.Should().BeTrue();
            (await _svc.StatusAsync()).Queued.Should().Be(1);

            await _svc.ProcessModelQueueAsync();
            _to3d.Created.Should().ContainSingle().Which.Should().StartWith("data:image/jpeg;base64,");
            (await _svc.StatusAsync()).Running.Should().Be(1);

            _to3d.Tasks["task-1"] = new PhotoTo3DTask("SUCCEEDED", 100, "https://cdn/x.glb", null, null);
            await _svc.ProcessModelQueueAsync();
            var row = (await _svc.ListAsync()).Single(r => r.ItemId == 1);
            row.GenStatus.Should().Be(Item4DGenStatus.Done);
            row.HasModel.Should().BeTrue(); row.ModelActive.Should().BeFalse("staff approve AI models first"); row.ModelIsAi.Should().BeTrue();
            _media.Saved.Should().ContainSingle(p => p.EndsWith(".glb"));
        }

        [Fact]
        public async Task Photo_to_3D_saves_the_model_at_the_dishs_real_width_and_drops_the_service_usdz()
        {
            await _svc.UpdateAsync(1, new Item4DUpdateDto(new(), false, 12m));
            await _svc.QueueModelAsync(1);
            await _svc.ProcessModelQueueAsync();
            _to3d.Glb = GlbFixture.Box(2.0, 1.0, 0.5); // a 2 m wide model, like many AI services return
            _to3d.Tasks["task-1"] = new PhotoTo3DTask("SUCCEEDED", 100, "https://cdn/x.glb", "https://cdn/x.usdz", null);
            await _svc.ProcessModelQueueAsync();
            _media.Saved.Should().ContainSingle().Which.Should().EndWith(".glb");
            GlbRealSize.Width(_media.SavedBytes[0])!.Value.Should().BeApproximately(0.12, 0.001);
        }

        [Fact]
        public async Task Photo_to_3D_reports_failures_and_bad_files()
        {
            await _svc.QueueModelAsync(1);
            await _svc.QueueModelAsync(2);
            await _svc.ProcessModelQueueAsync();
            _to3d.Tasks["task-1"] = new PhotoTo3DTask("FAILED", 0, null, null, "Image too dark");
            _to3d.Tasks["task-2"] = new PhotoTo3DTask("SUCCEEDED", 100, "https://cdn/x.glb", null, null);
            _to3d.Glb = Encoding.ASCII.GetBytes("<html>not a model</html>");
            await _svc.ProcessModelQueueAsync();
            var rows = await _svc.ListAsync();
            rows.Single(r => r.ItemId == 1).GenError.Should().Be("Image too dark");
            rows.Single(r => r.ItemId == 2).GenStatus.Should().Be(Item4DGenStatus.Failed);
            rows.Single(r => r.ItemId == 2).HasModel.Should().BeFalse();

            _to3d.FailCreate = true;
            await _svc.QueueModelAsync(1);
            await _svc.ProcessModelQueueAsync();
            (await _svc.ListAsync()).Single(r => r.ItemId == 1).GenError.Should().Contain("no credits");
        }

        [Fact]
        public async Task List_includes_dishes_that_only_have_an_uploaded_scan()
        {
            (await _svc.ListAsync()).Should().NotContain(r => r.ItemId == 3, "no photo and no scan");
            _db.Add(new ItemModel3D { ItemId = 3, GlbPath = "media/models/scan.glb", GlbBytes = 10, UpdatedBy = "staff" });
            _db.SaveChanges();
            var row = (await _svc.ListAsync()).Single(r => r.ItemId == 3);
            row.HasModel.Should().BeTrue(); row.ModelIsAi.Should().BeFalse(); row.ImagePath.Should().BeNull();
            (await _svc.StatusAsync()).WithModel.Should().Be(1, "scan-only dishes count as 3D models");
        }

        [Fact]
        public async Task Queue_all_skips_items_with_models_and_respects_the_parallel_limit()
        {
            _db.Add(new ItemModel3D { ItemId = 2, GlbPath = "media/models/scan.glb", GlbBytes = 10, UpdatedBy = "staff" });
            _db.SaveChanges();
            var r = await _svc.QueueAllModelsAsync();
            r.Queued.Should().Be(2, "items 1 and 4 have photos and no model; 2 already has a scan; 3 has no photo");
            _settings["Menu4D.MaxParallel"] = "1";
            await _svc.ProcessModelQueueAsync();
            var s = await _svc.StatusAsync();
            (s.Running + s.Failed).Should().BeGreaterThan(0);
            s.Running.Should().BeLessThanOrEqualTo(1);

            _settings["Menu4D.Meshy.ApiKey"] = null;
            (await _svc.QueueModelAsync(1)).Error.Should().Be("NotConfigured");
        }

        [Fact]
        public void Clean_keeps_at_most_eight_parts_and_trims_text()
        {
            var many = Enumerable.Range(1, 12).Select(i => new Dish4DPart(new string('x', 60) + i, new string('d', 300), i, i)).ToList();
            var c = Menu4DService.Clean(many);
            c.Should().HaveCount(8);
            c[0].Name.Length.Should().Be(40);
            c[0].Detail!.Length.Should().Be(160);
            Menu4DService.ParseParts("not json").Should().BeEmpty();
        }
    }
}

namespace Tests.Menu4D
{
    public class MeshyClientTests
    {
        private sealed class Handler : HttpMessageHandler
        {
            public List<(string Method, string Url, string? Auth, string? Body, long? Length)> Seen { get; } = new();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                var body = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
                Seen.Add((r.Method.Method, r.RequestUri!.ToString(), r.Headers.Authorization?.ToString(), body, r.Content?.Headers.ContentLength));
                var json = r.Method == HttpMethod.Post
                    ? "{\"result\":\"0193-abc\"}"
                    : "{\"id\":\"0193-abc\",\"status\":\"SUCCEEDED\",\"progress\":100,\"model_urls\":{\"glb\":\"https://assets.meshy.ai/x.glb\",\"usdz\":\"https://assets.meshy.ai/x.usdz\",\"fbx\":\"f\"},\"task_error\":{\"message\":\"\"}}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
        }

        [Fact]
        public async Task Creates_and_reads_an_image_to_3d_task()
        {
            var h = new Handler();
            var f = new Mock<IHttpClientFactory>();
            f.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(h, false));
            var c = new MeshyClient(f.Object);

            var id = await c.CreateAsync("msy-key", "https://api.meshy.ai", "data:image/jpeg;base64,AAAA");
            id.Should().Be("0193-abc");
            h.Seen[0].Url.Should().Be("https://api.meshy.ai/openapi/v1/image-to-3d");
            h.Seen[0].Auth.Should().Be("Bearer msy-key");
            h.Seen[0].Body.Should().Contain("\"image_url\":\"data:image/jpeg;base64,AAAA\"").And.Contain("\"should_texture\":true");
            h.Seen[0].Length.Should().NotBeNull("sent with a fixed length, not chunked");

            var t = await c.GetAsync("msy-key", "https://api.meshy.ai/", id);
            h.Seen[1].Url.Should().Be("https://api.meshy.ai/openapi/v1/image-to-3d/0193-abc");
            t.Status.Should().Be("SUCCEEDED"); t.Progress.Should().Be(100);
            t.GlbUrl.Should().EndWith(".glb"); t.UsdzUrl.Should().EndWith(".usdz"); t.Error.Should().BeNull();
        }
    }
}

namespace Tests.Menu4D
{
    using System.Text.Json;

    /// <summary>Tiny valid GLB files for the size tests.</summary>
    internal static class GlbFixture
    {
        /// <summary>One box-shaped primitive (bounds only) of the given size in metres, under a node with scale 1.</summary>
        public static byte[] Box(double x, double y, double z, object? node = null)
        {
            var bin = new byte[36];
            var json = JsonSerializer.Serialize(new
            {
                asset = new { version = "2.0" },
                scene = 0,
                scenes = new[] { new { nodes = new[] { 0 } } },
                nodes = new[] { node ?? new { mesh = 0 } },
                meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 } } } } },
                accessors = new[] { new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3", min = new[] { -x / 2, 0, -z / 2 }, max = new[] { x / 2, y, z / 2 } } },
                bufferViews = new[] { new { buffer = 0, byteLength = 36 } },
                buffers = new[] { new { byteLength = 36 } },
            });
            var j = Encoding.UTF8.GetBytes(json);
            var pad = (4 - j.Length % 4) % 4;
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("glTF")); w.Write(2u); w.Write((uint)(12 + 8 + j.Length + pad + 8 + bin.Length));
            w.Write((uint)(j.Length + pad)); w.Write(0x4E4F534Au); w.Write(j); for (var i = 0; i < pad; i++) w.Write((byte)0x20);
            w.Write((uint)bin.Length); w.Write(0x004E4942u); w.Write(bin);
            return ms.ToArray();
        }
    }

    public class GlbRealSizeTests
    {
        [Fact]
        public void Fits_the_widest_side_and_keeps_the_binary_data()
        {
            var glb = GlbFixture.Box(0.8, 0.3, 0.4);
            GlbRealSize.Width(glb)!.Value.Should().BeApproximately(0.8, 1e-6);
            var fitted = GlbRealSize.FitWidth(glb, 0.24);
            GlbRealSize.Width(fitted)!.Value.Should().BeApproximately(0.24, 1e-6);
            Encoding.ASCII.GetString(fitted, 0, 4).Should().Be("glTF");
            BitConverter.ToUInt32(fitted, 8).Should().Be((uint)fitted.Length);
            (fitted.Length % 4).Should().Be(0);
        }

        [Fact]
        public void Uses_node_transforms_and_the_deeper_side()
        {
            // scale 3 on the node, and Z is the widest side
            var glb = GlbFixture.Box(0.1, 0.1, 0.2, new { mesh = 0, scale = new[] { 3.0, 3.0, 3.0 }, translation = new[] { 5.0, 0, 0 } });
            GlbRealSize.Width(glb)!.Value.Should().BeApproximately(0.6, 1e-5);
            GlbRealSize.Width(GlbRealSize.FitWidth(glb, 0.3))!.Value.Should().BeApproximately(0.3, 1e-5);
        }

        [Fact]
        public void Leaves_files_it_cannot_read_unchanged()
        {
            var junk = Encoding.ASCII.GetBytes("glTF....fake");
            GlbRealSize.FitWidth(junk, 0.2).Should().BeSameAs(junk);
            GlbRealSize.Width(junk).Should().BeNull();
        }

        [Fact]
        public void Default_widths_follow_the_kind_of_dish()
        {
            Menu4DService.DefaultWidthCm("Iced Latte Iced Coffee").Should().Be(9);
            Menu4DService.DefaultWidthCm("Veggie Pizza Pizzas").Should().Be(30);
            Menu4DService.DefaultWidthCm("Shrimp Ramen Ramen").Should().Be(22);
        }
    }
}
