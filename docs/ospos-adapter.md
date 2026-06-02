# OSPOS Adapter

The OSPOS Adapter is a .NET 10 worker that polls the OSPOS MySQL database for completed sales and publishes `sale.completed` events to RabbitMQ on the `verdemart.events` topic exchange. The container exposes no ports.

## Event Flow

```
OSPOS sale (MySQL) -> ospos_adapter (poll) -> RabbitMQ verdemart.events
                                              routing key: sale.completed
```

The downstream consumer is the Order Integration Service `sale.completed` handler and is out of scope for this service.

## Configuration

All settings are environment variables, set in `docker-compose.yml`.

| Variable | Default | Description |
|---|---|---|
| `OSPOS_DB_HOST` | `ospos_mysql` | MySQL host |
| `OSPOS_DB_PORT` | `3306` | MySQL port |
| `OSPOS_DB_NAME` | `ospos` | Database name |
| `OSPOS_DB_USER` | `root` | DB user |
| `OSPOS_DB_PASSWORD` | `ospospass` | DB password |
| `RABBITMQ_HOST` | `rabbitmq` | RabbitMQ host |
| `RABBITMQ_PORT` | `5672` | AMQP port |
| `RABBITMQ_USERNAME` | `guest` | Broker user |
| `RABBITMQ_PASSWORD` | `guest` | Broker password |
| `RABBITMQ_EXCHANGE` | `verdemart.events` | Topic exchange name |
| `POLLING_INTERVAL_SECONDS` | `30` | Seconds between polling cycles |
| `IDEMPOTENCY_DB_PATH` | `/app/data/idempotency.db` | SQLite file path |

The SQLite database is persisted via the `ospos_adapter_data` Docker volume.

## Event Schema

```json
{
  "EventId": "guid",
  "EventType": "sale.completed",
  "Timestamp": "2026-05-27T14:52:15Z",
  "Items": [
    { "Sku": "WIDGET-001", "Quantity": 3.0, "StoreId": "main" }
  ]
}
```

## Idempotency

Two SQLite tables back the watermark and de-duplication strategy:

- `LastProcessedTime` - high-watermark used in the SQL `WHERE sale_time > ?` predicate
- `ProcessedSales` - set of already-published `sale_id` values

A sale is published only if its id is absent from `ProcessedSales`. Both tables are updated only after `PublishAsync` succeeds, so a RabbitMQ failure leaves the sale eligible for retry on the next polling cycle.

## Failure Handling

The polling loop wraps each cycle in `try/catch` and logs failures without exiting. Transient MySQL or RabbitMQ errors are retried implicitly by the next cycle. No exponential backoff is configured; the 30s polling cadence acts as the retry window.

## Operations

```bash
# Build and start
docker-compose up -d --build ospos_adapter

# Tail logs
docker logs -f ospos_adapter

# Inspect idempotency state
docker run --rm -v as-2-nopcommerce_ospos_adapter_data:/data alpine \
  sh -c "apk add sqlite >/dev/null && sqlite3 /data/idempotency.db 'SELECT * FROM ProcessedSales; SELECT * FROM LastProcessedTime;'"

# Reset idempotency state (forces re-publish of historical sales)
docker-compose down ospos_adapter
docker volume rm as-2-nopcommerce_ospos_adapter_data
docker-compose up -d ospos_adapter
```

## Verification

Insert a test sale and watch the message appear:

```bash
docker exec ospos_mysql mysql -uroot -pospospass ospos -e "
INSERT INTO ospos_items (item_id, name, item_number) VALUES (1, 'Test Widget', 'WIDGET-001')
  ON DUPLICATE KEY UPDATE name=VALUES(name);
INSERT INTO ospos_sales (sale_id, sale_time, sale_status) VALUES (2000, NOW(), 'COMPLETED');
INSERT INTO ospos_sales_items (sale_id, item_id, line, quantity_purchased, item_cost_price, item_unit_price)
  VALUES (2000, 1, 1, 3.000, 1.00, 2.00);
"

# Within 30 seconds, message count increments
curl -s -u guest:guest http://localhost:15672/api/exchanges/%2F/verdemart.events | jq .message_stats
```
