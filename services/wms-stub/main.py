import asyncio
import logging
import os
from datetime import datetime, timezone
from uuid import uuid4

import httpx
from fastapi import BackgroundTasks, FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

logging.basicConfig(level=logging.INFO, format="%(asctime)s [WMS] %(levelname)s %(message)s")
log = logging.getLogger(__name__)

app = FastAPI(title="WMS Stub", description="VerdeMart WMS stub — handles reservations, supports failure injection")
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])

DEFAULT_STOCK: int = int(os.getenv("DEFAULT_STOCK", "100"))
STOCK_WEBHOOK_URL: str | None = os.getenv("STOCK_WEBHOOK_URL")

if STOCK_WEBHOOK_URL:
    log.info("Webhook configured: %s", STOCK_WEBHOOK_URL)
else:
    log.warning("STOCK_WEBHOOK_URL not set — stock.updated notifications will be skipped")

# --- State ---
mode: str = "normal"
stock: dict[int, int] = {}
reservations: list[dict] = []
processed_orders: set[int] = set()        # orderId deduplication
processed_order_refs: dict[int, str] = {}  # orderId → reservationId
duplicates_skipped: int = 0


# --- Models ---
class ReservationItem(BaseModel):
    productId: int
    quantity: int


class ReservationRequest(BaseModel):
    orderId: int
    items: list[ReservationItem]


class ModeRequest(BaseModel):
    mode: str


# --- Helpers ---
async def _call_webhook(product_id: int, new_qty: int) -> None:
    if not STOCK_WEBHOOK_URL:
        return
    payload = {
        "eventId": str(uuid4()),
        "productId": product_id,
        "warehouseId": 1,
        "newStockQuantity": new_qty,
        "occurredAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    try:
        async with httpx.AsyncClient(timeout=5.0) as client:
            resp = await client.post(STOCK_WEBHOOK_URL, json=payload)
            log.info("Webhook → %s status=%d productId=%d newQty=%d", STOCK_WEBHOOK_URL, resp.status_code, product_id, new_qty)
    except Exception as exc:
        log.warning("Webhook call failed (productId=%d): %s", product_id, exc)


# --- Endpoints ---
@app.post("/reservations", status_code=200)
async def reserve(body: ReservationRequest, background_tasks: BackgroundTasks):
    global duplicates_skipped
    if mode == "down":
        log.warning("mode=down — rejecting POST /reservations for orderId=%s", body.orderId)
        raise HTTPException(status_code=503, detail="WMS unavailable")

    # Idempotency: same orderId already processed → return original reservationId immediately
    if body.orderId in processed_orders:
        reservation_id = processed_order_refs[body.orderId]
        duplicates_skipped += 1
        log.info("Duplicate orderId=%d — returning cached reservationId=%s (total skipped=%d)", body.orderId, reservation_id, duplicates_skipped)
        return {"status": "reserved", "reservationId": reservation_id}

    if mode == "slow":
        log.info("mode=slow — applying 10s delay for orderId=%s", body.orderId)
        await asyncio.sleep(10)

    reservation_id = str(uuid4())
    updated_stock: dict[int, int] = {}

    for item in body.items:
        current = stock.get(item.productId, DEFAULT_STOCK)
        new_qty = max(0, current - item.quantity)
        stock[item.productId] = new_qty
        updated_stock[item.productId] = new_qty
        log.info("Reserved productId=%d qty=%d → stock %d→%d", item.productId, item.quantity, current, new_qty)
        background_tasks.add_task(_call_webhook, item.productId, new_qty)

    reservations.append({"reservationId": reservation_id, "orderId": body.orderId, "stock": updated_stock})
    processed_orders.add(body.orderId)
    processed_order_refs[body.orderId] = reservation_id
    return {"status": "reserved", "reservationId": reservation_id}


@app.get("/reservations")
def list_reservations():
    return {"count": len(reservations), "reservations": reservations}


@app.get("/stock/{product_id}")
def get_stock(product_id: int):
    qty = stock.get(product_id, DEFAULT_STOCK)
    return {"productId": product_id, "quantity": qty}


@app.post("/admin/mode")
def set_mode(body: ModeRequest):
    global mode
    allowed = {"normal", "slow", "down"}
    if body.mode not in allowed:
        raise HTTPException(status_code=400, detail=f"mode must be one of {allowed}")
    mode = body.mode
    log.info("Mode changed to: %s", mode)
    return {"mode": mode}


@app.post("/admin/reset")
def reset():
    global mode, stock, reservations, processed_orders, processed_order_refs, duplicates_skipped
    mode = "normal"
    stock.clear()
    reservations.clear()
    processed_orders.clear()
    processed_order_refs.clear()
    duplicates_skipped = 0
    log.info("State reset")
    return {"status": "reset"}


@app.get("/health")
def health():
    return {
        "status": "ok",
        "mode": mode,
        "reservations_processed": len(reservations),
        "duplicates_skipped": duplicates_skipped,
    }
