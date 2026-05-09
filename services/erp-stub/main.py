import logging
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

logging.basicConfig(level=logging.INFO, format="%(asctime)s [ERP] %(levelname)s %(message)s")
log = logging.getLogger(__name__)

app = FastAPI(title="ERP Stub", description="VerdeMart ERP stub — accepts orders, supports failure injection")

# --- State ---
mode: str = "normal"
orders: list[dict] = []


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
    if mode == "down":
        log.warning("mode=down — rejecting POST /orders for orderId=%s", body.orderId)
        raise HTTPException(status_code=503, detail="ERP unavailable")

    record = body.model_dump()
    orders.append(record)
    log.info("Order accepted: orderId=%s eventId=%s items=%d", body.orderId, body.eventId, len(body.items))
    return {"status": "accepted", "erpRef": f"erp-{body.orderId}"}


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


@app.get("/health")
def health():
    return {"status": "ok", "mode": mode, "orders_received": len(orders)}
