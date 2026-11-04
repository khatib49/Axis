using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Application.Services.Ai;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase5
{
    public interface IAiToolsService
    {
        Task<ForecastDto> ForecastAsync(int days, CancellationToken ct = default);
        Task<BaseResponse<AiDraftDto>> ExtractAsync(string kind, IFormFile file, CancellationToken ct = default);
        Task<List<AiDraftDto>> ListDraftsAsync(string? kind, CancellationToken ct = default);
        Task<BaseResponse<AiDraftDto>> UpdateDraftAsync(int id, string json, CancellationToken ct = default);
        Task<bool> DiscardAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<AiApplyResultDto>> ApplyMenuAsync(int id, MenuApplyDto dto, CancellationToken ct = default);
        Task<BaseResponse<AiApplyResultDto>> ApplyInvoiceAsync(int id, InvoiceApplyDto dto, CancellationToken ct = default);
    }

    /// <summary>
    /// Smarter AI tools:
    /// - Forecast: next days' quantity per item from the same weekday of the last
    ///   8 weeks (recent weeks weigh more), turned into ingredient needs through
    ///   recipes and a suggested purchase against stock on hand.
    /// - Menu import: photo / PDF of a menu → categories, items, prices (Claude vision),
    ///   or a CSV (name,category,price[,kind]) without AI. Reviewed, then created.
    /// - Supplier invoice scan: photo / PDF → supplier, number, date, lines matched to
    ///   ingredients; reviewed, then booked through the existing purchase flow.
    /// </summary>
    public class AiToolsService : IAiToolsService
    {
        public const int PaidStatus = 6;
        public const int Weeks = 8;
        private const long MaxBytes = 10L * 1024 * 1024;
        private const string FallbackModel = "claude-sonnet-4-6";
        private static readonly string[] Kinds = { "Food", "Drinks", "Retail", "Tobacco" };

        private readonly ApplicationDbContext _db;
        private readonly IIntegrationSettingsService _settings;
        private readonly ClaudeApiClient _claude;
        private readonly IPurchaseService _purchases;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<AiToolsService> _log;

        public AiToolsService(ApplicationDbContext db, IIntegrationSettingsService settings, ClaudeApiClient claude, IPurchaseService purchases, ICurrentUserAccessor user, ILogger<AiToolsService> log)
        { _db = db; _settings = settings; _claude = claude; _purchases = purchases; _user = user; _log = log; }

        // ── Forecast ───────────────────────────────────────────────────
        public async Task<ForecastDto> ForecastAsync(int days, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 1, 14);
            var tz = VenueClock.Zone(await _settings.GetRawAsync("Venue.TimeZone", ct));
            var todayStart = VenueClock.TodayStartUtc(tz);
            var since = todayStart.AddDays(-7 * Weeks);
            var rows = await (from ti in _db.Set<TransactionItem>().AsNoTracking()
                              join t in _db.Set<TransactionRecord>().AsNoTracking() on ti.TransactionRecordId equals t.Id
                              where t.StatusId == PaidStatus && (t.PaidOn ?? t.CreatedOn) >= since && (t.PaidOn ?? t.CreatedOn) < todayStart
                              select new { ti.ItemId, ti.Quantity, On = t.PaidOn ?? t.CreatedOn }).ToListAsync(ct);
            var localToday = TimeZoneInfo.ConvertTimeFromUtc(todayStart, tz).Date;
            // qty[item][date]
            var byDay = rows.GroupBy(r => (r.ItemId, Date: TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.On, DateTimeKind.Utc), tz).Date))
                .ToDictionary(g => g.Key, g => (decimal)g.Sum(x => x.Quantity));
            var itemIds = rows.Select(r => r.ItemId).Distinct().ToList();
            var names = await _db.Set<Item>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
            var items = new List<ForecastItemDto>();
            foreach (var id in itemIds)
            {
                var list = new List<ForecastDayDto>();
                for (var d = 0; d < days; d++)
                {
                    var date = localToday.AddDays(d);
                    list.Add(new(date, Predict(w => byDay.GetValueOrDefault((id, date.AddDays(-7 * w))))));
                }
                var total = list.Sum(x => x.Quantity);
                if (total > 0) items.Add(new(id, names.GetValueOrDefault(id) ?? $"#{id}", total, list));
            }
            items = items.OrderByDescending(i => i.Total).ToList();

            var needs = new Dictionary<int, decimal>();
            var recipes = await _db.Set<RecipeLine>().AsNoTracking().Where(r => itemIds.Contains(r.ItemId)).ToListAsync(ct);
            foreach (var it in items)
                foreach (var r in recipes.Where(r => r.ItemId == it.ItemId))
                    needs[r.IngredientId] = needs.GetValueOrDefault(r.IngredientId) + r.Quantity * it.Total;
            var ingIds = needs.Keys.ToList();
            var ings = await _db.Set<Ingredient>().AsNoTracking().Where(i => ingIds.Contains(i.Id)).ToListAsync(ct);
            var ingredients = ings.Select(i =>
            {
                var need = Math.Round(needs[i.Id], 2);
                var suggest = Math.Max(0, Math.Round(need + (i.ReorderLevel ?? 0) - i.QuantityOnHand, 2));
                return new ForecastIngredientDto(i.Id, i.Name, i.Unit, need, i.QuantityOnHand, i.ReorderLevel, suggest);
            }).OrderByDescending(i => i.SuggestedPurchase).ThenBy(i => i.Name).ToList();
            return new(days, Weeks, items, ingredients);
        }

        /// <summary>Weighted average of the same weekday over the last 8 weeks (week 1 = last week, weight 8 … week 8, weight 1).</summary>
        public static decimal Predict(Func<int, decimal> qtyWeeksAgo)
        {
            decimal sum = 0, weights = 0;
            for (var w = 1; w <= Weeks; w++) { var weight = Weeks + 1 - w; sum += qtyWeeksAgo(w) * weight; weights += weight; }
            return Math.Round(sum / weights, 1);
        }

        // ── Extraction ─────────────────────────────────────────────────
        public async Task<BaseResponse<AiDraftDto>> ExtractAsync(string kind, IFormFile file, CancellationToken ct = default)
        {
            kind = kind.Equals("invoice", StringComparison.OrdinalIgnoreCase) ? "Invoice" : kind.Equals("menu", StringComparison.OrdinalIgnoreCase) ? "Menu" : "";
            if (kind == "") return Fail("Validation", "Kind must be menu or invoice.");
            if (file is null || file.Length == 0) return Fail("Validation", "Upload a file.");
            if (file.Length > MaxBytes) return Fail("Validation", "The file is larger than 10 MB.");
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            string json;
            if (kind == "Menu" && ext is ".csv" or ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                var parsed = ParseMenuCsv(await reader.ReadToEndAsync(ct));
                if (parsed is null) return Fail("Validation", "The CSV needs columns name, category, price (and optional kind: Food, Drinks, Retail, Tobacco).");
                json = parsed;
            }
            else
            {
                var media = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", ".pdf" => "application/pdf", _ => null };
                if (media is null) return Fail("Validation", "Upload a photo (jpg, png, webp) or a PDF" + (kind == "Menu" ? ", or a CSV." : "."));
                var apiKey = await _settings.GetRawAsync("Anthropic.ApiKey", ct);
                if (string.IsNullOrWhiteSpace(apiKey)) return Fail("NotConfigured", "Set Anthropic.ApiKey in Integrations to read photos and PDFs." + (kind == "Menu" ? " A CSV works without it." : ""));
                byte[] bytes;
                using (var ms = new MemoryStream()) { await file.CopyToAsync(ms, ct); bytes = ms.ToArray(); }
                try { json = await AskVisionAsync(apiKey, kind, media, bytes, ct); }
                catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
                {
                    _log.LogWarning(ex, "AI extraction failed"); return Fail("Provider", "The AI could not read this file: " + ex.Message);
                }
                if (kind == "Invoice") json = await MatchIngredientsAsync(json, ct);
            }
            var d = new AiImportDraft { Kind = kind, FileName = Path.GetFileName(file.FileName)[..Math.Min(200, Path.GetFileName(file.FileName).Length)], Json = json, CreatedBy = _user.UserName };
            _db.Add(d); await _db.SaveChangesAsync(ct);
            return new(true, null, "Draft ready: review it, then apply.", ToDto(d));
        }

        public static string? ParseMenuCsv(string text)
        {
            var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => SplitCsv(l)).Where(c => c.Count > 0).ToList();
            if (lines.Count == 0) return null;
            var header = lines[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
            int iName = header.IndexOf("name"), iCat = header.IndexOf("category"), iPrice = header.IndexOf("price"), iKind = header.IndexOf("kind");
            if (iName < 0 || iCat < 0 || iPrice < 0) return null;
            var cats = new Dictionary<string, (string kind, JsonArray items)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in lines.Skip(1))
            {
                string Get(int i) => i >= 0 && i < row.Count ? row[i].Trim() : "";
                var name = Get(iName); var cat = Get(iCat);
                if (name.Length == 0 || cat.Length == 0) continue;
                if (!decimal.TryParse(Get(iPrice).Replace("$", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var price)) continue;
                var kind = Kinds.FirstOrDefault(k => k.Equals(Get(iKind), StringComparison.OrdinalIgnoreCase)) ?? "Food";
                if (!cats.TryGetValue(cat, out var c)) { c = (kind, new JsonArray()); cats[cat] = c; }
                c.items.Add(new JsonObject { ["name"] = name, ["price"] = price });
            }
            if (cats.Count == 0) return null;
            var arr = new JsonArray();
            foreach (var (name, c) in cats) arr.Add(new JsonObject { ["name"] = name, ["kind"] = c.kind, ["items"] = c.items });
            return new JsonObject { ["currency"] = "USD", ["categories"] = arr }.ToJsonString();
        }

        private static List<string> SplitCsv(string line)
        {
            var res = new List<string>(); var cur = new System.Text.StringBuilder(); var q = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"') { if (q && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else q = !q; }
                else if ((ch == ',' || ch == ';') && !q) { res.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            res.Add(cur.ToString());
            return res;
        }

        private async Task<string> AskVisionAsync(string apiKey, string kind, string media, byte[] bytes, CancellationToken ct)
        {
            var model = await _settings.GetRawAsync("Anthropic.Model", ct) ?? FallbackModel;
            var system = kind == "Menu"
                ? "You read restaurant and cafe menus (English or Arabic). Return JSON only: {\"currency\":\"USD|LBP|...\",\"categories\":[{\"name\":\"...\",\"kind\":\"Food|Drinks|Retail|Tobacco\",\"items\":[{\"name\":\"...\",\"price\":0,\"description\":\"...\"}]}]}. Keep names as printed. Prices are numbers without currency symbols. Skip anything without a price."
                : "You read supplier invoices (English or Arabic). Return JSON only: {\"supplier\":\"...\",\"invoiceNumber\":\"...\",\"date\":\"yyyy-MM-dd\",\"currency\":\"USD|LBP|...\",\"lines\":[{\"name\":\"...\",\"quantity\":0,\"unit\":\"kg|g|l|ml|pcs|...\",\"unitCost\":0,\"total\":0}],\"total\":0}. Numbers only, no symbols. Do not invent lines.";
            var block = media == "application/pdf"
                ? (object)new { type = "document", source = new { type = "base64", media_type = media, data = Convert.ToBase64String(bytes) } }
                : new { type = "image", source = new { type = "base64", media_type = media, data = Convert.ToBase64String(bytes) } };
            var content = JsonSerializer.SerializeToElement(new object[] { block, new { type = "text", text = kind == "Menu" ? "Extract this menu." : "Extract this invoice." } });
            var resp = await _claude.CreateMessageAsync(apiKey, model, system, new[] { new ClaudeMessage { Role = "user", Content = content } }, null, 4096, ct);
            var text = string.Concat(resp.Content.Where(c => c.Type == "text").Select(c => c.Text ?? ""));
            var s = text.IndexOf('{'); var e = text.LastIndexOf('}');
            if (s < 0 || e <= s) throw new JsonException("The answer was not JSON.");
            var node = JsonNode.Parse(text[s..(e + 1)]) ?? throw new JsonException("Empty answer.");
            return node.ToJsonString();
        }

        private async Task<string> MatchIngredientsAsync(string json, CancellationToken ct)
        {
            var node = JsonNode.Parse(json)!.AsObject();
            var ings = await _db.Set<Ingredient>().AsNoTracking().Where(i => i.IsActive).Select(i => new { i.Id, i.Name, i.Unit }).ToListAsync(ct);
            if (node["lines"] is JsonArray lines)
                foreach (var l in lines.OfType<JsonObject>())
                {
                    var name = l["name"]?.GetValue<string>() ?? "";
                    var best = ings.Select(i => new { i, score = Similarity(name, i.Name) }).OrderByDescending(x => x.score).FirstOrDefault();
                    if (best is not null && best.score >= 0.5) { l["ingredientId"] = best.i.Id; l["ingredientName"] = best.i.Name; l["ingredientUnit"] = best.i.Unit; }
                }
            var supplier = node["supplier"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(supplier))
            {
                var sups = await _db.Set<Supplier>().AsNoTracking().Where(s => s.IsActive).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
                var best = sups.Select(s => new { s, score = Similarity(supplier, s.Name) }).OrderByDescending(x => x.score).FirstOrDefault();
                if (best is not null && best.score >= 0.6) { node["supplierId"] = best.s.Id; node["supplierName"] = best.s.Name; }
            }
            return node.ToJsonString();
        }

        /// <summary>Word-overlap similarity 0..1 (case and accent tolerant enough for item names).</summary>
        public static double Similarity(string a, string b)
        {
            static HashSet<string> W(string s) => s.ToLowerInvariant().Split(new[] { ' ', '-', '_', '/', ',', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).Select(w => w.TrimEnd('s')).ToHashSet();
            var x = W(a); var y = W(b);
            if (x.Count == 0 || y.Count == 0) return 0;
            if (a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase)) return 1;
            return (double)x.Intersect(y).Count() / Math.Min(x.Count, y.Count);
        }

        private static AiDraftDto ToDto(AiImportDraft d) => new(d.Id, d.Kind, d.FileName, d.Json, d.Status, d.Result, d.CreatedOn, d.CreatedBy);

        public async Task<List<AiDraftDto>> ListDraftsAsync(string? kind, CancellationToken ct = default)
        {
            var q = _db.Set<AiImportDraft>().AsNoTracking();
            if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(d => d.Kind == kind);
            return (await q.OrderByDescending(d => d.Id).Take(50).ToListAsync(ct)).Select(ToDto).ToList();
        }

        public async Task<BaseResponse<AiDraftDto>> UpdateDraftAsync(int id, string json, CancellationToken ct = default)
        {
            var d = await _db.Set<AiImportDraft>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null) return Fail("NotFound", "Draft not found.");
            if (d.Status != "Draft") return Fail("Done", $"This draft is {d.Status}.");
            try { JsonNode.Parse(json); } catch (JsonException) { return Fail("Validation", "Invalid JSON."); }
            d.Json = json; await _db.SaveChangesAsync(ct);
            return new(true, null, "Draft saved.", ToDto(d));
        }

        public async Task<bool> DiscardAsync(int id, CancellationToken ct = default)
        {
            var d = await _db.Set<AiImportDraft>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null || d.Status != "Draft") return false;
            d.Status = "Discarded"; await _db.SaveChangesAsync(ct); return true;
        }

        public async Task<BaseResponse<AiApplyResultDto>> ApplyMenuAsync(int id, MenuApplyDto dto, CancellationToken ct = default)
        {
            var d = await _db.Set<AiImportDraft>().FirstOrDefaultAsync(x => x.Id == id && x.Kind == "Menu", ct);
            if (d is null) return new(false, "NotFound", "Menu draft not found.");
            if (d.Status != "Draft") return new(false, "Done", $"This draft is {d.Status}.");
            var qty = Math.Clamp(dto.DefaultQuantity, 0, 100000);
            JsonObject root;
            try { root = JsonNode.Parse(d.Json)!.AsObject(); } catch (Exception) { return new(false, "Validation", "The draft JSON is invalid."); }
            int created = 0, skipped = 0;
            var cats = await _db.Set<Category>().Where(c => c.Type == "item").ToListAsync(ct);
            var existingItems = await _db.Set<Item>().AsNoTracking().Select(i => new { i.Name, i.CategoryId }).ToListAsync(ct);
            foreach (var c in (root["categories"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                var cname = (c["name"]?.GetValue<string>() ?? "").Trim();
                if (cname.Length == 0) continue;
                var kind = Kinds.FirstOrDefault(k => k.Equals(c["kind"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase)) ?? "Food";
                var cat = cats.FirstOrDefault(x => x.Name.Equals(cname, StringComparison.OrdinalIgnoreCase));
                if (cat is null) { cat = new Category { Name = cname[..Math.Min(100, cname.Length)], Type = "item", ItemType = kind, ShowInShop = kind == "Retail" }; _db.Add(cat); await _db.SaveChangesAsync(ct); cats.Add(cat); }
                foreach (var it in (c["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                {
                    var name = (it["name"]?.ToString() ?? "").Trim();
                    if (name.Length == 0 || !decimal.TryParse(it["price"]?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0) { skipped++; continue; }
                    if (existingItems.Any(e => e.CategoryId == cat.Id && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { skipped++; continue; }
                    _db.Add(new Item { Name = name[..Math.Min(200, name.Length)], Price = Math.Round(price, 2), Type = "CoffeeShop", CategoryId = cat.Id, StatusId = 1, Quantity = qty, SellOnline = kind == "Retail" });
                    existingItems.Add(new { Name = name, CategoryId = cat.Id });
                    created++;
                }
            }
            d.Status = "Applied"; d.AppliedOn = DateTime.UtcNow; d.Result = $"{created} items created, {skipped} skipped.";
            await _db.SaveChangesAsync(ct);
            return new(true, null, d.Result, new(d.Id, d.Result, created, skipped, null));
        }

        public async Task<BaseResponse<AiApplyResultDto>> ApplyInvoiceAsync(int id, InvoiceApplyDto dto, CancellationToken ct = default)
        {
            var d = await _db.Set<AiImportDraft>().FirstOrDefaultAsync(x => x.Id == id && x.Kind == "Invoice", ct);
            if (d is null) return new(false, "NotFound", "Invoice draft not found.");
            if (d.Status != "Draft") return new(false, "Done", $"This draft is {d.Status}.");
            var lines = (dto.Lines ?? new()).Where(l => l.Quantity > 0).ToList();
            if (lines.Count == 0) return new(false, "Validation", "Keep at least one line with a quantity.");
            if (lines.Any(l => l.UnitCost < 0)) return new(false, "Validation", "Costs cannot be negative.");
            var ingIds = lines.Select(l => l.IngredientId).Distinct().ToList();
            if (await _db.Set<Ingredient>().CountAsync(i => ingIds.Contains(i.Id), ct) != ingIds.Count) return new(false, "Validation", "Match every line to an ingredient.");
            var supplierId = dto.SupplierId;
            if (supplierId is null && !string.IsNullOrWhiteSpace(dto.NewSupplierName))
            {
                var s = new Supplier { Name = dto.NewSupplierName.Trim()[..Math.Min(150, dto.NewSupplierName.Trim().Length)], IsActive = true };
                _db.Add(s); await _db.SaveChangesAsync(ct); supplierId = s.Id;
            }
            var p = await _purchases.CreateAsync(new PurchaseCreateDto(supplierId, dto.PurchaseDate ?? DateTime.UtcNow, dto.InvoiceNumber, $"From scanned invoice {d.FileName}",
                lines.Select(l => new PurchaseLineInputDto(l.IngredientId, l.Quantity, l.UnitCost, l.Notes)).ToList()), _user.UserName, ct);
            d.Status = "Applied"; d.AppliedOn = DateTime.UtcNow; d.Result = $"Purchase #{p.Id} booked ({lines.Count} lines).";
            await _db.SaveChangesAsync(ct);
            return new(true, null, d.Result, new(d.Id, d.Result, lines.Count, 0, p.Id));
        }

        private static BaseResponse<AiDraftDto> Fail(string e, string m) => new(false, e, m);
    }
}
