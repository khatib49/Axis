# Gap-closing modules – deployment notes

Branch: `claude/gallant-dijkstra-tgdb1l` in both `Axis` (API) and `AxisDashboardUI`.
Everything is additive: no existing table, column, endpoint, service or page was changed.

## 1. Database (run by the DB owner)

`docs/sql/20261103_GapModules.sql` is an idempotent script generated from the EF migration
`20261103120716_GapModules`. It only **creates 61 new tables** (prefixes `Acct_`, `Bi_`, `Branch_`,
`Currency_`, `Delivery_`, `Floor_`, `Hr_`, `Offline_`, `Promo_`, `SelfOrder_`, `StockOps_`, `Till_`)
with their indexes and foreign keys to existing tables (`transactions`, `Items`, `Ingredients`,
`Rooms`, `Games`, `Suppliers`, `Purchases`, `PurchaseLines`, `accounts`, `journal_entries`,
`journal_entry_lines`, `AspNetUsers`) and inserts the row in `__EFMigrationsHistory`.

Run it with psql (or paste into pgAdmin):

```bash
psql "$POSTGRES_CONNECTION" -v ON_ERROR_STOP=1 -f docs/sql/20261103_GapModules.sql
```

Do **not** run `dotnet ef database update` instead: the EF model snapshot had drifted from the
database (tables such as `Owners`, `RolePages`, `Shipments`, `SiteContents` and several columns on
`OnlineOrders`, `EventRegistrations`, `Items`, `Categories`, `Events`, `Settings` were created by
hand). The generated migration was pruned to the new tables only; the snapshot now matches the
model, so the next migration will be clean.

No seed rows are required. Lazy seeds done by the code on first use:
- Currencies: USD (base) and LBP (rate 89,500, step 1,000) on the first currency/tender call.
- Stock location "Main" (default) on the first stock-ops call.
- First branch created in the UI becomes the default branch.

## 2. Integration settings (Admin → Integrations, existing page)

| Key | Used by |
|---|---|
| `Delivery.WebhookSecret.{source}` (secret) | aggregator webhook `POST api/delivery/webhook/{source}` |
| `SelfOrder.PublicBaseUrl` (optional) | printed QR links |
| `Smtp.Host`, `Smtp.Port`, `Smtp.User`, `Smtp.Password` (secret), `Smtp.From` | scheduled report e-mails |
| `Reports.WhatsAppTemplate` | scheduled reports over WhatsApp |

## 3. Frontend build flags

- Default build: identical behaviour to today.
- `VITE_ENABLE_OFFLINE_MODE=true` at build time: enables the PWA service worker, offline order
  queue, keeps the till signed in when the connection drops, and shows the offline banner.
  Requires HTTPS.

## 4. New Hangfire jobs (registered in Program.cs)

- `stockops-expiry-alert` daily 07:00 UTC – Notification rows for batches expiring in 3 days.
- `bi-scheduled-reports` hourly at :05 – sends due scheduled reports (no-op until configured).

## 5. Roles

Pages are registered in `PageCatalog` so custom roles can be granted them in Roles & Permissions.
Built-in roles keep their legacy access; new till pages are open to cashier, gamecashier and
admin_fnb; stock pages to chef, stock and admin_fnb; everything else is admin.

## 6. Housekeeping found on the way

- `Infrastructure/DBContext/ApplicationDbContextFactory.cs` contains a hard-coded production
  connection string with a password. It should be moved to the `POSTGRES_CONNECTION` environment
  variable and the password rotated.
- `Directory.Build.rsp` disables MSBuild node reuse and the shared compiler server so parallel
  builds on one machine never hold file locks; harmless for CI.
