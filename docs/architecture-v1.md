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
        | HTTP now / SignalR next
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

## First vertical slice

```text
PIN -> JWT -> Menu -> Create order -> Add item -> Persist -> Audit -> Outbox
```

This starter implements that slice.
