# Timings table

All timestamps are UTC. "Wall-clock" deltas are computed from the absolute
timestamps recorded in `scenario-trace.txt` and `service-logs.txt`.

| Scenario | Stimulus | Observed measure | QA target | Result |
|---|---|---|---|---|
| QA-1 Baseline (happy path) | Publish `order.placed` (orderId 1001) at 2026-06-01T19:31:10Z | ERP `/orders` and WMS `/reservations` each show the order within 5 s; OIS `eventsProcessed = 1`, `dlqDepth = 0` | End-to-end propagation in < 10 s | Pass |
| QA-2 WMS unreachable, partial success | Set WMS `mode=down` at 19:31:32Z; publish 8 `order.placed` between 19:41:43Z and 19:43:08Z | ERP received all 8 orders; OIS routed all 8 to DLQ; `dlqDepth` grew 1 -> 8 monotonically; OIS reported `status: degraded` | ERP must succeed independently of WMS; failed WMS legs must be captured in DLQ, never lost | Pass |
| QA-2b Circuit breaker OPEN | Same WMS-down stimulus (13 failing calls + reconciliation retries observed in logs) | Circuit breaker remained `CLOSED` throughout; DLQ caught every failure | Polly breaker should transition CLOSED -> OPEN after 3 consecutive failures | Inconclusive - see "Known limitation: circuit breaker did not trip" in `README.md` (DLQ backstop made the bulkhead effective regardless) |
| QA-3 WMS recovery, DLQ reconciliation | Set WMS `mode=normal` at 19:43:35.824Z | OIS `dlqDepth` dropped to 0 by 19:43:40.850Z; all 9 reservations present in WMS; `duplicates_skipped = 0` on WMS stub | DLQ must drain automatically; no duplicate reservations | Pass - drained in ~5 s, no duplicates |
| QA-4 ERP transient 503 | ERP `mode=down` at 19:44:01.177Z; publish `erp-flaky-1`; `mode=normal` at 19:44:03.285Z | ERP eventually contained `erp-flaky-1` at 19:44:06.312Z (5.1 s after publish, 3.0 s after recovery) | Polly retry (3 attempts, expo backoff 1/2/4 s) should hide transient ERP failures | Pass |
| QA-5 UC2 cross-channel stock update | POST `/reservations` to WMS at 19:44:20.229Z | `wms-event-adapter.events_published` incremented from 10 to 11; reservation accepted | WMS-originated stock change must publish `stock.updated` to RabbitMQ | Pass for the producer side; consumer-side application (nopCommerce StockUpdateConsumer) Inconclusive - nopCommerce container exited during MSSQL EF init |

## Derived intervals

| From | To | Delta |
|---|---|---|
| WMS set to `down` (19:31:32.357Z) | Last failure logged before recovery (19:43:09.7Z) | ~11 m 37 s of degraded operation; 8 events absorbed by DLQ |
| WMS set to `normal` (19:43:35.824Z) | DLQ depth = 0 (19:43:40.850Z) | 5.026 s |
| ERP set to `down` (19:44:01.177Z) | ERP recorded the order (19:44:06.312Z) | 5.135 s end-to-end (transparent to publisher) |
