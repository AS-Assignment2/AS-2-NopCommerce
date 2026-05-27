import json
import logging
import os

import pika
import pika.exceptions
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

logging.basicConfig(level=logging.INFO, format="%(asctime)s [WMS-ADAPTER] %(levelname)s %(message)s")
log = logging.getLogger(__name__)

app = FastAPI(
    title="WMS Event Adapter",
    description="Receives WMS webhook calls and publishes stock.updated to RabbitMQ. "
                "Temporary bridge until the Order Integration Service implements this endpoint.",
)
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])

RABBITMQ_URL: str = os.getenv("RABBITMQ_URL", "amqp://guest:guest@localhost:5672/")
EXCHANGE: str = "verdemart.events"

# --- State ---
events_published: list[dict] = []
events_failed: int = 0


# --- Models ---
class StockChangedPayload(BaseModel):
    eventId: str
    productId: int
    warehouseId: int
    newStockQuantity: int
    occurredAt: str


# --- Helpers ---
def _publish_to_rabbitmq(payload: dict) -> None:
    params = pika.URLParameters(RABBITMQ_URL)
    connection = pika.BlockingConnection(params)
    try:
        channel = connection.channel()
        channel.exchange_declare(exchange=EXCHANGE, exchange_type="topic", durable=True)
        channel.basic_publish(
            exchange=EXCHANGE,
            routing_key="stock.updated",
            body=json.dumps(payload),
            properties=pika.BasicProperties(
                content_type="application/json",
                delivery_mode=2,  # persistent message
            ),
        )
        log.info(
            "Published stock.updated → exchange=%s productId=%d newQty=%d eventId=%s",
            EXCHANGE,
            payload["productId"],
            payload["newStockQuantity"],
            payload["eventId"],
        )
    finally:
        connection.close()


# --- Endpoints ---
@app.post("/webhooks/stock-changed", status_code=200)
def stock_changed(body: StockChangedPayload):
    global events_failed
    payload = body.model_dump()
    try:
        _publish_to_rabbitmq(payload)
        events_published.append(payload)
        return {"status": "published", "eventId": body.eventId, "routingKey": "stock.updated"}
    except pika.exceptions.AMQPConnectionError as exc:
        events_failed += 1
        log.error("RabbitMQ connection failed (eventId=%s): %s", body.eventId, exc)
        raise HTTPException(status_code=503, detail=f"RabbitMQ unavailable: {exc}")
    except Exception as exc:
        events_failed += 1
        log.error("Unexpected error publishing eventId=%s: %s", body.eventId, exc)
        raise HTTPException(status_code=500, detail=str(exc))


@app.get("/health")
def health():
    return {
        "status": "ok",
        "rabbitmq_url": RABBITMQ_URL.split("@")[-1],  # strip credentials from logs
        "events_published": len(events_published),
        "events_failed": events_failed,
    }
