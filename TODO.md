# TODO - Scenario C Implementation

## Part 1 - Architecture Checkpoint (Due May 5-6) - COMPLETE

### All Requirements Met
- [x] Scenario choice (Scenario C)
- [x] Current-state analysis (docs/architecture/current-state-analysis.md)
- [x] Domain and boundary model (docs/architecture/bounded-contexts.md)
- [x] QA scenarios (5 complete - QA-1 through QA-5 in drivers-and-qa-scenarios.md)
- [x] Chosen framework (ADD justification in drivers-and-qa-scenarios.md)
- [x] Target architecture with diagrams (docs/architecture/target-architecture.md)
- [x] Evolution roadmap (docs/architecture/evolution-roadmap.md)
- [x] ADRs (4 complete: RabbitMQ, Outbox, Runtime, Stubs)
- [x] Risk plan document (docs/architecture/risk-plan.md)
- [x] Feasibility spike (proven, merged via PR #1, pushed to remote)
- [x] Presentation started (commit f39dbe96d2)

**Git Status:**
- [x] Spike branch merged to develop
- [x] Pushed to origin (origin/spike/outbox-pattern-feasibility)
- [x] All Part 1 commits pushed to origin/develop

### Documentation Updates (Post-Checkpoint)
- [x] Update risk-plan.md with spike evidence (Risk 1 MITIGATED, Risk 3 updated)
- [x] Update target-architecture.md diagrams to include OSPOS
- [x] Document OSPOS integration pattern in ADR-004
- [x] Update C4 Context diagram with OSPOS and Cashier actor
- [x] Update Architecture Overview with OSPOS Adapter
- [x] Update evolution path with OSPOS deployment step

---

## Part 2 - Final Delivery (Due June 2-3)

### Week 2 - External Systems & Infrastructure

#### Order Integration Service
- [ ] Project setup (.NET 10 worker service)
- [ ] RabbitMQ consumer configuration
  - [ ] Subscribe to `verdemart.events` exchange
  - [ ] Listen for `order.placed` routing key
  - [ ] Listen for `sale.completed` routing key (from OSPOS Adapter)
  - [ ] Deserialize IntegrationEvent messages
- [ ] ERP Adapter
  - [ ] HTTP client for ERP stub
  - [ ] Polly retry policy (exponential backoff, 3 retries)
  - [ ] Error handling and logging
  - [ ] Handle both web orders and POS sales
- [ ] WMS Adapter
  - [ ] HTTP client for WMS stub
  - [ ] Polly circuit breaker (3 failures → open, 30s timeout)
  - [ ] Dead-letter queue routing on circuit open
  - [ ] Handle both web orders (POST /reservations) and POS sales (POST /pos-sales)
- [ ] POS Sale Consumer
  - [ ] Consume `sale.completed` events from OSPOS Adapter
  - [ ] Forward to WMS stub (POST /pos-sales)
  - [ ] Forward to ERP stub (POST /sales)
  - [ ] WMS publishes `stock.updated` → nopCommerce updates stock
- [ ] Reconciliation loop
  - [ ] Background service polls DLQ
  - [ ] Retry on circuit half-open
  - [ ] Mark as processed on success
- [ ] Health endpoint
  - [ ] GET /health
  - [ ] Returns: circuit state, DLQ depth, last processed event
- [ ] Idempotency
  - [ ] Track processed eventIds (in-memory cache or database)
  - [ ] Skip duplicate messages
- [ ] Dockerize
  - [ ] Dockerfile
  - [ ] Add to docker-compose.yml

#### ERP Stub
- [x] Create `services/erp-stub/` project (Python + FastAPI)
- [x] POST /orders endpoint
  - [x] Accept order data
  - [x] Store in-memory (simulate ERP acceptance)
  - [x] Return 200 OK or configurable failure
- [x] Idempotency by `eventId` — duplicate retries return cached response, never double-count
- [x] POST /admin/mode endpoint
  - [x] Modes: `normal`, `down`
  - [x] In `down` mode: return 503 for all requests
- [x] POST /admin/reset endpoint — clear all state, reset mode to normal
- [x] GET /orders endpoint (for verification)
- [x] GET /health — reports mode, orders_received, duplicates_skipped
- [x] Dockerize
  - [x] Dockerfile
  - [x] Add to docker-compose.yml

#### WMS Stub
- [x] Create `services/wms-stub/` project (Python + FastAPI)
- [x] POST /reservations endpoint
  - [x] Accept reservation request
  - [x] Calculate new stock level
  - [x] Call WMS Event Adapter webhook (instead of direct RabbitMQ)
  - [x] Return 200 OK or configurable failure/timeout
- [x] Idempotency by `orderId` — duplicate DLQ retries return cached reservationId, zero double-deductions (QA-3)
- [x] POST /admin/mode endpoint
  - [x] Modes: `normal`, `slow` (10s delay), `down` (503 error)
- [x] POST /admin/reset endpoint — clear all state, reset stock and mode
- [x] GET /reservations endpoint (for verification)
- [x] GET /stock/{productId} endpoint
- [x] GET /health — reports mode, reservations_processed, duplicates_skipped
- [x] Dockerize
  - [x] Dockerfile
  - [x] Add to docker-compose.yml

#### WMS Webhook Endpoint / WMS Event Adapter
- [x] Implemented as standalone `services/wms-event-adapter/` (Python + FastAPI + pika, port 8085)
  - Note: temporary bridge until Integration Service implements this endpoint natively.
    When ready, set `STOCK_WEBHOOK_URL=http://order-integration-service:8080/webhooks/stock-changed`
- [x] POST /webhooks/stock-changed endpoint
  - [x] Receive webhook from WMS stub
  - [x] Publish `stock.updated` to RabbitMQ `verdemart.events` (routing key: `stock.updated`, durable, persistent)
- [x] GET /health — reports events_published, events_failed
- [x] Structured logging with `[WMS-ADAPTER]` prefix
- [x] Dockerize
  - [x] Dockerfile
  - [x] Add to docker-compose.yml with `depends_on: rabbitmq (service_healthy)`

#### Docker Compose Updates
- [x] Add health checks for all services (erp-stub, wms-stub, wms-event-adapter, rabbitmq)
- [x] Configure `depends_on` with `service_healthy` conditions
  - [x] wms-event-adapter → rabbitmq
  - [x] wms-stub → wms-event-adapter
  - [x] dashboard → erp-stub, wms-stub
- [x] Add environment variable configuration (STOCK_WEBHOOK_URL, RABBITMQ_URL, DEFAULT_STOCK)
- [x] Document startup sequence (services/README.md — startup order + docker compose command)

---

### Week 3 - OSPOS & nopCommerce Integration

#### OSPOS (Open Source Point of Sale) - COMPLETE
- [x] Deploy OSPOS system
  - [x] Pull OSPOS Docker image (jekkos/opensourcepos)
  - [x] Configure MySQL database for OSPOS (ospos_mysql in docker-compose)
  - [x] Run OSPOS container
  - [x] Access OSPOS web interface
- [x] Configure OSPOS
  - [x] Create store location
  - [x] Create cashier user account
  - [x] Configure tax rates
  - [x] Sync product catalog with nopCommerce
    - [x] Export products from nopCommerce MSSQL
    - [x] Import to OSPOS via catalog sync script (commit 66cc015118)
- [x] Document OSPOS setup (docs/ospos-setup.md)
- [x] Dockerize
  - [x] Add OSPOS service to docker-compose.yml
  - [x] Configure volumes for persistence

#### OSPOS Integration Adapter - COMPLETE (`services/ospos-adapter/`)
**Note:** Adapter publishes `sale.completed` to RabbitMQ. Integration Service consumes this event and forwards to WMS/ERP.

- [x] Create `services/ospos-adapter/` project (.NET 10 worker service)
- [x] Implement sale polling mechanism
  - [x] Connect to OSPOS MySQL database
  - [x] Poll for new sales (OsposPollingService)
  - [x] Track last processed sale timestamp in SQLite
- [x] Transform sale data
  - [x] Map OSPOS sale format → `SaleCompletedEvent` model
  - [x] Extract: SKU, quantity, storeId, timestamp
  - [x] Generate EventId (UUID) for idempotency
- [x] Publish to RabbitMQ
  - [x] Publish `sale.completed` to `verdemart.events` exchange
  - [x] Routing key: `sale.completed`
- [x] Idempotency handling (IdempotencyTracker — SQLite /app/data/idempotency.db)
- [x] Error handling and logging
- [x] Dockerize
  - [x] Dockerfile (multi-stage .NET 10)
  - [x] Add to docker-compose.yml with ospos_mysql + rabbitmq dependencies
  - [x] Configure polling interval via POLLING_INTERVAL_SECONDS env var
  - [x] Volume mount for SQLite persistence

#### nopCommerce - Real Order Events - COMPLETE
- [x] Replace spike's `AppStartedEventConsumer` → `OrderPlacedEventConsumer`
- [x] Hook into order placement
  - [x] After order saved to DB
  - [x] Write IntegrationEvent to outbox
  - [x] Event type: `order.placed`
  - [x] Event data: orderId, customerId, items, total, timestamp
- [x] Update `SpikeOutboxPublisherTask` → `OutboxPublisherTask`
  - [x] Remove "Spike" prefix
  - [x] Production-ready error handling
  - [x] Configurable poll interval

#### nopCommerce - Stock Consumer - COMPLETE
**Note:** Consumer handles `stock.updated` from WMS (both web orders and POS sales flow through WMS).

- [x] Create `StockUpdateConsumerBackgroundService`
  - [x] Subscribe to RabbitMQ `stock.updated` routing key
  - [x] Deserialize event
  - [x] Call `ProductService.AdjustInventoryAsync()`
- [x] Cross-channel conflict resolution (StockUpdateHandler.cs)
  - [x] If stock becomes negative after any stock update
  - [x] Query recent pending web orders (last 60s)
  - [x] Cancel most recent web order(s) until stock ≥ 0
  - [x] Publish `order.cancelled` event
  - [ ] Send customer notification email
- [x] Register consumer in `IntegrationStartup.cs`

#### nopCommerce - Health Endpoint - COMPLETE
- [x] Add `/integration/health` endpoint (IntegrationHealthController.cs)
  - [x] Return: pending outbox count, last publish time, status
  - [x] Status codes: 200 (healthy), 503 (degraded)
- [x] Add controller to `Nop.Web`

---

### Week 4 - Observability & Testing

#### Observability Dashboard - MOSTLY COMPLETE
- [x] Create `services/dashboard/` project (Vite + React + Tailwind, port 8090)
- [x] Dark sidebar with pulsing live status dots per service
- [x] Full-width layout (sidebar + main content)
- [x] Poll endpoints every 3 seconds:
  - [x] GET /integration/health (nopCommerce) — endpoint exists; dashboard wiring may still need polling card
  - [x] GET /health (Integration Service) — card ready, shows "connecting…" until IS is up
- [ ] Display cards:
  - [x] WMS mode (normal/slow/down) — badge on WMS page
  - [x] ERP mode (normal/down) — badge on ERP page
  - [x] Duplicates Skipped (ERP + WMS) — idempotency guard counters
  - [x] Circuit breaker state (OPEN/HALF_OPEN/CLOSED) — polling wired, awaits IS
  - [x] Dead-letter queue depth — polling wired, awaits IS
  - [ ] Outbox pending count — needs wiring to `/integration/health`
  - [x] Last event published timestamp — shown when Integration Service is online
- [ ] Demo control buttons:
  - [x] Toggle WMS mode (normal/slow/down)
  - [x] Toggle ERP mode (normal/down)
  - [x] Reset State (ERP + WMS)
  - [ ] Simulate POS sale — pending OSPOS integration
  - [ ] Clear DLQ — pending Integration Service
- [x] Visual indicators (red/yellow/green for status, colour-coded metric cards)
- [x] Stock lookup
  - [ ] LOW STOCK / OUT OF STOCK labels
- [x] Dockerize and add to docker-compose.yml

#### Integration Tests
- [ ] Test: Outbox publishes to RabbitMQ
  - [ ] Place order → verify event in RabbitMQ within 10s
- [ ] Test: Circuit breaker lifecycle
  - [ ] Normal → WMS down → 3 failures → circuit OPEN
  - [ ] Verify DLQ accumulates messages
  - [ ] WMS up → circuit HALF_OPEN → test → CLOSED
  - [ ] Verify DLQ drains
- [ ] Test: Cross-channel conflict resolution
  - [ ] POS sells last unit + web order simultaneously
  - [ ] Verify: POS succeeds, web order cancelled
- [ ] Test: Idempotency
  - [ ] Publish same eventId twice
  - [ ] Verify: WMS called only once
- [ ] Test: ERP retry
  - [ ] ERP returns 500 → verify retry with backoff
  - [ ] ERP returns 200 on 3rd attempt → success
- [ ] Startup smoke test script
  - [ ] `docker-compose up -d`
  - [ ] Verify all health endpoints return 200
  - [ ] Verify startup event published

#### Demo Rehearsal
- [ ] Run full pressure point scenario:
  1. Normal operation: place order → verify in ERP + WMS
  2. Set WMS to `down` mode
  3. Place 5 orders → verify circuit opens, DLQ grows
  4. Observe dashboard: circuit OPEN, DLQ depth = 5
  5. Set WMS to `normal` mode
  6. Verify: circuit closes, DLQ drains within 60s
- [ ] Measure timings:
  - [ ] Time to circuit open (target: < 15s)
  - [ ] Time to drain DLQ (target: < 60s per QA-3)
- [ ] Take screenshots for evidence pack

---

### Week 5 - Documentation & Final Polish

#### Architecture Report - COMPLETE (docs/architecture-report.md, 876 lines)
- [x] Executive summary
- [x] Business drivers and QA scenarios
- [x] Current-state analysis
- [x] Bounded contexts and responsibilities
- [x] Target architecture (C4 diagrams)
- [x] Evolution path (step-by-step)
- [x] Design decisions (ADRs)
- [x] Cross-cutting concerns (observability, idempotency)
- [x] Scope decisions and justifications (stubs, POS, adapters)
- [ ] Evidence pack references (pending evidence pack)

#### ADR Updates
- [ ] Review all 4 ADRs for consistency
- [ ] Add consequences section if missing
- [ ] Update status if any decisions changed

#### Evidence Pack
- [ ] Screenshots:
  - [ ] RabbitMQ Management UI (verdemart.events exchange)
  - [ ] Observability dashboard (normal state)
  - [ ] Observability dashboard (WMS down, circuit OPEN)
  - [ ] Observability dashboard (recovery, DLQ draining)
  - [ ] Database query showing outbox events
  - [ ] Integration test results (all green)
- [ ] Log samples:
  - [ ] Order placed → outbox written
  - [ ] Event published to RabbitMQ
  - [ ] Circuit breaker opening
  - [ ] DLQ reconciliation
  - [ ] Cross-channel conflict resolution
- [ ] Video recording (optional):
  - [ ] 2-minute demo of pressure point scenario

#### Presentation Slides
- [ ] Title slide
- [ ] Scenario C overview
- [ ] Business drivers (3-5 bullets)
- [ ] QA scenarios (focus on QA-1 through QA-4)
- [ ] Architecture diagrams (C4 Context, Overview, Sequences)
- [ ] Top 3 architectural risks
- [ ] Key design decisions (ADR highlights)
- [ ] Live demo plan (or video backup)
- [ ] Tradeoffs and limitations (honest defense)
- [ ] Q&A preparation

#### Defense Preparation
- [ ] Anticipated questions:
  - [ ] Why RabbitMQ over Kafka? (ADR-001)
  - [ ] Why stubs for ERP/WMS but real OSPOS? (ADR-004 + bounded-contexts)
  - [ ] How does OSPOS integration work? (polling vs webhooks)
  - [ ] What happens if RabbitMQ goes down?
  - [ ] How do you handle message ordering?
  - [ ] Why POS priority over web orders?
  - [ ] What's the latency for cross-channel stock updates?
  - [ ] How would this scale to 100 stores with OSPOS?
  - [ ] Why not use OSPOS API instead of database polling?

---

## Repository & Collaboration

### Git Workflow
- [x] Merge spike branch to develop
- [x] Create feature branches:
  - [ ] `feature/integration-service`
  - [x] `feature/stubs` (ERP, WMS) — PR #4 merged
  - [x] `feature/nop-real-order-events` — PR #2 merged
  - [x] `feature/StockConsumer` — PR #3 merged
  - [x] `feature/observability-dashboard` — merged with stubs PR
- [x] Pull request review process (PRs #1–#4 merged)
- [ ] Final merge to main for submission

### Team Coordination
- [x] Assign owners to each service/component
- [ ] Shared evidence pack folder
- [ ] Demo rehearsal schedule

---

## Quick Reference - Success Criteria

### Part 1 Checkpoint (May 5-6)
- [x] Scenario chosen and justified
- [x] 3-5 QA scenarios documented
- [x] Target architecture with diagrams
- [x] At least 3 ADRs
- [x] Risk plan with feasibility spike

### Part 2 Final Delivery (June 2-3)
- [ ] Runnable implementation (all services dockerized)
- [ ] Architecture report (comprehensive documentation)
- [ ] Updated ADRs (4+)
- [ ] Evidence pack (screenshots, logs, tests)
- [ ] Live demo showing degraded + recovering behavior
- [ ] Defense of tradeoffs and limits

### Demo Must Show
- [ ] Normal operation (order flows through all systems)
- [ ] Degraded dependency (WMS down)
- [ ] Observable system behavior (dashboard shows circuit OPEN)
- [ ] Recovery path (WMS up, DLQ drains, circuit CLOSED)

---

## Notes

- **Spike complete:** Outbox pattern proven viable (May 4, 2026)
- **Critical path:** Integration Service + WMS circuit breaker (highest risk for demo)
- **Time estimate:** ~80-100 hours total (4-5 weeks with 20h/week)
- **Repository access needed:** Cannot push spike branch yet
