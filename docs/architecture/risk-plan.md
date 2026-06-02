# Risk Plan and Validation

**Scenario C - Omnichannel Commerce Core (VerdeMart Retail)**  
**Owner:** Sebastião

---

## Top Architectural Risks

Three risks were identified as load-bearing for Scenario C. Severity is graded against the impact on the quality attribute scenarios (QA-1..QA-5) and the mandatory pressure point.

| # | Risk | Severity | Problem | Architectural impact |
|---|------|----------|---------|----------------------|
| 1 | Outbox Pattern Integration | HIGH | nopCommerce internal modification (DI, migrations, background tasks) | Events never publish if integration fails - violates QA-1, QA-5, and the entire pressure-point story |
| 2 | Cross-Channel Inventory Conflicts | MEDIUM | POS + Web simultaneous orders - race condition - overselling | Violates stock accuracy requirement (QA-2) |
| 3 | Event Idempotency and Duplicate Processing | MEDIUM | Event idempotency failures cause duplicate ERP orders or WMS reservations | Violates inventory correctness and order-ledger correctness |

---

### Risk 1 - Outbox Pattern Integration (HIGH)

**Problem.** Adding the `IntegrationEvent` table and a publisher requires modifying the nopCommerce monolith - hooking into the `OrderPlacedEvent`, registering a recurring publisher, and adding a migration. nopCommerce uses Autofac DI, FluentMigrator, and an `IRepository<T>` pattern; getting any of these wrong means events are never published and the rest of the architecture is invisible.

**Risk.** Events never publish if the integration fails. With no events leaving nopCommerce, every downstream guarantee (QA-1 availability, QA-3 recoverability, QA-4 observability, QA-5 reliability) collapses, because there is nothing for the Order Integration Service to consume.

**Mitigation.** A feasibility spike was executed before full implementation began. The spike proved the end-to-end path:
- A minimal `IntegrationEvent` table created via FluentMigrator.
- A spike outbox publisher that writes one row on application startup.
- A spike RabbitMQ publisher background service that reads the row and publishes to `verdemart.events`.
- Confirmed in the RabbitMQ Management UI within 10 s of startup.

The spike was merged via PR #1 and the working pattern was promoted to production (`OutboxPublisherTask` as an `IScheduleTask`, `OutboxService.WriteEventAsync`, `OutboxPublisherTaskMigration`).

**Status.** MITIGATED.

**Residual risk.** LOW. The core mechanism is proven and live; the production code is a refinement of working spike code.

---

### Risk 2 - Cross-Channel Inventory Conflicts (MEDIUM)

**Problem.** When OSPOS and a web order both try to sell the last available unit, a race condition can produce overselling:
- The physical store sells the last unit via OSPOS. The OSPOS Adapter polls and eventually publishes `sale.completed` (resulting stock: 0).
- Simultaneously, a web customer completes checkout. nopCommerce confirms the order based on its current stock view, which has not yet received the WMS-driven `stock.updated` correction.
- Both channels believe inventory is available; the second sale oversells.

**Risk.** Violates stock accuracy requirement (QA-2): "Stock update in nopCommerce web store within 30 s of WMS event". Direct revenue and trust impact.

**Mitigation.**
- **Event timestamp ordering for automatic resolution.** Every event carries an `eventId` (UUID) and a UTC timestamp. The Integration Service and the nopCommerce `StockUpdateHandler` apply updates in arrival order; downstream conflicts are detected by inspecting the resulting stock level.
- **Prefer cancel online over physical sell.** A physical sale is immutable (the customer left with the product). When `sale.completed` would push `Product.StockQuantity` below zero, `StockUpdateHandler.ResolveCrossChannelConflictAsync` cancels the most recent pending/processing web order(s) until stock is non-negative, calling `CancelOrderAsync(notifyCustomer: true)` and publishing an `order.cancelled` IntegrationEvent.
- **Time window tolerance (30 seconds).** A 30 s tolerance window matches the QA-2 measure and the OSPOS adapter polling cadence; orders cancelled within the window are treated as conflict-resolution rather than customer-facing failures.

**Status.** Cancellation path implemented in `StockUpdateHandler`. End-to-end exercise of the cross-channel compensation flow is recorded as partially observed in the evidence pack (`docs/evidence/`): producer leg of QA-5 verified (`wms-event-adapter.events_published` incremented within 3 s of a WMS reservation); the consumer leg in nopCommerce was blocked in the demo run by the nopCommerce container exiting with status 139 during MSSQL EF Core initialization. The mechanism is in the codebase and unit-level reachable, but full storefront-side validation is recorded as a known gap.

**Residual risk.** MEDIUM. A 30-60 s window (matching the OSPOS polling interval) remains where overselling is possible; it is observable and recoverable through the cancellation path. Tighter polling or OSPOS webhooks are the production hardening step.

---

### Risk 3 - Event Idempotency and Duplicate Processing (MEDIUM)

**Problem.** Every reliability mechanism in the architecture (Polly retry on ERP, dead-letter queue + reconciliation on WMS, RabbitMQ at-least-once delivery, manual operator replay) can deliver the same event more than once. Without explicit deduplication, the second delivery would cause: a duplicate ERP order, a double stock reservation in the WMS, or a double application of `stock.updated` in nopCommerce.

**Risk.** Violates inventory correctness and order-ledger correctness. Directly conflicts with QA-3 ("All DLQ messages processed within 60 s; zero duplicate reservations") and QA-5 ("zero duplicate ERP orders").

**Mitigation.**
- **eventId (UUID) as deduplication key.** Every IntegrationEvent carries a UUID `eventId` generated at the producer (nopCommerce outbox or OSPOS Adapter). Downstream consumers use it as the deduplication key.
- **Integration Service tracks processed eventIds (in-memory cache).** `IdempotencyTracker` in the Order Integration Service maintains a set of seen eventIds; duplicate deliveries are dropped before being forwarded to ERP or WMS. The same tracker shields the reconciliation loop when re-draining the DLQ.
- **WMS stub ignores duplicate reservationId.** The WMS stub keys idempotency by `orderId` and returns the previously-issued `reservationId` for repeated calls without re-decrementing stock. The ERP stub does the same by `eventId`.
- **nopCommerce StockUpdateHandler dedup.** The inbound `StockUpdateConsumerBackgroundService` and `StockUpdateHandler` deduplicate `stock.updated` events by `EventId` using a static `HashSet<string>` (`StockUpdateHandler.cs:10-39`), so a replay never double-applies the same correction.

**Status.** All four deduplication points are implemented and live. The evidence pack records `WMS duplicates_skipped = 0` and `ERP duplicates_skipped = 0` after the QA-3 DLQ drain, with each of the 9 distinct orderIds appearing exactly once in WMS `/reservations`.

**Residual risk.** LOW. The deduplication set is in-memory and does not survive a process restart; the persistent backstop is the WMS stub and ERP stub idempotency, plus the producer-side eventId which is stable across retries. A production hardening would persist the dedup set or use an external store.

---

## Implementation Findings

These are observations from the Part 2 evidence run that are not "risks" in the Part 1 sense (they were not pre-identified architectural risks), but are honest findings worth recording so the demo and defense can address them.

### Polly circuit breaker did not transition to OPEN

During the QA-2 scenario, the WMS stub was put into `down` mode and 13+ consecutive HTTP 503 responses were observed from the WMS leg in `service-logs.txt`. Despite this, `/health` on the Order Integration Service kept reporting `circuitBreakerState: CLOSED`. The probable cause is that `AddHttpClient<T>.AddPolicyHandler(...)` rebuilds the typed `HttpMessageHandler` per request, so the Polly `CircuitBreaker` state object is not shared across calls. The DLQ + reconciliation loop made the externally observable behavior equivalent to "breaker opens, drain on recovery", which is why QA-3 still passed cleanly: the DLQ grew monotonically to 8 and drained to 0 in 5.026 s after WMS recovery. The breaker-as-state-machine claim in QA-4 is therefore partially supported and the finding is recorded for follow-up. See `docs/evidence/README.md` and `docs/evidence/timings.md`.

### nopCommerce first-run MSSQL EF Core initialization

The nopCommerce container exited with status 139 (segfault) during its first-run MSSQL Entity-Framework initialization in the demo environment, preventing storefront-side validation of QA-5 (the consumer side of the cross-channel stock update). The rest of the bus (RabbitMQ, Order Integration Service, ERP stub, WMS stub, WMS Event Adapter, OSPOS Adapter, dashboard) was healthy throughout. This is a deployment issue, not an architectural defect, and is recorded as a known limitation in `docs/evidence/README.md`.

---

## Validation Plan

| Risk | Validation method | Evidence |
|------|------------------|----------|
| Outbox Pattern Integration | Feasibility spike - manual end-to-end test | `spikes/outbox-spike/`, PR #1 |
| Cross-Channel Inventory Conflicts | Cancellation path code review + WMS producer-leg trace | `src/Libraries/Nop.Services/Integration/StockUpdateHandler.cs`, `docs/evidence/scenario-trace.txt` |
| Event Idempotency | DLQ drain + WMS/ERP ledger comparison | `docs/evidence/timings.md`, `docs/evidence/final-health.json` |
| Docker Compose startup | `docker compose up` + health probes | `docs/evidence/health-snapshots.txt` |
| Stock update applied in nopCommerce | place order - WMS event - verify product stock | Inconclusive - nopCommerce container exit 139 documented in `docs/evidence/README.md` |

---

## Feasibility Spike

**Goal.** Prove the outbox mechanism works before committing to full implementation.

**Spike scope** (`spikes/outbox-spike/`):
1. Add a minimal `IntegrationEvent` table via FluentMigrator migration (just `Id`, `Payload`, `PublishedAt`).
2. Add a `SpikeOutboxPublisher` that writes one row on application startup (not tied to real order placement yet).
3. Add a `SpikeRabbitPublisher` background service that reads the row and publishes to RabbitMQ.
4. Confirm message received via RabbitMQ Management UI.

**Success criteria.**
- Message appears in RabbitMQ `verdemart.events` exchange within 10 s of nopCommerce startup.
- Row marked as published in DB.
- No crashes or DI registration errors.

**Outcome.** All criteria met. The `IScheduleTask` route was adopted as the final implementation. Running the outbox publisher as a nopCommerce `IScheduleTask` (recurring task via nopCommerce's own scheduler) is a known extension point and requires less DI wiring than the `IHostedService`/`INopStartup` alternative, which was kept available as a fallback but not needed.
