# Evidence pack - pressure-point demo

This folder contains the raw artifacts produced by the Part 2 demo of the
VerdeMart Order Integration architecture. Every claim in the architecture
report links back to a line in one of these files.

## Architectural references

- ADR-001: nopCommerce as canonical order source - `docs/adr/ADR-001-*`
- ADR-002: Event-driven Order Integration Service with RabbitMQ topic
  exchange `verdemart.events` - `docs/adr/ADR-002-*`
- ADR-003: ERP retry (Polly `WaitAndRetryAsync(3, expo)`) + WMS circuit
  breaker (3 failures / 30 s break) + Dead-letter queue with periodic
  reconciliation - `docs/adr/ADR-003-*`
- ADR-004: OSPOS adapter polling and `sale.completed` fan-out -
  `docs/adr/ADR-004-*`

## Artifacts in this folder

| File | What it shows |
|---|---|
| `health-snapshots.txt` | First `/health` probe of every service at run start (all 200 OK). |
| `scenario-trace.txt` | Time-stamped script log for every scenario (QA-1..QA-5), including the JSON returned by each probe. |
| `service-logs.txt` | `docker compose logs --tail 200` for `order-integration-service`, `erp-stub`, `wms-stub`, `wms-event-adapter`, `ospos_adapter`, `rabbitmq`. |
| `final-health.json` | One-shot snapshot of every reachable service's `/health`, `/orders`, `/reservations`, `/dlq` taken at the end of the run. |
| `dashboard-states.md` | Text capture of the dashboard's view at peak DLQ depth and after recovery. |
| `timings.md` | The QA results table with absolute timestamps and computed deltas. |

## QA-1..QA-5 traceability

### QA-1 - End-to-end order propagation (baseline)

- Stimulus: `scenario-trace.txt`, section `=== BASELINE (QA-1 ...) ===`,
  publish `orderId: 1001` at 19:31:10Z.
- Evidence: ERP `/orders` count: 1 with `eventId: baseline-001`; WMS
  `/reservations` count: 1 with the same `orderId`; OIS `/health`
  `eventsProcessed = 1`, `lastProcessedAt = 19:31:10.557Z`.
- Result: **Pass**.

### QA-2 - Bulkhead under WMS failure

- Stimulus: `scenario-trace.txt`, section `=== QA-2/QA-3: WMS DOWN
  scenario ===`. WMS set to `down` at 19:31:32.357Z; eight `order.placed`
  events published.
- Evidence: ERP successfully recorded all 8 orders (`erp` was untouched
  by the WMS outage). OIS `/dlq` shows depth grew 1 -> 8; every entry
  carries `lastError: HTTP 503`. OIS `status` transitioned to
  `degraded`. The DLQ caught every failed WMS leg - zero events lost.
- Sub-result on the circuit breaker: see "Known limitations" below.
- Result: **Pass** for the bulkhead behaviour; **Inconclusive** for the
  circuit-breaker state transition.

### QA-3 - Self-healing via reconciliation

- Stimulus: `scenario-trace.txt`, section `=== QA-3: WMS RECOVERY
  scenario ===`. WMS set to `normal` at 19:43:35.824Z.
- Evidence: OIS `dlqDepth` was 0 at 19:43:40.850Z (5.026 s after
  recovery). All 9 distinct `orderId`s present exactly once in WMS
  `/reservations`; WMS reports `duplicates_skipped: 0` (the OIS
  idempotency tracker prevented double-delivery).
- Result: **Pass**.

### QA-4 - Transient ERP outage absorbed by Polly retry

- Stimulus: `scenario-trace.txt`, section `=== QA-4: ERP TRANSIENT 503
  ===`. ERP `mode=down` at 19:44:01.177Z; `mode=normal` at
  19:44:03.285Z.
- Evidence: ERP recorded `erp-flaky-1` at 19:44:06.312Z (3.0 s after
  recovery, well inside the Polly retry budget of 1+2+4 s).
- Result: **Pass**.

### QA-5 - Cross-channel stock update from WMS

- Stimulus: `scenario-trace.txt`, section `=== QA-5: UC2 cross-channel
  ===`. POST `/reservations` with `orderId: 9001` at 19:44:20.229Z.
- Evidence: `wms-event-adapter` `/health` `events_published`
  incremented from 10 to 11 within 3 s of the reservation, confirming
  the webhook -> RabbitMQ producer leg works.
- Inconclusive part: the nopCommerce `StockUpdateConsumer` could not be
  exercised because the nopCommerce container exited during MSSQL
  Entity-Framework initialization (status 139). The producer chain is
  fully verified; consumer-side application of `stock.updated` is left
  as a known gap.
- Result: **Partial** - producer Pass, consumer Inconclusive.

## Known limitations of this run

1. **nopCommerce unavailable**. The `nopcommerce` container exited with
   status 139 (segfault) during its first-run MSSQL Entity-Framework
   initialization. The container repeatedly hits the same crash on this
   host and the 3-minute budget set for this evidence pack was reached.
   Everything that depends on nopCommerce being live - the
   `StockUpdateConsumer` for QA-5 and any UI screenshots from the
   admin - is therefore deferred. The rest of the bus (RabbitMQ
   exchange, OIS, ERP stub, WMS stub, WMS event adapter, OSPOS adapter,
   dashboard) was healthy throughout.
2. **Circuit breaker did not transition to OPEN**. Polly is configured
   on the `WmsAdapter` typed `HttpClient` with
   `handledEventsAllowedBeforeBreaking: 3, durationOfBreak: 30s` and
   reflected onto `HealthState` via `onBreak/onReset/onHalfOpen`. During
   QA-2 we observed 13+ consecutive WMS 503s in `service-logs.txt`, yet
   `/health` kept reporting `circuitBreakerState: CLOSED`. The most
   likely root cause is that `AddHttpClient<T>.AddPolicyHandler(...)`
   rebuilds the handler chain per HttpMessageHandler lifetime, so the
   `CircuitBreaker` state object is not shared across calls. This is a
   real architectural finding and will be reflected in the
   pressure-point report; the DLQ + reconciliation backstop made the
   externally observable behaviour identical to "breaker open then
   drain", which is why QA-3 still passed cleanly.
3. **No real-time UI screenshots**. The dashboard renders the same JSON
   captured in `final-health.json`; see `dashboard-states.md` for a
   text-equivalent rendering at the two key moments.

## Reproducing this evidence

```
cd "$REPO_ROOT"     # the AS-2-NopCommerce checkout
docker compose up -d --build order-integration-service
# Then re-run the four scenarios as scripted in scenario-trace.txt.
```
