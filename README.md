# Restaurant Platform — V1 foundation

A clean-room restaurant POS platform started from scratch. It does **not** depend on iiko or any existing project.

## V1 technology

- POS client: Flutter (Windows, Android, iOS)
- Restaurant Node: ASP.NET Core / .NET 10
- Local restaurant database: PostgreSQL
- Realtime: SignalR (next milestone)
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
- Flutter POS starter source with PIN login, menu loading and basic order creation

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

1. Halls/tables UI and table-backed orders
2. Order item modifiers
3. Send-to-kitchen command + kitchen tickets
4. Print queue worker
5. Register shifts and payments
6. SignalR realtime between all POS clients
7. Cloud sync worker

See `docs/architecture-v1.md` and `docs/database-v1.md`.
