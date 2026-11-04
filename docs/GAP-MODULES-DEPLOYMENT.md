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

---

# Phase 2 (branch `claude/gallant-dijkstra-tgdb1l-phase2`)

Builds on phase 1. Run **after** `20261103_GapModules.sql`:

```bash
psql "$POSTGRES_CONNECTION" -v ON_ERROR_STOP=1 -f docs/sql/20261103_Phase2.sql
```

The script creates `Pay_WhishRequests`, `Till_DisplayStates`, `Promo_ComboApplications`, adds
`SeatNumber`/`Quantity` to `Floor_OrderCourses` (unique index now includes the seat),
`ReminderSentOn`/`ConfirmationSentOn` to `Floor_Reservations`, `Version` to
`Promo_HappyHourApplications`, and the **only change to an existing table**: nullable
`transactions.BranchId` with an index. Nothing is dropped.

## What phase 2 adds

| Item | Where |
|---|---|
| Ordering from the table with seat and course per line, bill and split by seat | Floor view → table → Order / Bill by seat |
| Handheld waiter mode (phone) | `/waiter` (cashier, game cashier, F&B admin, admin) |
| Happy hour and combos applied automatically, re-evaluated as lines change | till pages call `POST api/promotions/auto-apply`; chips show what is booked |
| Branch on every new sale (header → user default → default branch) | interceptor; `POST api/branches/backfill-transactions` once for legacy rows |
| Customer-facing display with USD/LBP totals and Whish QR | Admin → Till Controls → Customer Displays; open `/display?token=…` on the second screen; cashier picks "Use on this till" |
| Whish payment for invoices (till, self-order, kiosk) | `POST api/payments/whish/start`, status polling, tender "Whish" and auto-close when fully paid |
| WhatsApp reservation reminders | daily job `reservation-reminders` + Remind button |
| Menu engineering with recipe costing | Accounting → Menu Engineering |
| Automated smoke suite and dark-mode check | `npm run test:e2e`, `npm run e2e:capture`, `npm run e2e:contrast` (see UI `docs/testing.md`) |

## Settings to add (Admin → Integrations)

| Key | Purpose |
|---|---|
| `Whish.Channel`, `Whish.Secret` (secret), optional `Whish.WebsiteUrl`, `Whish.BaseUrl` | same keys the events module already uses |
| `Reservations.WhatsAppReminderTemplate`, optional `Reservations.WhatsAppConfirmTemplate` | Meta template names, 3 body params: name, date/time, table/room |

## Steps after deploy

1. Run the Phase2 SQL script.
2. Press "Backfill legacy transactions" on Admin → Branches → Consolidated (or call the endpoint) once.
3. Create a customer display per register and open its link on the second screen.
4. Set the Whish keys to enable QR payments; without them every Whish button reports "Whish not configured".

## Verified on the local build

519 backend tests pass; the UI type-checks and builds; the Playwright smoke suite loads every route per role; the phase-2 flows were exercised live (auto-apply booked a happy hour and a combo and was idempotent, seat bill and split, display push and anonymous view, branch backfill of 75 rows, menu engineering report).

## Known follow-ups

- Pre-existing bug fixed in passing: `/admin/cards` crashed because the card list is paginated.
- Pre-existing dark-mode issues on legacy admin pages (black headings) are listed in the e2e contrast report; new pages were fixed.
- Hard-coded Azure SignalR hub URLs in the UI produce console noise when running locally.
- Gift-card liability accounting, payroll and barcode scanning remain open (see the comparison PDF).

# Phase 3 (branch `claude/gallant-dijkstra-tgdb1l-phase3`)

Builds on phase 2. Run **after** `20261103_Phase2.sql`:

```bash
psql "$POSTGRES_CONNECTION" -v ON_ERROR_STOP=1 -f docs/sql/20261104_Phase3.sql
```

The script only **creates** 13 new tables (`Hr_PayComponentTypes`, `Hr_EmployeePayComponents`,
`Hr_PayrollRuns`, `Hr_PayrollLines`, `Hr_PayrollAdvances`, `Mkt_Segments`, `Mkt_Campaigns`,
`Mkt_CampaignRecipients`, `Mkt_Unsubscribes`, `Promo_AccountingPostings`, `Retail_Barcodes`,
`Retail_SerialNumbers`, `Retail_SerialSettings`). It does not alter or drop anything existing and is safe to re-run.

## What phase 3 adds

| Item | Where |
|---|---|
| Gift-card liability and promotion discount journals (sale of a card credits 2150, redemption debits it, discounts post to 4950) | Promotions → Promotion Accounting; one-off `POST api/promotions/accounting/backfill` |
| Stock per branch and backfill of every legacy row to a branch | Stock → Branch Positions; `POST api/branches/backfill?tables=all` once |
| Barcodes (EAN-13, scan at the till), shelf labels, serial numbers per sale | Inventory → Barcodes / Serial Numbers; `/admin/retail/labels` prints |
| Game sessions keep running offline and sync when the connection returns | game cashier pages, offline queue |
| Marketing segments and campaigns over email, SMS and WhatsApp, with unsubscribe links | Marketing → Segments / Campaigns; job `marketing-send-due-campaigns` every 5 minutes |
| Payroll: pay components, monthly runs from attendance, advances, journal on pay (5400 expense, 2200 payable, 2310 statutory) | HR → Payroll / Pay Components |
| Dark mode on the legacy pages, including antd tables and modals | theme toggle |
| Public online shop: catalog, cart, checkout (pay at pickup or Whish), my orders, account | `/shop` |

## Settings to add (Admin → Integrations)

| Key | Purpose |
|---|---|
| `Sms.Provider` (`http`), `Sms.Url`, `Sms.ApiKey` (secret), `Sms.From` | SMS campaigns; without them SMS recipients are marked "not configured" |
| `Marketing:PublicBaseUrl` (appsettings, optional) | absolute unsubscribe links in emails; defaults to a relative `/api` path |

Email and WhatsApp reuse the existing SMTP and WhatsApp settings. Accounts 2150 and 4950 are created automatically on first posting.

## Steps after deploy

1. Run the Phase3 SQL script.
2. Call `POST api/branches/backfill?tables=all` once (admin), then `POST api/promotions/accounting/backfill` once.
3. Add barcodes to items (or generate internal EAN-13s) and print labels.
4. Define pay components per employee before the first payroll run.
5. Link the shop from the website: `https://<dashboard>/shop`.

## Security fix in this phase

Page grants used to unlock every action of a new-module controller, so a cashier with a till page could also create barcodes, void reasons, delivery zones and floor areas, issue gift cards and approve voids. For controllers in the new modules, admin-only actions now open only through admin-grade pages. Till actions and all original controllers behave exactly as before (covered by `Tests/Security/DynamicPagePermissionTests.cs`).

## Verified on the local build

615 backend tests pass; the UI type-checks; the smoke suite loads every route per role in light and dark; seven live API flows (accounting, branch stock, barcodes/serials, offline games, marketing, payroll, storefront) pass end to end against Postgres.

## Known follow-ups

- Original controllers still let a page grant unlock their admin actions (pre-existing; not changed to avoid affecting current users).
- `ApplicationDbContextFactory` contains a hard-coded production connection string; move it to an environment variable and rotate the password.
- Whish, SMTP, SMS and WhatsApp were verified up to the provider call only; send one real message and one real payment after configuring the keys.
