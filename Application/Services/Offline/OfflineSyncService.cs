using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Offline
{
    /// <summary>
    /// Replays a till's offline queue against the EXISTING order services.
    /// Idempotency: a successful op is stored under its client opId and any
    /// later replay of that key returns the stored response without
    /// re-executing. A key reused with a different payload is a 409 conflict.
    /// Failures (400/500) are NOT stored so the client may retry them.
    /// </summary>
    public class OfflineSyncService : IOfflineSyncService
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IBaseRepository<IdempotencyRecord> _records;
        private readonly IBaseRepository<OfflineSyncBatch> _batches;
        private readonly IUnitOfWork _uow;
        private readonly ITransactionRecordService _transactions;
        private readonly ILogger<OfflineSyncService> _log;

        public OfflineSyncService(
            IBaseRepository<IdempotencyRecord> records,
            IBaseRepository<OfflineSyncBatch> batches,
            IUnitOfWork uow,
            ITransactionRecordService transactions,
            ILogger<OfflineSyncService> log)
        {
            _records = records;
            _batches = batches;
            _uow = uow;
            _transactions = transactions;
            _log = log;
        }

        public async Task<OfflineSyncResponse> SyncAsync(OfflineSyncRequest request, string createdBy, int? branchId, CancellationToken ct = default)
        {
            var ops = request.Ops ?? new List<OfflineOpRequest>();
            var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? "unknown" : request.ClientId.Trim();
            var results = new List<OfflineOpResult>(ops.Count);
            // opId -> transaction id created in THIS batch (for ops that reference an offline-created invoice).
            var createdInBatch = new Dictionary<Guid, int>();

            foreach (var op in ops)
            {
                OfflineOpResult result;
                try
                {
                    result = await ProcessOneAsync(op, createdBy, branchId, createdInBatch, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Offline op {OpId} ({Type}) failed unexpectedly", op.OpId, op.Type);
                    result = new OfflineOpResult(op.OpId, 500, OfflineOpOutcomes.Failed, null, ex.Message);
                }
                results.Add(result);
            }

            var batch = new OfflineSyncBatch
            {
                ClientId = clientId,
                ReceivedOn = DateTime.UtcNow,
                ReceivedBy = createdBy,
                BranchId = branchId,
                OpsCount = ops.Count,
                SucceededCount = results.Count(r => r.Outcome is OfflineOpOutcomes.Ok or OfflineOpOutcomes.Replayed),
                FailedCount = results.Count(r => r.Outcome is OfflineOpOutcomes.Failed or OfflineOpOutcomes.Conflict),
            };
            try
            {
                await _batches.AddAsync(batch, ct);
                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Audit must never cost the till its results.
                _log.LogError(ex, "Could not write offline sync batch audit row for client {ClientId}", clientId);
            }

            return new OfflineSyncResponse(clientId, batch.Id, DateTime.UtcNow, results);
        }

        private async Task<OfflineOpResult> ProcessOneAsync(OfflineOpRequest op, string createdBy, int? branchId,
            Dictionary<Guid, int> createdInBatch, CancellationToken ct)
        {
            if (op.OpId == Guid.Empty)
                return new OfflineOpResult(op.OpId, 400, OfflineOpOutcomes.Failed, null, "opId is required.");

            var type = (op.Type ?? string.Empty).Trim();
            if (!OfflineOpTypes.All.Contains(type, StringComparer.OrdinalIgnoreCase))
                return new OfflineOpResult(op.OpId, 400, OfflineOpOutcomes.Failed, null, $"Unknown op type '{op.Type}'.");
            type = OfflineOpTypes.All.First(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));

            var key = op.OpId.ToString("D");
            var hash = ComputeHash(type, op.Payload);

            var existing = await _records.Query(asNoTracking: true).FirstOrDefaultAsync(r => r.Key == key, ct);
            if (existing != null)
            {
                if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
                    return new OfflineOpResult(op.OpId, 409, OfflineOpOutcomes.Conflict, null,
                        "This operation id was already used with a different payload.");

                var stored = ParseStored(existing.ResponseBody);
                RememberCreatedId(type, op.OpId, stored, createdInBatch);
                return new OfflineOpResult(op.OpId, existing.ResponseStatus, OfflineOpOutcomes.Replayed, stored, null);
            }

            BaseResponse<TransactionDto> response;
            try
            {
                response = await ExecuteAsync(type, op.Payload, createdBy, createdInBatch, ct);
            }
            catch (JsonException jx)
            {
                return new OfflineOpResult(op.OpId, 400, OfflineOpOutcomes.Failed, null, $"Invalid payload for {type}: {jx.Message}");
            }
            catch (ArgumentException ax)
            {
                return new OfflineOpResult(op.OpId, 400, OfflineOpOutcomes.Failed, null, ax.Message);
            }

            if (response is null)
                return new OfflineOpResult(op.OpId, 500, OfflineOpOutcomes.Failed, null, "No response from order service.");

            var body = JsonSerializer.SerializeToElement(response, Json);

            if (!response.Success)
                return new OfflineOpResult(op.OpId, 400, OfflineOpOutcomes.Failed, body,
                    response.Error ?? response.Message ?? "Operation failed.");

            RememberCreatedId(type, op.OpId, body, createdInBatch);

            try
            {
                await _records.AddAsync(new IdempotencyRecord
                {
                    Key = key,
                    Endpoint = type,
                    RequestHash = hash,
                    ResponseStatus = 200,
                    ResponseBody = body.GetRawText(),
                    CreatedOn = DateTime.UtcNow,
                    CreatedBy = createdBy,
                    BranchId = branchId,
                }, ct);
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Unique-key race: another sync of the same client landed first.
                // The order has been executed; report it and let the client drop the op.
                _log.LogWarning(ex, "Idempotency record for {Key} already exists (concurrent sync)", key);
            }

            return new OfflineOpResult(op.OpId, 200, OfflineOpOutcomes.Ok, body, null);
        }

        private async Task<BaseResponse<TransactionDto>> ExecuteAsync(string type, JsonElement payload, string createdBy,
            Dictionary<Guid, int> createdInBatch, CancellationToken ct)
        {
            switch (type)
            {
                case OfflineOpTypes.CreateCoffeeShopOrder:
                {
                    var req = Deserialize<CreateCoffeeShopOrderRequest>(payload);
                    if (req.ItemsRequest is null || req.ItemsRequest.Count == 0)
                        throw new ArgumentException("itemsRequest must contain at least one line.");
                    return await _transactions.CreateCoffeeShopOrder(req.UserId, req.DiscountId, req.ItemsRequest, createdBy, ct,
                        req.Comment ?? string.Empty, req.IsOpenInvoice, req.setId, req.ChannelId, req.WalletAmount);
                }
                case OfflineOpTypes.AddItemsToOpenInvoice:
                {
                    var req = Deserialize<AddItemsToOpenInvoicePayload>(payload);
                    if (req.ItemsRequest is null || req.ItemsRequest.Count == 0)
                        throw new ArgumentException("itemsRequest must contain at least one line.");
                    var invoiceId = await ResolveInvoiceIdAsync(req.InvoiceId, req.InvoiceOpId, createdInBatch, ct);
                    return await _transactions.AddItemsToOpenInvoice(invoiceId, req.ItemsRequest, createdBy, ct);
                }
                case OfflineOpTypes.CloseOpenInvoice:
                {
                    var req = Deserialize<CloseOpenInvoicePayload>(payload);
                    var invoiceId = await ResolveInvoiceIdAsync(req.InvoiceId, req.InvoiceOpId, createdInBatch, ct);
                    return await _transactions.CloseOpenInvoice(invoiceId, createdBy, ct, req.WalletAmount);
                }
                default:
                    throw new ArgumentException($"Unknown op type '{type}'.");
            }
        }

        /// <summary>
        /// Invoices created offline only have a negative temp id on the client;
        /// the follow-up ops carry the creating op's id instead. Resolve it from
        /// this batch first, then from an earlier batch's stored response.
        /// </summary>
        private async Task<int> ResolveInvoiceIdAsync(int invoiceId, Guid? invoiceOpId, Dictionary<Guid, int> createdInBatch, CancellationToken ct)
        {
            if (invoiceId > 0) return invoiceId;
            if (invoiceOpId is null || invoiceOpId == Guid.Empty)
                throw new ArgumentException("invoiceId must be positive, or invoiceOpId must name the op that created the invoice.");

            if (createdInBatch.TryGetValue(invoiceOpId.Value, out var id)) return id;

            var key = invoiceOpId.Value.ToString("D");
            var rec = await _records.Query(asNoTracking: true).FirstOrDefaultAsync(r => r.Key == key, ct);
            if (rec != null)
            {
                var stored = ParseStored(rec.ResponseBody);
                var resolved = ExtractTransactionId(stored);
                if (resolved > 0) return resolved;
            }
            throw new ArgumentException("The invoice this op refers to has not been created on the server yet (its create op failed or was not synced).");
        }

        private static void RememberCreatedId(string type, Guid opId, JsonElement? body, Dictionary<Guid, int> createdInBatch)
        {
            if (type != OfflineOpTypes.CreateCoffeeShopOrder) return;
            var id = ExtractTransactionId(body);
            if (id > 0) createdInBatch[opId] = id;
        }

        private static int ExtractTransactionId(JsonElement? body)
        {
            if (body is null || body.Value.ValueKind != JsonValueKind.Object) return 0;
            if (!body.Value.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return 0;
            if (!data.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) return 0;
            return idEl.TryGetInt32(out var id) ? id : 0;
        }

        private static T Deserialize<T>(JsonElement payload)
        {
            if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                throw new JsonException("payload is required.");
            return JsonSerializer.Deserialize<T>(payload.GetRawText(), Json)
                   ?? throw new JsonException("payload could not be read.");
        }

        private static JsonElement? ParseStored(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static string ComputeHash(string type, JsonElement payload)
        {
            // Re-serialising normalises whitespace; property order is whatever the
            // client stored, which is stable across retries of the same queued op.
            var canonical = payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? "null"
                : JsonSerializer.Serialize(payload);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(type + "|" + canonical));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
