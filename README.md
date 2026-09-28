# Restaurant Platform — V1 foundation

A clean-room restaurant POS platform started from scratch. It does **not** depend on iiko or any existing project.

## V1 technology

- POS client: Flutter (Windows, Android, iOS)
- Restaurant Node: ASP.NET Core / .NET 10
- Local restaurant database: PostgreSQL
- Realtime: SignalR between POS clients
- Cloud sync: transactional outbox (schema included; worker comes next)

## What is already in this starter

- Restaurant / role / employee / device / hall / table domain model
- Menu: categories, products, prices, modifier schema, kitchen stations
- PIN authentication with PBKDF2 hash and JWT
- Development seed data
- Orders with UUIDv7 IDs, human display numbers and item snapshots
- Order totals and optimistic `version`
- Immutable audit events
- Transactional outbox events
- PostgreSQL Docker Compose for development
- Flutter POS with tables, guests, kitchen flow, payments and guest-split payments
- SignalR realtime synchronization between POS terminals
- Optimistic order-version conflict protection for concurrent edits
- Active table edit locks: only one POS session can edit a table at a time
- BackOffice-managed discount and surcharge presets; POS users cannot type arbitrary rates or amounts
- Two-layer adjustment access: role permission plus per-preset allowed roles
- Per-preset POS comment policy: BackOffice can require a cashier comment before applying a discount or surcharge
- Pricing Engine v1: rules can target selected dishes and/or categories
- Pricing target modes: all dishes, BackOffice-selected products/categories, or cashier-selected concrete order lines
- POS item selection is stored by exact OrderItemId, so later identical dishes do not inherit the adjustment
- BackOffice product targeting scales with dish search and category filtering
- Manual or automatic pricing rules with weekday/time schedules
- Time eligibility can use order-open time or each item's add time
- Explicit rule priority controls discount/surcharge calculation order
- Stackable/non-stackable rules prevent unwanted promotion combinations
- Applied-rule snapshots freeze value, targets, schedule and priority for existing orders
- Order/guest discounts and service charges with audit trail and correct guest-payment allocation
- Receipt/precheck printing with subtotal, discount, service and final total

## Development seed

Development-only restaurant ID:

`11111111-1111-1111-1111-111111111111`

Default development PIN:

`1234`

Change it with `SEED_ADMIN_PIN`. Never use the development PIN in production.

## 1. Start PostgreSQL

Copy `.env.example` to `.env`, change local secrets, then:

```powershell
docker compose up -d postgres
```

## 2. Start Restaurant Node

Requires .NET 10 SDK.

```powershell
cd services/restaurant-node/src/RestaurantNode.Api
$env:ConnectionStrings__RestaurantDb="Host=localhost;Port=5432;Database=restaurant_local;Username=restaurant;Password=restaurant_dev_password"
$env:Jwt__Key="replace-this-with-a-long-random-development-key-123456"
$env:Seed__AdminPin="1234"
dotnet restore
dotnet run
```

API listens on `http://0.0.0.0:8180` by default.

Health check:

```text
GET http://127.0.0.1:8180/health
```

## 3. Generate the Flutter platform shell

Flutter must be installed. From repository root on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\create-pos.ps1
```

The script runs `flutter create` for Windows, Android and iOS, then overlays our POS source.

Start on Windows:

```powershell
cd apps\pos
flutter pub get
flutter run -d windows --dart-define=API_BASE_URL=http://127.0.0.1:8180
```

For an Android emulator use `http://10.0.2.2:8180`. For a physical Android/iOS device use the Restaurant Node computer's LAN IP, for example `http://192.168.1.20:8180`.

> iOS builds require macOS/Xcode, even though the shared Flutter source can be developed elsewhere.

## Current API

Unauthenticated:

- `GET /health`
- `POST /api/v1/auth/pin`

Authenticated:

- `GET /api/v1/me`
- `GET /api/v1/menu`
- `GET /api/v1/orders/open`
- `GET /api/v1/orders/{id}`
- `POST /api/v1/orders`
- `POST /api/v1/orders/{id}/items`
- `DELETE /api/v1/orders/{id}/items/{itemId}` (only unsent items)

## Next milestone

1. Manager approval flows for sensitive operations
2. Better kitchen/receipt grouping and reprint controls
3. Cloud sync worker for transactional outbox events
4. Backup / restore and production diagnostics

See `docs/architecture-v1.md` and `docs/database-v1.md`.
