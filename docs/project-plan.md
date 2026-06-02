# Project Plan - Scenario C: Omnichannel Commerce Core (VerdeMart Retail)

## Context

**Scenario C**: nopCommerce becomes the commerce core of a wider enterprise ecosystem (ERP, warehouse, POS, shipping). The key pressure: surrounding systems become delayed, stale, or unavailable - the commerce core must remain useful, make the degradation visible, and recover.

**Two mandatory use cases:**
1. Buy-online / fulfill-through-another-channel (order -> ERP + warehouse)
2. Cross-channel stock/order-state visibility (external change reflected back in nopCommerce)

**Mandatory pressure point:** WMS goes unavailable -> nopCommerce stays functional -> orders queue -> WMS recovers -> reconciliation.

**Deliverables:**
- Part 1 - Architecture Checkpoint - 7 min presentation - 20% weight
- Part 2 - Final Delivery and Demo - 15 min live demo - 80% weight

---

## Target Architecture

```
nopCommerce (monolith, minimal changes)
  └─ Outbox table -> OutboxPublisherTask -> RabbitMQ
                                              │
                               ┌──────────────┴─────────────┐
                                                            
                   Order Integration Service          (reverse flow)
                   [.NET 10 worker, port 8083]        StockUpdateConsumer
                        │            │                (inside nopCommerce)
                                    
                    ERP Stub      WMS Stub
                   (FastAPI 8001) (FastAPI 8002)
```

**Key reliability decisions:**
- **Outbox pattern** in nopCommerce guarantees at-least-once delivery to RabbitMQ even if the broker is temporarily down.
- **Polly retry with exponential backoff** in the Order Integration Service ERP adapter (3 attempts, 1s/2s/4s) absorbs transient ERP failures.
- **Polly circuit breaker** in the Order Integration Service WMS adapter (3 failures, OPEN for 30s) detects WMS failure and short-circuits subsequent calls.
- **Dead-letter queue** inside the Order Integration Service holds undeliverable WMS messages for reconciliation.

**No shared database** across extracted boundaries.
**Framework:** ADD (Attribute-Driven Design) - QA scenarios directly drive decomposition.

---

## Part 1 - Architecture Checkpoint

Documentation-only deliverable. Four parallel tracks produced the Part 1 artefacts.

**Track A - Current-state analysis and target architecture**
- nopCommerce order flow and inventory management mapped.
- Architectural seams identified for event injection.
- C4-style target architecture diagram produced.
- Deliverables: `docs/architecture/current-state-analysis.md`, `docs/architecture/target-architecture.md`.

**Track B - Drivers, QA scenarios, framework application**
- Business and architectural drivers defined.
- QA-1 through QA-5 scenarios written (stimulus / environment / response / measure).
- ADD framework choice justified.
- Deliverables: `docs/architecture/drivers-and-qa-scenarios.md` (the ADD framework justification is captured in its "Chosen Framework" section).

**Track C - Bounded contexts and evolution path**
- Bounded contexts and data ownership identified.
- Context map drawn with upstream/downstream relationships.
- Phased evolution documented: monolith -> outbox -> integration service -> pressure demo.
- Deliverables: `docs/architecture/bounded-contexts.md`, `docs/architecture/evolution-roadmap.md`.

**Track D - ADRs, risk plan, feasibility spike**
- ADR-001: RabbitMQ vs Kafka.
- ADR-002: Outbox vs direct publish.
- ADR-003: .NET Worker Service vs Python.
- ADR-004: WMS stub vs real OpenBoxes.
- Risk plan.
- Feasibility spike: outbox -> RabbitMQ proven inside nopCommerce (`spikes/outbox-spike/`).
- Deliverables: `docs/adr/`, `docs/architecture/risk-plan.md`.

---

## Part 2 - Implementation (delivered and merged)

All components below are built, integrated on `development`, and exercised in the evidence pack.

### nopCommerce integration layer (in-monolith)

1. `IntegrationEvent` entity with FluentMigrator migration under `src/Libraries/Nop.Data/Migrations/`.
2. `OutboxPublisherTask` - polls pending rows, publishes to RabbitMQ, marks sent.
3. Hook in `OrderProcessingService.PlaceOrderAsync()` writes `order.placed` to the outbox.
4. `StockUpdateConsumerBackgroundService` consumes `stock.updated` and calls `ProductService.AdjustInventoryAsync()`.
5. `/integration/health` endpoint reports pending outbox count and last publish time.
6. Run instructions in `docs/architecture-report.md` Section 14 ("Setup and Run").

Key files: `src/Libraries/Nop.Services/Orders/OrderProcessingService.cs`, `src/Libraries/Nop.Services/Integration/`, `src/Presentation/Nop.Web/Controllers/CommonController.cs`.

### Order Integration Service (`services/order-integration-service/`)

Independently deployable .NET 10 worker on port 8083.

1. RabbitMQ consumers for `order.placed` and `sale.completed`.
2. `ErpAdapter` - HTTP POST to ERP stub with Polly retry (3 attempts, exponential backoff 1s/2s/4s).
3. `WmsAdapter` - HTTP POST to WMS stub with Polly circuit breaker (3 failures -> OPEN for 30s). On failure, the message is pushed to the internal DLQ.
4. `ReconciliationService` drains the DLQ on a timer when downstream recovers.
5. Publishes `stock.updated` to RabbitMQ after successful WMS reservation.
6. Structured logging (Serilog) carries a `correlationId` end-to-end.
7. Endpoints: `/health`, `/dlq`, `/dlq/clear`, `/webhooks/stock-changed`.

### ERP Stub, WMS Stub, OSPOS Adapter, Observability Dashboard, Docker Compose

**ERP stub** (`services/erp-stub/`, Python FastAPI, port 8001): `POST /orders`, `GET /orders`, `POST /admin/mode {normal,down}`, `POST /admin/reset`, `GET /health`. Idempotency keyed on `eventId`.

**WMS stub** (`services/wms-stub/`, Python FastAPI, port 8002): `POST /reservations`, `GET /reservations`, `GET /stock/{productId}`, `POST /admin/mode {normal,slow,down}`, `POST /admin/reset`, `GET /health`. Idempotency keyed on `orderId`.

**WMS Event Adapter** (`services/wms-event-adapter/`, Python FastAPI, port 8085): `POST /webhooks/stock-changed`, `GET /health`. Superseded by the Order Integration Service `/webhooks/stock-changed` in the final wiring; kept available as a fallback.

**OSPOS** (`jekkos/opensourcepos` image, port 8080) backed by `ospos_mysql` (MySQL 5.7).

**OSPOS Adapter** (`services/ospos-adapter/`, .NET 10 worker, no exposed port): polls `ospos_sales` and publishes `sale.completed` to RabbitMQ `verdemart.events`. SQLite idempotency store at `/app/data/idempotency.db`.

**Observability dashboard** (`services/dashboard/`, React + Vite, served by nginx on port 8090): polls service health endpoints and renders live circuit-breaker state, WMS mode, pending outbox count, and DLQ depth. Visual centrepiece of the pressure-point demo.

**`docker-compose.yml`**: single `docker compose up` starts nopCommerce, MSSQL (`nopcommerce_mssql_server`), RabbitMQ (3-management, ports 5672/15672), Order Integration Service, ERP stub, WMS stub, WMS Event Adapter, OSPOS, `ospos_mysql`, OSPOS Adapter, and Dashboard.

### Integration tests, architecture report, evidence pack, demo script

1. **Integration test suite** (`tests/integration/`) - end-to-end coverage for happy path, WMS down -> DLQ grows, WMS recovery -> reconciliation. Doubles as reproducible evidence.
2. **ADR updates** as implementation decisions crystallised.
3. **Architecture report** at `docs/architecture-report.md`.
4. **Evidence pack** at `docs/evidence/` - logs, snapshots, timings, known limitations.
5. **Demo script** is captured in `docs/architecture-report.md` Section 14 and replayed from `docs/evidence/scenario-trace.txt` for the live presentation.

---

## Repository Structure

```
AS-2-NopCommerce/
├── src/                                    # nopCommerce monolith
│   └── Libraries/Nop.Services/Integration/
├── services/
│   ├── order-integration-service/          # .NET 10 worker, port 8083
│   ├── erp-stub/                           # FastAPI, port 8001
│   ├── wms-stub/                           # FastAPI, port 8002
│   ├── wms-event-adapter/                  # FastAPI, port 8085
│   ├── ospos-adapter/                      # .NET 10 worker
│   └── dashboard/                          # React + Vite, port 8090
├── docs/
│   ├── project-plan.md                     # this file
│   ├── architecture/                       # Part 1 architecture docs
│   ├── adr/                                # Architecture Decision Records
│   └── evidence/                           # Part 2 evidence pack
├── spikes/
│   └── outbox-spike/
├── tests/
│   └── integration/
└── docker-compose.yml
```

---

## Integration Contracts

**RabbitMQ exchange:** `verdemart.events` (topic exchange).
**Routing keys:** `order.placed`, `sale.completed`, `stock.updated`, `order.cancelled`.
**Dead-letter handling:** internal DLQ inside the Order Integration Service, drained by `ReconciliationService`.

**`order.placed`** - nopCommerce -> Order Integration Service:
```json
{
  "eventId": "uuid",
  "orderId": 123,
  "customerId": 456,
  "items": [{ "productId": 789, "sku": "ABC", "quantity": 2 }],
  "totalAmount": 49.99,
  "occurredAt": "2026-05-10T14:00:00Z"
}
```

**`stock.updated`** - Order Integration Service -> nopCommerce:
```json
{
  "eventId": "uuid",
  "productId": 789,
  "warehouseId": 1,
  "newStockQuantity": 45,
  "occurredAt": "2026-05-10T14:00:05Z"
}
```

**WMS stub reservation** `POST /reservations`:
```json
{ "orderId": 123, "items": [{ "productId": 789, "quantity": 2 }] }
```

---

## Key Principles

- **Smaller and defensible beats bigger and vague.** The WMS degradation and recovery story is the core demo; everything else serves it.
- **Feasibility spike first.** The outbox integration with nopCommerce was the riskiest point and was proven in `spikes/outbox-spike/` before downstream work began.
- **Integration contracts are frozen.** All components code against the schemas above to keep tracks independent.
- **Stubs over real systems.** Justified in ADR-004 - same architectural pressure, full failure injection control, minimal ops overhead.
