using System.Text.Json;
using Domain.Entities;
using Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Infrastructure.Persistence
{
    /// <summary>
    /// Watches SaveChanges and writes one AdminAuditLog row per allow-listed
    /// entity that was added / modified / deleted. Runs automatically inside
    /// the same transaction as the original change — so the audit either
    /// commits with the change or rolls back with it.
    ///
    /// Lives in Infrastructure so it can read EF metadata, but it only
    /// references Domain entity types (no Application dependency).
    /// </summary>
    public class AdminAuditInterceptor : SaveChangesInterceptor
    {
        private readonly IHttpContextAccessor _http;

        // Entities we want to audit. Lookup uses CLR Type.Name so this is
        // cheap. To audit a new entity, just add its name here.
        private static readonly HashSet<string> AuditedEntities = new(StringComparer.Ordinal)
        {
            nameof(Item),
            nameof(Category),
            nameof(Discount),
            nameof(Room),
            nameof(Setting),
            nameof(Channel),
            nameof(Ingredient),
            nameof(Supplier),
            nameof(Purchase),
            nameof(PurchaseLine),
            nameof(RecipeLine),
            nameof(ExpenseCategory),
            nameof(Expense),
            nameof(Account),
            nameof(AccountType),
            nameof(AppUser),                // user create / update / delete by admin
            // Notably NOT audited (their own audit/history covers them):
            //   TransactionRecord / TransactionItem  → TransactionAuditLog
            //   JournalEntry / JournalEntryLine      → posted/voided lifecycle
            //   StockMovement                        → IS the audit
        };

        // Properties we don't want noise from in field-change deltas.
        // These are bookkeeping fields the app sets automatically.
        private static readonly HashSet<string> IgnoredFields = new(StringComparer.Ordinal)
        {
            "Id", "CreatedOn", "ModifiedOn", "CreatedAt", "ModifiedAt",
            "CreatedBy", "ModifiedBy",
            // Identity bookkeeping noise (Lockout counters, security stamp churn,
            // login tokens) — none of these are "admin actions".
            "AccessFailedCount", "LockoutEnd", "LockoutEnabled",
            "ConcurrencyStamp", "SecurityStamp",
            "LastLoginAt", "RefreshToken", "RefreshTokenExpiry",
            // SECURITY: never persist password material into the audit log.
            // Snapshot + delta paths both skip these.
            "PasswordHash", "PasswordSalt",
        };

        // System-managed fields: these are recomputed by background jobs
        // (balance rebuild, stock movements, sales). An Update where the
        // ONLY modified field belongs to this list is not an admin action —
        // skip it entirely so the Admin Activity tab stays clean.
        // Key = entity type name; value = set of field names.
        private static readonly Dictionary<string, HashSet<string>> SystemManagedFields
            = new(StringComparer.Ordinal)
            {
                [nameof(Account)]    = new(StringComparer.Ordinal) { "CurrentBalance", "Balance" },
                [nameof(Item)]       = new(StringComparer.Ordinal) { "Quantity", "QuantityOnHand" },
                [nameof(Ingredient)] = new(StringComparer.Ordinal) { "QuantityOnHand", "BuyPricePerUnit" },
                // BuyPricePerUnit is auto-updated by purchases (latest-cost rule)
                // so admin edits to it via the UI will be drowned out by purchase
                // events — keep it out of the admin feed.
            };

        public AdminAuditInterceptor(IHttpContextAccessor http)
        {
            _http = http;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ApplicationDbContext ctx)
            {
                WriteAudits(ctx);
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            if (eventData.Context is ApplicationDbContext ctx)
            {
                WriteAudits(ctx);
            }
            return base.SavingChanges(eventData, result);
        }

        private void WriteAudits(ApplicationDbContext ctx)
        {
            var actor = _http.HttpContext?.User?.Identity?.Name ?? "system";
            var now = DateTime.UtcNow;

            // Snapshot the relevant entries first — we'll mutate them during
            // logging so iterate over a stable copy.
            var entries = ctx.ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added
                         || e.State == EntityState.Modified
                         || e.State == EntityState.Deleted)
                .Where(e => AuditedEntities.Contains(e.Entity.GetType().Name))
                .ToList();

            foreach (var entry in entries)
            {
                var typeName = entry.Entity.GetType().Name;
                var entityId = TryGetIntKey(entry);
                var entityName = TryGetFriendlyName(entry);

                string action;
                string? fieldChanges = null;
                string? snapshot = null;

                switch (entry.State)
                {
                    case EntityState.Added:
                        action = "Created";
                        snapshot = SerializeSnapshot(entry);
                        break;

                    case EntityState.Deleted:
                        action = "Deleted";
                        snapshot = SerializeOriginalSnapshot(entry);
                        break;

                    case EntityState.Modified:
                        action = "Updated";
                        var deltas = BuildDeltas(entry);
                        if (deltas.Count == 0)
                        {
                            // No real changes (or only ignored fields like
                            // ModifiedOn). Skip to avoid noise.
                            continue;
                        }
                        fieldChanges = JsonSerializer.Serialize(deltas);
                        break;

                    default:
                        continue;
                }

                ctx.AdminAuditLogs.Add(new AdminAuditLog
                {
                    EntityType = typeName,
                    EntityId = entityId,
                    EntityName = entityName,
                    Action = action,
                    FieldChanges = fieldChanges,
                    Snapshot = snapshot,
                    ChangedBy = actor,
                    ChangedOn = now
                });
            }
        }

        // ── Helpers ────────────────────────────────────────────────────
        private static int? TryGetIntKey(EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey();
            if (key == null || key.Properties.Count != 1) return null;
            var keyProp = key.Properties[0];
            var v = entry.Property(keyProp.Name).CurrentValue
                 ?? entry.Property(keyProp.Name).OriginalValue;
            if (v == null) return null;
            try { return Convert.ToInt32(v); }
            catch { return null; }
        }

        // Best-effort friendly name — common fields across entities, with
        // UserName / Email added so deleted users show up as e.g.
        // "user@axislb.com" rather than just "#42".
        private static string? TryGetFriendlyName(EntityEntry entry)
        {
            foreach (var prop in new[] { "Name", "AccountName", "Title", "InvoiceNumber", "UserName", "Email", "FullName" })
            {
                var meta = entry.Metadata.FindProperty(prop);
                if (meta == null) continue;
                var v = entry.Property(prop).CurrentValue
                     ?? entry.Property(prop).OriginalValue;
                if (v is string s && !string.IsNullOrWhiteSpace(s))
                    return s.Length > 200 ? s[..200] : s;
            }
            return null;
        }

        private static Dictionary<string, object> BuildDeltas(EntityEntry entry)
        {
            // Look up the system-managed field set for this entity type
            // (Account.CurrentBalance, Item.Quantity, etc.). Those fields
            // are excluded from the delta — if they were the ONLY thing
            // that changed, the result is an empty dictionary and the
            // caller drops the row, keeping the Admin Activity feed clean.
            var typeName = entry.Entity.GetType().Name;
            SystemManagedFields.TryGetValue(typeName, out var sysFields);

            var deltas = new Dictionary<string, object>();
            foreach (var prop in entry.Properties)
            {
                if (!prop.IsModified) continue;
                var name = prop.Metadata.Name;
                if (IgnoredFields.Contains(name)) continue;
                if (sysFields != null && sysFields.Contains(name)) continue;
                var oldV = prop.OriginalValue;
                var newV = prop.CurrentValue;
                if (Equals(oldV, newV)) continue;
                deltas[name] = new { old = oldV, @new = newV };
            }
            return deltas;
        }

        private static string SerializeSnapshot(EntityEntry entry)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in entry.Properties)
            {
                if (IgnoredFields.Contains(prop.Metadata.Name)) continue;
                dict[prop.Metadata.Name] = prop.CurrentValue;
            }
            return JsonSerializer.Serialize(dict);
        }

        private static string SerializeOriginalSnapshot(EntityEntry entry)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in entry.Properties)
            {
                if (IgnoredFields.Contains(prop.Metadata.Name)) continue;
                dict[prop.Metadata.Name] = prop.OriginalValue;
            }
            return JsonSerializer.Serialize(dict);
        }
    }
}
