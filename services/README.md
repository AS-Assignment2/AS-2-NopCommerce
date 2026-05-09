# Services — VerdeMart External System Stubs

This folder contains the independently deployable stub services and (in future weeks) the Order Integration Service and Observability Dashboard that together form the integration layer around nopCommerce.

---

## What Is Implemented

| Service | Folder | Port | Status |
|---------|--------|------|--------|
| ERP Stub | `erp-stub/` | 8001 | ✅ Done |
| WMS Stub | `wms-stub/` | 8002 | ✅ Done |
| Order Integration Service | `order-integration-service/` | 8080 | TODO|
| Observability Dashboard | `dashboard/` | 8090 | TODO |

### ERP Stub

Simulates the back-office ERP system that receives orders from the Order Integration Service. Stores all accepted orders in memory and supports switching to a failure mode to trigger the ERP retry scenario (QA-5).

**Endpoints:**

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/orders` | Accept an order. Returns `503` when mode is `down`. |
| `GET` | `/orders` | List all orders received so far. |
| `POST` | `/admin/mode` | Switch mode: `normal` or `down`. |
| `GET` | `/health` | Health check — always `200`, reports mode and order count. |

**Modes:**

| Mode | Behaviour |
|------|-----------|
| `normal` | Accepts orders, stores them, returns `{"status":"accepted","erpRef":"erp-{orderId}"}` |
| `down` | Returns `503 Service Unavailable` on `POST /orders` — triggers the Polly retry in the Integration Service |

### WMS Stub

Simulates the Warehouse Management System that receives stock reservation requests. Maintains an in-memory stock ledger per product. On successful reservation it fires an asynchronous webhook to the Integration Service, which then publishes the `stock.updated` event to RabbitMQ. Supports three failure modes to drive the circuit breaker scenario (QA-1, QA-3).

**Endpoints:**

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/reservations` | Reserve stock for an order. Returns `503` (down) or delays 10 s (slow). |
| `GET` | `/stock/{productId}` | Query current stock for a product. |
| `POST` | `/admin/mode` | Switch mode: `normal`, `slow`, or `down`. |
| `GET` | `/health` | Health check — always `200`, reports mode and reservation count. |

**Modes:**

| Mode | Behaviour |
|------|-----------|
| `normal` | Reserves stock, fires webhook, returns `{"status":"reserved","reservationId":"..."}` |
| `slow` | Waits **10 seconds** before responding — triggers circuit breaker timeout detection |
| `down` | Returns `503 Service Unavailable` — triggers circuit breaker failure counting |

---

## How It Is Implemented

### ERP Stub (`erp-stub/main.py`)

Pure in-memory HTTP service. Global `mode` variable controls rejection behaviour. All endpoints are synchronous (no I/O beyond in-process state). The `/health` and `/admin/mode` endpoints are always available regardless of mode — only the business endpoint `/orders` is affected.

```
Request → mode check → append to orders list → return erpRef
                ↓ (if mode=down)
            HTTP 503
```

### WMS Stub (`wms-stub/main.py`)

Async HTTP service. Global `mode` and `stock` dictionary (productId → quantity). Unknown products default to `DEFAULT_STOCK` (env var, default 100). Stock quantity never goes below 0 (`max(0, current - quantity)`).

The webhook is dispatched using FastAPI's `BackgroundTasks` — the reservation response is returned immediately to the Integration Service, and the webhook call happens afterwards in the background. This means a webhook failure never blocks or fails the reservation.

```
Request → mode check → (optional 10s sleep) → decrement stock →
         schedule webhook (BackgroundTask) → return reservationId
                ↓ (background, after response)
         POST {STOCK_WEBHOOK_URL} with stock.updated payload
         (errors caught and logged, never propagated)
```

**Webhook payload** sent to `STOCK_WEBHOOK_URL` per reserved item:

```json
{
  "eventId": "<uuid>",
  "productId": 789,
  "warehouseId": 1,
  "newStockQuantity": 97,
  "occurredAt": "2026-05-09T10:00:05Z"
}
```

If `STOCK_WEBHOOK_URL` is not set, the webhook step is skipped with a warning log — this allows the WMS stub to run standalone during development without the Integration Service being up.

---

## Tools Used

| Tool | Version | Purpose |
|------|---------|---------|
| Python | 3.12 | Runtime |
| FastAPI | 0.115.0 | HTTP framework — request validation, async support, auto docs at `/docs` |
| Uvicorn | 0.30.0 | ASGI server that runs FastAPI |
| Pydantic | (bundled with FastAPI) | Request/response model validation |
| httpx | 0.27.0 | Async HTTP client used by WMS stub to call the webhook |
| Docker | — | Container packaging |

Both stubs are built from `python:3.12-slim` for a minimal image footprint.

---

## How to Run

### Option A — Docker Compose (recommended)

Run only the stubs (no need for the full stack):

```bash
docker compose up --build erp-stub wms-stub
```

Both services will be available on `localhost:8001` and `localhost:8002`.

To run the full stack when all services are ready:

```bash
docker compose up --build
```

### Option B — Local Python (no Docker)

```bash
# Create a virtual environment
python3 -m venv .venv
source .venv/bin/activate
pip install fastapi uvicorn httpx

# Terminal 1 — ERP Stub
cd services/erp-stub
uvicorn main:app --port 8001 --reload

# Terminal 2 — WMS Stub
cd services/wms-stub
STOCK_WEBHOOK_URL=http://localhost:8080/webhooks/stock-changed \
uvicorn main:app --port 8002 --reload
```

### Interactive API docs

Once running, FastAPI generates interactive documentation automatically:

- ERP Stub: http://localhost:8001/docs
- WMS Stub: http://localhost:8002/docs

---

## How to Test

All tests below use `curl`. Replace `localhost` with the container name when testing inside Docker network.

### ERP Stub

```bash
# 1. Health check
curl http://localhost:8001/health
# → {"status":"ok","mode":"normal","orders_received":0}

# 2. Submit an order (happy path)
curl -X POST http://localhost:8001/orders \
  -H "Content-Type: application/json" \
  -d '{
    "eventId": "evt-001",
    "orderId": 1,
    "customerId": 42,
    "items": [{"productId": 789, "sku": "ABC", "quantity": 2}],
    "totalAmount": 49.99,
    "occurredAt": "2026-05-09T10:00:00Z"
  }'
# → {"status":"accepted","erpRef":"erp-1"}

# 3. Verify it was stored
curl http://localhost:8001/orders
# → {"count":1,"orders":[...]}

# 4. Trigger failure mode
curl -X POST http://localhost:8001/admin/mode \
  -H "Content-Type: application/json" \
  -d '{"mode":"down"}'
# → {"mode":"down"}

# 5. Confirm orders are rejected (should return 503)
curl -X POST http://localhost:8001/orders \
  -H "Content-Type: application/json" \
  -d '{"eventId":"evt-002","orderId":2,"customerId":42,"items":[{"productId":789,"sku":"ABC","quantity":1}],"totalAmount":24.99,"occurredAt":"2026-05-09T10:01:00Z"}'
# → HTTP 503 {"detail":"ERP unavailable"}

# 6. Restore
curl -X POST http://localhost:8001/admin/mode \
  -H "Content-Type: application/json" \
  -d '{"mode":"normal"}'
```

### WMS Stub

```bash
# 1. Health check
curl http://localhost:8002/health
# → {"status":"ok","mode":"normal","reservations_processed":0}

# 2. Check initial stock (unknown product defaults to 100)
curl http://localhost:8002/stock/789
# → {"productId":789,"quantity":100}

# 3. Reserve stock (happy path)
curl -X POST http://localhost:8002/reservations \
  -H "Content-Type: application/json" \
  -d '{"orderId": 1, "items": [{"productId": 789, "quantity": 3}]}'
# → {"status":"reserved","reservationId":"<uuid>"}

# 4. Verify stock decreased
curl http://localhost:8002/stock/789
# → {"productId":789,"quantity":97}

# 5. Trigger down mode (circuit breaker scenario)
curl -X POST http://localhost:8002/admin/mode \
  -H "Content-Type: application/json" \
  -d '{"mode":"down"}'

curl -X POST http://localhost:8002/reservations \
  -H "Content-Type: application/json" \
  -d '{"orderId": 2, "items": [{"productId": 789, "quantity": 1}]}'
# → HTTP 503 {"detail":"WMS unavailable"}

# 6. Trigger slow mode (timeout scenario — takes 10 seconds)
curl -X POST http://localhost:8002/admin/mode \
  -H "Content-Type: application/json" \
  -d '{"mode":"slow"}'

curl -X POST http://localhost:8002/reservations \
  -H "Content-Type: application/json" \
  -d '{"orderId": 3, "items": [{"productId": 789, "quantity": 1}]}'
# → responds after ~10s → {"status":"reserved","reservationId":"<uuid>"}

# 7. Restore to normal
curl -X POST http://localhost:8002/admin/mode \
  -H "Content-Type: application/json" \
  -d '{"mode":"normal"}'
```

### Demo Pressure Point Script

This is the sequence used to drive the full WMS degradation → recovery demo scenario:

```bash
# Step 1 — confirm normal operation
curl -X POST http://localhost:8002/reservations \
  -H "Content-Type: application/json" \
  -d '{"orderId":10,"items":[{"productId":1,"quantity":1}]}'

# Step 2 — bring WMS down
curl -X POST http://localhost:8002/admin/mode -H "Content-Type: application/json" -d '{"mode":"down"}'

# Step 3 — place 5 orders (Integration Service will hit circuit breaker, route to DLQ)
for i in 1 2 3 4 5; do
  curl -s http://localhost:8001/orders -X POST \
    -H "Content-Type: application/json" \
    -d "{\"eventId\":\"evt-$i\",\"orderId\":$i,\"customerId\":1,\"items\":[{\"productId\":1,\"sku\":\"X\",\"quantity\":1}],\"totalAmount\":9.99,\"occurredAt\":\"2026-05-09T10:0$i:00Z\"}"
done
# Dashboard should show: circuit=OPEN, dlq_depth=5

# Step 4 — restore WMS
curl -X POST http://localhost:8002/admin/mode -H "Content-Type: application/json" -d '{"mode":"normal"}'
# Dashboard should show: circuit=CLOSED, dlq_depth=0 (within 60s)
```

---

## System Flow Diagram

### Happy Path — Order Placed End-to-End

```mermaid
sequenceDiagram
    participant NOP as nopCommerce
    participant RMQ as RabbitMQ
    participant IS  as Integration Service
    participant ERP as ERP Stub :8001
    participant WMS as WMS Stub :8002

    NOP->>RMQ: order.placed (outbox, same DB tx)
    RMQ->>IS: order.placed
    IS->>ERP: POST /orders
    ERP-->>IS: 200 {"status":"accepted","erpRef":"erp-123"}
    IS->>WMS: POST /reservations
    WMS-->>IS: 200 {"status":"reserved","reservationId":"..."}
    Note over WMS: BackgroundTask fires after response
    WMS->>IS: POST /webhooks/stock-changed
    IS->>RMQ: stock.updated
    RMQ->>NOP: stock.updated
    Note over NOP: AdjustInventoryAsync()
```

### Pressure Point — WMS Degradation → Recovery

```mermaid
sequenceDiagram
    participant IS  as Integration Service
    participant WMS as WMS Stub
    participant DLQ as RabbitMQ DLQ

    Note over WMS: admin sets mode=down

    IS->>WMS: POST /reservations
    WMS-->>IS: 503 (failure 1)
    IS->>WMS: POST /reservations
    WMS-->>IS: 503 (failure 2)
    IS->>WMS: POST /reservations
    WMS-->>IS: 503 (failure 3)
    Note over IS: circuit breaker OPEN

    IS->>DLQ: route to DLQ (message 1)
    IS->>DLQ: route to DLQ (message 2)
    IS->>DLQ: route to DLQ (message 3)

    Note over WMS: admin sets mode=normal

    IS->>WMS: POST /reservations (HALF-OPEN probe)
    WMS-->>IS: 200 reserved
    Note over IS: circuit breaker CLOSED

    DLQ->>IS: reconciliation loop drains DLQ
    IS->>WMS: POST /reservations (queued message 1)
    WMS-->>IS: 200 reserved
    IS->>WMS: POST /reservations (queued message 2)
    WMS-->>IS: 200 reserved
    IS->>WMS: POST /reservations (queued message 3)
    WMS-->>IS: 200 reserved
```

---

## What Must Be Implemented for a Complete Flow

The two stubs are ready and independently testable. The following components are needed to complete the end-to-end integration:

### 1. Order Integration Service — `services/order-integration-service/`

This is the central connector. Without it, orders from nopCommerce never reach the stubs.

- **RabbitMQ consumer**: subscribe to `verdemart.events` exchange, `order.placed` routing key
- **ERP Adapter**: `POST http://erp-stub:8001/orders` with the `order.placed` payload + Polly exponential backoff (3 retries)
- **WMS Adapter**: `POST http://wms-stub:8002/reservations` + Polly circuit breaker (3 failures → OPEN, 30 s reset timeout)
- **Dead-letter routing**: when circuit is OPEN, push message to `verdemart.dead-letter` queue instead
- **Reconciliation loop**: background service that polls the DLQ and retries when circuit is HALF-OPEN
- **Webhook endpoint**: `POST /webhooks/stock-changed` — receives the WMS stub's webhook call, publishes `stock.updated` to RabbitMQ `verdemart.events`
- **Health endpoint**: `GET /health` — returns circuit state, DLQ depth, last processed event (consumed by Dashboard)

**Contract the stubs expect from the Integration Service:**

`POST http://erp-stub:8001/orders` body:
```json
{
  "eventId": "uuid",
  "orderId": 123,
  "customerId": 456,
  "items": [{ "productId": 789, "sku": "ABC", "quantity": 2 }],
  "totalAmount": 49.99,
  "occurredAt": "2026-05-09T14:00:00Z"
}
```

`POST http://wms-stub:8002/reservations` body:
```json
{
  "orderId": 123,
  "items": [{ "productId": 789, "quantity": 2 }]
}
```

### 2. nopCommerce — Outbox Integration

Without this, no `order.placed` events flow into RabbitMQ.

- Replace `AppStartedEventConsumer` (spike test) with a real hook inside `OrderProcessingService.PlaceOrderAsync()` that writes an `IntegrationEvent` row in the same DB transaction
- Rename `SpikeOutboxPublisherTask` → `OutboxPublisherTask`, make poll interval configurable in `appsettings.json`
- The outbox row must use the `order.placed` routing key and the contract JSON above

### 3. nopCommerce — Stock Update Consumer

Without this, stock levels in nopCommerce are never updated after WMS confirms a reservation.

- New background service subscribing to RabbitMQ `stock.updated` routing key
- Calls `ProductService.AdjustInventoryAsync()` with the new quantity from the webhook payload
- Register in `IntegrationStartup.cs`

### 4. Observability Dashboard — `services/dashboard/` 

Without this, the pressure point is invisible during the demo.

- Single-page HTML/JS that polls every 2 s:
  - `GET http://order-integration-service:8080/health` → circuit state, DLQ depth
  - `GET http://nopcommerce/integration/health` → outbox pending count
- Demo control buttons wired to `POST /admin/mode` on both stubs
- Visual colour indicators (green/yellow/red)

---

## Environment Variables Reference

### WMS Stub

| Variable | Default | Description |
|----------|---------|-------------|
| `DEFAULT_STOCK` | `100` | Starting stock quantity for any productId not yet seen |
| `STOCK_WEBHOOK_URL` | *(unset)* | Full URL to call after each reservation, e.g. `http://order-integration-service:8080/webhooks/stock-changed`. If unset, webhook is skipped. |

### ERP Stub

No environment variables — all configuration is via the `/admin/mode` endpoint.

---

## Implementation Checklist

### ERP Stub

- [x] `services/erp-stub/` folder created
- [x] `POST /orders` — accepts order, stores in memory, returns `erpRef`
- [x] `GET /orders` — returns all stored orders for verification
- [x] `POST /admin/mode` — switches between `normal` and `down`
- [x] `GET /health` — always `200`, reports mode and order count
- [x] Mode `down` returns `503` on business endpoints only
- [x] Request validated against `order.placed` contract (Pydantic model)
- [x] Structured logging with `[ERP]` prefix
- [x] `Dockerfile` (python:3.12-slim, port 8001)
- [x] `requirements.txt` (fastapi, uvicorn)
- [x] Added to `docker-compose.yml` with health check

### WMS Stub

- [x] `services/wms-stub/` folder created
- [x] `POST /reservations` — reserves stock per item, fires webhook, returns `reservationId`
- [x] `GET /stock/{productId}` — returns current quantity (defaults to `DEFAULT_STOCK`)
- [x] `POST /admin/mode` — switches between `normal`, `slow`, and `down`
- [x] `GET /health` — always `200`, reports mode and reservation count
- [x] Mode `down` returns `503` on business endpoints only
- [x] Mode `slow` applies 10 s delay before processing
- [x] Stock ledger — per-product quantity, never goes below 0
- [x] Unknown products default to `DEFAULT_STOCK` (env var)
- [x] Webhook dispatched via `BackgroundTasks` (non-blocking, fire-and-forget)
- [x] Webhook errors caught and logged — never fail the reservation response
- [x] `STOCK_WEBHOOK_URL` configurable via env var; skipped gracefully if unset
- [x] Webhook payload matches `stock.updated` contract
- [x] Structured logging with `[WMS]` prefix
- [x] `Dockerfile` (python:3.12-slim, port 8002)
- [x] `requirements.txt` (fastapi, uvicorn, httpx)
- [x] Added to `docker-compose.yml` with health check and env vars

### Docker Compose

- [x] `erp-stub` service — build, port 8001, health check
- [x] `wms-stub` service — build, port 8002, health check, env vars
- [x] RabbitMQ health check added
- [ ] `order-integration-service` — pending
- [ ] `depends_on: service_healthy` conditions — pending service
- [ ] `dashboard` service — pending Week 4
