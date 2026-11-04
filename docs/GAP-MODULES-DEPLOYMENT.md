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

# Phase 4 (branch `claude/gallant-dijkstra-tgdb1l-phase4`)

Builds on phase 3. Run **after** `20261104_Phase3.sql`:

```bash
psql "$POSTGRES_CONNECTION" -v ON_ERROR_STOP=1 -f docs/sql/20261104_Phase4.sql
```

The script only **creates** 9 new tables (`Menu_ItemModels`, `Upsell_Rules`, `Upsell_Pairs`, `Upsell_Events`,
`Delivery_Platforms`, `Delivery_ItemMappings`, `Delivery_PlatformPushes`, `Pay_CardTerminals`, `Pay_TerminalPayments`).
Nothing existing is altered or dropped, and it is safe to re-run.

## What phase 4 adds

| Item | Where |
|---|---|
| 3D models per item (GLB for web and Android, USDZ for iPhone AR), "View on your table" | Inventory → 3D Menu; shown on the shop product page, public menu, QR self-order and kiosk |
| Upsells: manager rules plus pairs learned nightly from paid invoices, optional AI-written pitch, acceptance report | Promotions → Upsells; suggestions on the till order drawer, waiter, shop cart, self-order and kiosk |
| Delivery apps: item mapping, menu feed with markup, auto-accept, signed status updates back to the app with retry | F&B Admin → Delivery Apps |
| Card terminals: stand-alone (approval code typed), connected through an HTTP API, or simulator for tests; Card tender recorded and invoice closed automatically; reconciliation report | Till Controls → Card Terminals; "Card terminal" panel in the till checkout |

## Settings (Admin → Integrations)

| Key | Purpose |
|---|---|
| `Delivery.WebhookSecret.{source}` | Shared secret per app: checks incoming orders and menu-feed calls, signs status updates (`X-Axis-Signature: sha256=<HMAC of body>`) |
| `Delivery.ApiKey.{source}` (optional) | Bearer key sent with status updates to the app |
| `CardTerminal.Cloud.BaseUrl`, `CardTerminal.Cloud.ApiKey` | Bank or provider API for connected terminals (contract below) |
| `CardTerminal.Cloud.WebhookSecret` (optional) | Lets the provider push results to `POST api/payments/terminal/callback` |
| `CardTerminal.AllowSimulator` | `true` only on test systems; leave empty in production |
| `Upsell.AiPitches` (optional) | `false` turns off AI-written pitches; they also need `Anthropic.ApiKey` |

Connected-terminal contract that a bank adapter or local bridge must expose:
`POST {BaseUrl}/payments {reference, terminalId, amount, currency, description}`,
`GET {BaseUrl}/payments/{reference}` → `{status: pending|approved|declined|cancelled|failed, approvalCode, last4, scheme, rrn}`,
`POST {BaseUrl}/payments/{reference}/cancel`.

## New jobs

- `upsell-rebuild-pairs` daily 01:30 UTC.
- `delivery-platform-status-push` every minute (no-op without a callback URL).

## Steps after deploy

1. Run the Phase4 SQL script.
2. Upsells → "Relearn now" once, then add any rules you want first.
3. Card Terminals → add one terminal per register. Use "Stand-alone" until the bank gives an API.
4. Delivery Apps → connect the app, set its shared secret, send the app the webhook and menu-feed URLs, map items.
5. 3D Menu → upload models for your best sellers.

## Verified on the local build

645 backend tests pass. The smoke suite loads every route per role in light and dark (168 pass). A live API flow of 61 checks passed twice against Postgres. It covered card payments with the simulator, manual codes, a fake bank API and signed callbacks, plus upsells, 3D uploads and file serving, delivery-app mapping, feed, auto-accept and signed pushes, and the role checks. A browser walkthrough confirmed the 3D model renders on the shop, menu and kiosk, the upsell adds items at the till and kiosk, and the card checkout closes the invoice.

## Limits to know

- Toters (or any app) must call the webhook and menu feed in the Axis format. If they impose their own API, a small adapter is needed. Nothing app-specific was available to test against.
- Connected card terminals need a bank or provider API in the contract above. Without one, use the stand-alone mode, which works with any terminal today.
- The legacy public `/menu` page only lists categories whose type is Food, Drinks, Retail or Tobacco. Categories typed "FnB" do not appear there (pre-existing).

# Phase 5 (branch `claude/gallant-dijkstra-tgdb1l-phase5`)

Builds on phase 4. Run **after** `20261104_Phase4.sql`:

```bash
psql "$POSTGRES_CONNECTION" -v ON_ERROR_STOP=1 -f docs/sql/20261104_Phase5.sql
```

The script only **creates** 10 new tables (`Tour_Tournaments`, `Tour_Participants`, `Tour_Matches`,
`Booking_StationBookings`, `Station_Codes`, `OwnerBot_Chats`, `Parent_Links`, `Alert_Rules`, `Alert_Events`,
`Ai_ImportDrafts`). Nothing existing is altered or dropped, and it is safe to re-run.

## What phase 5 adds

| Item | Where |
|---|---|
| Tournaments: public sign-up, seeded bracket with byes, free stations for ready matches, tap the winner, places, prize split paid into wallets | Gaming → Tournaments; guests at `/t` and `/t/<link>` |
| Book a PS5 room or station online, QR check-in, staff list, no-shows marked automatically | `/book`; Gaming → Station Bookings |
| Order food from the station while a game runs; one open invoice per station; extend-time requests | Gaming → Station QR Ordering; guests at `/station/<code>` |
| Owner assistant on Telegram and WhatsApp: summary, free questions to the AI, daily summary | Owner → Owner Assistant |
| Parent wallet: daily spend limit enforced at checkout, alerts on spend / low balance / session end, parent page | Gaming → Parent Wallet; parents at `/parent/<link>` |
| Owner alerts: 10 conditions (sales, no sales, big sale, voids, low stock, old open tabs, waiting delivery, cash variance, busy stations), hours, cooldown, Telegram / WhatsApp / email / bell | Owner → Owner Alerts |
| AI forecast of sales and ingredients to buy; AI supplier invoice scan to purchase; AI menu import from photo, PDF or CSV | Owner → AI Forecast / AI Invoice Scan / AI Menu Import |
| Setup in minutes: live checklist, business details, language, menu import | Owner → Setup |
| Journal exports for Xero, QuickBooks Online, Odoo and CSV, plus chart of accounts | Accounting → Accounting Exports |
| Arabic and English with right-to-left layout | language button in the header and on guest pages |
| Integration settings page (keys for AI, Whish, WhatsApp, Telegram, booking) | Owner → Integrations |

## Settings (Owner → Integrations / Owner Assistant)

| Key | Purpose |
|---|---|
| `Telegram.BotToken`, `Telegram.WebhookSecret` (created by "Connect webhook") | Owner bot on Telegram |
| `WhatsApp.VerifyToken`, `WhatsApp.AppSecret` | Incoming WhatsApp messages for the owner bot (reuses `WhatsApp.PhoneNumberId` / `AccessToken`) |
| `OwnerBot.AllowedTelegramChatIds`, `OwnerBot.AllowedWhatsAppNumbers` | Who may use the bot and receives alerts |
| `OwnerBot.DailySummaryHour`, `OwnerBot.DailySummary` | Daily summary time, or `false` to turn it off |
| `Alerts.Emails`, `Alerts.WhatsAppTemplate` | Email recipients; WhatsApp template for alerts outside Meta's 24-hour window |
| `Venue.TimeZone` | Venue day for "today" (default Asia/Beirut) |
| `Booking.OpenHour`, `Booking.CloseHour`, `Booking.SlotMinutes`, `Booking.MaxDays`, `Booking.RoomIds`, `Booking.RequireApproval`, `Booking.NoShowMinutes` | Online booking rules |
| `Station.RequireRunningSession` | `false` lets guests order from the station QR without a running game |
| `Anthropic.ApiKey` | Needed for menu photos/PDFs, invoice scan and free questions to the bot |

## New jobs

- `owner-alerts-and-parent-wallet` every 5 minutes.
- `booking-no-shows` every 10 minutes.
- `owner-daily-summary` hourly, sends only at the configured hour.

## Changes to existing code

- `WalletService.SpendAsync` refuses a wallet payment over a parent's daily limit (read-only check; no effect for wallets without parent controls).
- `AnonymousRateLimit` now counts per client IP **and endpoint**. Before, one counter per IP covered every guest endpoint, so browsing availability could block the booking itself, and guests on one venue Wi-Fi shared it.

## Verified on the local build

666 backend tests pass. The smoke suite loads every route per role: 189 pass in light theme; in dark theme 188 pass and the one failure (`/avatars`, a template demo page) passed on rerun after a dev-server reload during the run. A live API flow of 66 checks passed twice against Postgres, with fake Telegram and WhatsApp endpoints. A browser walkthrough of 18 checks confirmed Arabic right-to-left, online booking, Arabic tournament sign-up, picking a winner, ordering from the station and the owner pages.

## Limits to know

- AI photo / PDF reading and free bot questions were tested with a simulated AI answer, not the real Anthropic API (no key here). CSV menu import works without AI and was tested live.
- Telegram and WhatsApp were tested against local fakes of their APIs. Do one real message with your bot after setting the keys.
- Tournament entry fees are collected at the till; the page only marks players as paid.
- Arabic covers the menu, the header, guest pages and the new pages. Older admin pages keep English text inside the page.
