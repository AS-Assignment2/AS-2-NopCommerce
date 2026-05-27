import logging
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

logging.basicConfig(level=logging.INFO, format="%(asctime)s [ERP] %(levelname)s %(message)s")
log = logging.getLogger(__name__)

app = FastAPI(title="ERP Stub", description="VerdeMart ERP stub — accepts orders, supports failure injection")
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])

# --- State ---
mode: str = "normal"
orders: list[dict] = []
processed_events: set[str] = set()       # eventId deduplication
processed_event_refs: dict[str, str] = {}  # eventId → erpRef
duplicates_skipped: int = 0


# --- Models ---
class OrderItem(BaseModel):
    productId: int
    sku: str
    quantity: int


class OrderRequest(BaseModel):
    eventId: str
    orderId: int
    customerId: int
    items: list[OrderItem]
    totalAmount: float
    occurredAt: str


class ModeRequest(BaseModel):
    mode: str


# --- Endpoints ---
@app.post("/orders", status_code=200)
def receive_order(body: OrderRequest):
    global duplicates_skipped
    if mode == "down":
        log.warning("mode=down — rejecting POST /orders for orderId=%s", body.orderId)
        raise HTTPException(status_code=503, detail="ERP unavailable")

    # Idempotency: same eventId already processed → return original response
    if body.eventId in processed_events:
        erp_ref = processed_event_refs[body.eventId]
        duplicates_skipped += 1
        log.info("Duplicate eventId=%s — returning cached erpRef=%s (total skipped=%d)", body.eventId, erp_ref, duplicates_skipped)
        return {"status": "accepted", "erpRef": erp_ref}

    erp_ref = f"erp-{body.orderId}"
    orders.append(body.model_dump())
    processed_events.add(body.eventId)
    processed_event_refs[body.eventId] = erp_ref
    log.info("Order accepted: orderId=%s eventId=%s items=%d", body.orderId, body.eventId, len(body.items))
    return {"status": "accepted", "erpRef": erp_ref}


@app.get("/orders")
def list_orders():
    return {"count": len(orders), "orders": orders}


@app.post("/admin/mode")
def set_mode(body: ModeRequest):
    global mode
    allowed = {"normal", "down"}
    if body.mode not in allowed:
        raise HTTPException(status_code=400, detail=f"mode must be one of {allowed}")
    mode = body.mode
    log.info("Mode changed to: %s", mode)
    return {"mode": mode}


@app.post("/admin/reset")
def reset():
    global mode, orders, processed_events, processed_event_refs, duplicates_skipped
    mode = "normal"
    orders.clear()
    processed_events.clear()
    processed_event_refs.clear()
    duplicates_skipped = 0
    log.info("State reset")
    return {"status": "reset"}


@app.get("/health")
def health():
    return {
        "status": "ok",
        "mode": mode,
        "orders_received": len(orders),
        "duplicates_skipped": duplicates_skipped,
    }
