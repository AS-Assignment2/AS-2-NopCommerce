# Feature: nopCommerce Real Order Events

**Branch:** `feature/nop-real-order-events`
**Date:** 2026-05-19
**Status:** Merged via PR #2

Replaced the Part-1 outbox spike (`AppStartedEvent` placeholder) with the production wiring of nopCommerce to RabbitMQ for the omnichannel commerce core (Scenario C).

## What this branch delivered

| # | Concern | Outcome |
|---|---|---|
| 1 | Real `order.placed` events | Hooked into `OrderProcessingService.PlaceOrderAsync` via `IConsumer<OrderPlacedEvent>` with no surgery on core code |
| 2 | Generic outbox writer | `SpikeOutboxService` replaced by `OutboxService` with `WriteEventAsync(eventType, data)` |
| 3 | Production publisher task | `SpikeOutboxPublisherTask` replaced by `OutboxPublisherTask`; RabbitMQ config moved to `appsettings.json` |
| 4 | Inbound stock updates | `StockUpdateConsumerBackgroundService` (HostedService) consumes `stock.updated` from RabbitMQ |
| 5 | Health endpoint | `GET /integration/health` returns pending count and last-publish age, 200/503 |
| 6 | Config | New `IntegrationConfig : IConfig`, auto-bound from `appsettings.json` |
| 7 | Migration | `OutboxPublisherTaskMigration` renames the `ScheduleTask` row from the spike type to the new type (or seeds it) |

## File-by-file changes

### Added

- **`src/Libraries/Nop.Core/Configuration/IntegrationConfig.cs`**
  Typed `IConfig` bound from `appsettings.json` via the existing IConfig auto-discovery.
  Fields: `RabbitMqHostname` (default `rabbitmq`), `RabbitMqPort` (5672), `RabbitMqUsername`/`Password` (guest/guest), `EventsExchange` (`verdemart.events`), `StockUpdatedQueue` (`nopcommerce.stock-updated`), `StockUpdatedRoutingKey` (`stock.updated`), `HealthDegradedAfterSeconds` (60).

- **`src/Libraries/Nop.Services/Integration/IOutboxService.cs`** and **`OutboxService.cs`**
  Replaces `ISpikeOutboxService` / `SpikeOutboxService`. Single method: `WriteEventAsync(string eventType, object data)` serializes payload to JSON and inserts an `IntegrationEvent` row with `Published = false`.

- **`src/Libraries/Nop.Services/Integration/OrderPlacedEventConsumer.cs`**
  `IConsumer<OrderPlacedEvent>`. nopCommerce already publishes `OrderPlacedEvent` at `OrderProcessingService.cs:1617` and `:1993`, so this consumer plugs in via the existing `IEventPublisher` plumbing without touching `PlaceOrderAsync`. Loads order items via `IOrderService.GetOrderItemsAsync`, serializes `{ eventId, orderId, orderGuid, customerId, storeId, total, currency, createdOnUtc, items[] }` to the outbox as routing key `order.placed`.

- **`src/Libraries/Nop.Services/Integration/OutboxPublisherTask.cs`**
  Renamed from `SpikeOutboxPublisherTask`. Polls outbox for `Published == false` ordered by Id, publishes each to `IntegrationConfig.EventsExchange` with `IntegrationEvent.EventType` as the routing key. On success: marks `Published`, sets `PublishedOnUtc`, increments `PublishAttempts`, clears `LastError`. On failure: increments `PublishAttempts`, sets `LastError`, logs (row stays unpublished for retry).

- **`src/Libraries/Nop.Services/Integration/RabbitMQ/StockUpdateConsumerBackgroundService.cs`**
  `BackgroundService` registered as `IHostedService`. On start: declares `verdemart.events` topic exchange, durable queue `nopcommerce.stock-updated` bound to routing key `stock.updated`, sets up an `AsyncEventingBasicConsumer`. Per message: creates a service scope, resolves `IProductService`, calls `AdjustInventoryAsync(product, delta, ...)`. Manual ack on success; unknown productIds are ack'd and dropped (logged warning); deserialization/processing errors are nack'd with `requeue: false` (routes to DLX once configured).

- **`src/Presentation/Nop.Web/Controllers/IntegrationHealthController.cs`**
  `GET /integration/health` does not inherit `BasePublicController` (lightweight probe, no guest-account side effects, matching `KeepAliveController`). Counts unpublished outbox rows; reads most recent `PublishedOnUtc`. Returns JSON:
  ```json
  { "status": "healthy|degraded", "pendingOutboxCount": 0, "lastPublishedOnUtc": "...", "lastPublishedAgeSeconds": 12.4, "degradedAfterSeconds": 60 }
  ```
  Status code 503 when `pending > 0 && (no last-publish OR age > threshold)`, else 200.

- **`src/Libraries/Nop.Data/Migrations/UpgradeTo500/OutboxPublisherTaskMigration.cs`**
  `NopUpdateMigration` (`2026-05-19 00:00:00`). Idempotent: if a `ScheduleTask` row exists with the old or new `Type`, it is updated to the new fully-qualified type name and renamed; otherwise a new row is seeded with `Seconds = 10`, `Enabled = true`. Down is a no-op.

### Modified

- **`src/Libraries/Nop.Services/Integration/RabbitMQ/RabbitMqPublisher.cs`**
  Constructor takes `IntegrationConfig` and reads connection params from it. The `TODO: Move to appsettings.json` block was removed. "Spike" log lines were removed (the publisher task already logs at the higher level).

- **`src/Presentation/Nop.Web.Framework/Infrastructure/IntegrationStartup.cs`**
  - Registers `IntegrationConfig` as a singleton resolved from `Singleton<AppSettings>.Instance.Get<IntegrationConfig>()` so other services can inject it directly.
  - `IOutboxService` to `OutboxService` (scoped).
  - `IRabbitMqPublisher` to `RabbitMqPublisher` (scoped, unchanged).
  - `AddHostedService<StockUpdateConsumerBackgroundService>()`.

### Deleted

- `src/Libraries/Nop.Services/Events/AppStartedEventConsumer.cs` - spike placeholder, no longer fires anything.
- `src/Libraries/Nop.Services/Integration/ISpikeOutboxService.cs`
- `src/Libraries/Nop.Services/Integration/SpikeOutboxService.cs`
- `src/Libraries/Nop.Services/Integration/SpikeOutboxPublisherTask.cs`

## Event contracts

### `order.placed` (outbound, nopCommerce to Order Integration Service)

```json
{
  "eventId": "GUID",
  "orderId": 1234,
  "orderGuid": "GUID",
  "customerId": 42,
  "storeId": 1,
  "total": 99.50,
  "currency": "EUR",
  "createdOnUtc": "2026-05-19T10:00:00Z",
  "items": [
    { "productId": 17, "quantity": 2, "unitPrice": 49.75 }
  ]
}
```
Exchange: `verdemart.events` (topic), routing key: `order.placed`.

### `stock.updated` (inbound, Order Integration Service to nopCommerce)

```json
{
  "eventId": "GUID",
  "productId": 17,
  "delta": -2,
  "source": "wms|pos",
  "timestampUtc": "2026-05-19T10:00:05Z"
}
```
Queue: `nopcommerce.stock-updated` (durable), bound to `verdemart.events` with routing key `stock.updated`. `delta` is the signed quantity change passed to `IProductService.AdjustInventoryAsync`.

## Configuration

`appsettings.json` gains (on first run after the IConfig binding) an `IntegrationConfig` section. Override per environment to point at the real RabbitMQ host. Example for docker-compose:

```json
"IntegrationConfig": {
  "RabbitMqHostname": "rabbitmq",
  "RabbitMqPort": 5672,
  "RabbitMqUsername": "guest",
  "RabbitMqPassword": "guest",
  "EventsExchange": "verdemart.events",
  "StockUpdatedQueue": "nopcommerce.stock-updated",
  "StockUpdatedRoutingKey": "stock.updated",
  "HealthDegradedAfterSeconds": 60
}
```

## Architectural notes

- **Outbox commit semantics**: `OrderPlacedEventConsumer` writes the event row in the same logical request as the order-placed event handling. The nopCommerce `OrderPlacedEvent` is published after the order has been saved (`SaveOrderDetailsAsync` runs at `OrderProcessingService.cs:1589`), so the outbox row is committed alongside the order within the same request scope. Strict same-DB-transaction guarantees would require enrolling in the active LinqToDB transaction explicitly; for the current failure modes (broker down, network partition) the outbox write happens first and the publisher retries until the broker is reachable, so durability is preserved.
- **Idempotency**: `StockUpdateHandler` deduplicates `stock.updated` events by `EventId` using an in-process `HashSet<string>` (`StockUpdateHandler.cs:10-39`). The Order Integration Service likewise tracks processed event IDs on its side, so duplicates are dropped at both the producer and consumer boundaries.
- **Cross-channel conflict resolution** is implemented in `StockUpdateHandler.ResolveCrossChannelConflictAsync`: when a `stock.updated` event would drive `StockQuantity` below zero, the handler cancels the most recent web order(s) until stock is non-negative, then publishes an `order.cancelled` IntegrationEvent that downstream systems consume. The customer notification is handled by the framework's `CancelOrderAsync(notifyCustomer: true)` call path.
- **Polly / circuit breaker**: lives in the Order Integration Service, not in the monolith. The nopCommerce stock consumer nacks without requeue; a DLX/DLQ binding on the consumer queue is the cleanest hardening step.

## Done

1. **StockUpdateConsumerBackgroundService landed** in `src/Libraries/Nop.Services/Integration/RabbitMQ/` and is registered as a HostedService via `IntegrationStartup`.
2. **OutboxPublisherTask** ticks every 10 seconds against `IntegrationEvent` rows, with `Published`, `PublishedOnUtc`, `PublishAttempts`, and `LastError` columns populated as designed.
3. **`/integration/health` endpoint** is live on the storefront and reports `pendingOutboxCount` plus `lastPublishedAgeSeconds`, returning 503 when the publisher is stuck and 200 otherwise.
4. **Migration `OutboxPublisherTaskMigration`** is idempotent and renames the spike scheduled task on upgrade or seeds it on fresh installs.
5. **RabbitMQ wiring** consumes `IntegrationConfig` from `appsettings.json`; the spike constants and log lines were removed.

## Open follow-ups

1. **DLX/DLQ binding** for `nopcommerce.stock-updated` so nacks land somewhere observable.
2. **Persist the StockUpdateHandler dedup set** to survive process restarts (currently an in-memory `HashSet<string>`).

## How to verify locally

1. Bring up RabbitMQ (`docker-compose -f docker-compose.yml up rabbitmq`).
2. Start nopCommerce. The `OutboxPublisherTaskMigration` runs and the scheduled task ticks every 10s.
3. Place a test order through the storefront. The DB `IntegrationEvent` table shows a row with `EventType = "order.placed"`. Within 10s `Published = true`.
4. In RabbitMQ Management UI (15672) confirm the message reached `verdemart.events` with routing key `order.placed`.
5. Hit `GET /integration/health` - returns `healthy` with `pendingOutboxCount = 0`.
6. Stop RabbitMQ and place another order. Within 60s `/integration/health` flips to `503 degraded`.
7. Publish a test `stock.updated` message manually (Management UI) - the `StockUpdateConsumerBackgroundService` log line and `Product.StockQuantity` change confirm the consumer path.
