# Architecture V1

## Boundaries

The platform is independent. No code, database, protocol or runtime dependency is taken from iiko or the user's other projects.

## Runtime topology

```text
Cloud (later)
  ASP.NET Core + PostgreSQL
           ^
           | outbox sync
           v
Restaurant LAN
  Restaurant Node (.NET 10)
        | local PostgreSQL
        | HTTP commands + SignalR realtime
        +---- Windows POS (Flutter)
        +---- Android POS (Flutter)
        +---- iOS POS (Flutter)
        +---- Kitchen/Printer workers (next)
```

## Offline rule

Internet loss must not stop the restaurant. Restaurant Node and its local PostgreSQL database are the source of truth for restaurant operations while offline. Cloud synchronization is asynchronous.

For V1, Restaurant Node itself is a required local dependency. Device-independent emergency mode is explicitly deferred.

## Core design rules

1. IDs are UUIDv7. Human order numbers are separate database-generated numbers.
2. Closed financial facts are not silently rewritten; later corrections become explicit operations.
3. Order lines snapshot product name and price at sale time.
4. Audit events are append-only.
5. Cloud integration uses the transactional outbox pattern.
6. Flutter never connects directly to PostgreSQL.
7. Device/hardware integration belongs behind Restaurant Node adapters whenever practical.
8. Start as a modular monolith, not microservices.
9. Financial adjustments are centrally configured in BackOffice. POS clients may only apply active presets authorized for the employee role; they never define arbitrary discount/surcharge values.
10. Pricing rules are evaluated server-side per order item. Rules may target products/categories, use restaurant-local schedules, choose order-open or item-added time, run manually or automatically, and execute by explicit priority.
11. Applied pricing rules keep immutable calculation snapshots so later BackOffice changes do not silently alter an already-applied order rule.

## First vertical slice

```text
PIN -> JWT -> Menu -> Create order -> Add item -> Persist -> Audit -> Outbox
```

This starter implements that slice and now also includes SignalR realtime change notifications between POS clients plus optimistic order-version conflict protection.
