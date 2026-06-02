# Dashboard States (text capture)

The dashboard (http://localhost:8090) renders the same JSON returned by the
service `/health` and `/dlq` endpoints. Because this evidence pack runs
headless, we capture the JSON observed at each phase. The dashboard's React
view simply re-formats these payloads.

## Peak DLQ depth (WMS down, eight order.placed events buffered)

Captured at 2026-06-01T19:43:11.386Z (run id: wms-down).

Order Integration Service `/health`:

```json
{
  "status": "degraded",
  "circuitBreakerState": "CLOSED",
  "dlqDepth": 8,
  "lastProcessedAt": "2026-06-01T19:43:08.3692318Z",
  "eventsProcessed": 9,
  "duplicatesSkipped": 0
}
```

Order Integration Service `/dlq` (depth 8; one DLQ row per order). Each entry
records `firstFailedAt`, `lastAttemptAt`, `retryCount`, `lastError: HTTP 503`.
See `scenario-trace.txt` lines under `=== QA-2/QA-3: WMS DOWN scenario ===`
for the full JSON.

WMS stub `/health`:

```json
{"status":"ok","mode":"down","reservations_processed":1,"duplicates_skipped":0}
```

ERP stub `/orders` count: 9 (ERP succeeded for every order even while WMS was
down - confirms the partial-success / split-fan-out behaviour).

## Post-recovery (WMS set back to normal, reconciliation drains DLQ)

Captured at 2026-06-01T19:43:40.850Z (five seconds after the recovery
command).

Order Integration Service `/health`:

```json
{
  "status": "healthy",
  "circuitBreakerState": "CLOSED",
  "dlqDepth": 0,
  "lastProcessedAt": "2026-06-01T19:43:39.7759252Z",
  "eventsProcessed": 17,
  "duplicatesSkipped": 0
}
```

WMS stub `/reservations` count: 9 - each distinct `orderId` (1001, 1101..1108)
appears exactly once. `duplicates_skipped: 0` on the stub side confirms the
Order Integration Service's idempotency tracker prevented re-sends after the
DLQ drained.

## Cross-channel sanity (UC2: WMS-originated stock change)

WMS `/reservations` POST with `orderId: 9001` returns `200 OK` and the
wms-event-adapter `/health` counter `events_published` increments to 11
(was 10 prior to the call). The webhook -> `stock.updated` -> RabbitMQ path
is exercised; the consumer side (nopCommerce StockUpdateConsumer) is not
verified because the nopCommerce container is unavailable in this run
(documented under "Known limitations" in `README.md`).
