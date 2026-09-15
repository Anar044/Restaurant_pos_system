# Database V1

The starter includes the full V1-oriented domain shape even though only the first vertical slice has endpoints today.

## Identity and restaurant structure

- `restaurants`
- `roles`
- `employees`
- `devices`
- `halls`
- `dining_tables`

## Menu

- `categories`
- `kitchen_stations`
- `products`
- `product_prices`
- `modifier_groups`
- `modifiers`
- `product_modifier_groups`
- `modifier_group_modifiers`

## Operations

- `shifts`
- `orders`
- `order_items`
- `order_item_modifiers`
- `payments`
- `cash_transactions`
- `kitchen_tickets`
- `print_jobs`

## Reliability and audit

- `audit_events`
- `outbox_events`

## Important modeling choices

### Orders are not owned by a cash shift

A waiter may create an order from an Android/iOS tablet while the payment is later accepted on a Windows register. Therefore the payment references the register shift; the restaurant order itself does not have to belong to that shift.

### Menu snapshots

`order_items` stores `product_name_snapshot` and `unit_price`. A later rename or repricing of a product must not mutate old orders.

### Version

`orders.version` is incremented on mutations. API clients receive the current version so optimistic concurrency can be enforced as the editing API grows.

### Outbox

Domain changes and `outbox_events` are committed in the same local PostgreSQL transaction. A future sync worker sends unsent events to Cloud and marks them processed only after acknowledgement.
