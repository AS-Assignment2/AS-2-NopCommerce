#!/usr/bin/env bash
# VerdeMart pressure-point demo driver.
# Usage:
#   ./scripts/demo.sh order [N]   # push N order.placed events through the live pipeline (default 1)
#   ./scripts/demo.sh down        # set WMS to DOWN (start the outage)
#   ./scripts/demo.sh normal      # set WMS back to NORMAL (trigger recovery)
#   ./scripts/demo.sh status      # print IS health + DLQ + ERP/WMS counts
set -euo pipefail

MSSQL=nopcommerce_mssql_server
PW='nopCommerce_db_password'
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
IS=http://localhost:8083
WMS=http://localhost:8002
ERP=http://localhost:8001

inject_order() {
  local oid="$1"
  local eid; eid=$(python3 -c "import uuid;print(uuid.uuid4())")
  docker exec "$MSSQL" "$SQLCMD" -S localhost -U sa -P "$PW" -C -d nopcommerce -Q \
    "INSERT INTO IntegrationEvent (EventType, EventData, CreatedOnUtc, Published, PublishAttempts)
     VALUES ('order.placed','{\"eventId\":\"$eid\",\"orderId\":$oid,\"customerId\":11,\"storeId\":1,\"total\":1400.00,\"currency\":\"USD\",\"createdOnUtc\":\"2026-06-01T19:00:00Z\",\"items\":[{\"productId\":1,\"quantity\":1,\"unitPrice\":1400.00}]}',GETUTCDATE(),0,0);" \
    >/dev/null 2>&1
  echo "  injected order.placed orderId=$oid eventId=$eid"
}

cmd=${1:-status}
case "$cmd" in
  order)
    n=${2:-1}; base=$(( (RANDOM % 9000) + 2000 ))
    echo "Pushing $n order.placed event(s) through outbox -> RabbitMQ -> Integration Service ..."
    for i in $(seq 1 "$n"); do inject_order $((base + i)); done
    echo "Done. Outbox publisher fires every ~10s; watch '$0 status'." ;;
  down)
    echo "WMS -> DOWN"; curl -s -X POST "$WMS/admin/mode" -H 'Content-Type: application/json' -d '{"mode":"down"}'; echo ;;
  normal)
    echo "WMS -> NORMAL (reconciliation drains DLQ every 5s)"; curl -s -X POST "$WMS/admin/mode" -H 'Content-Type: application/json' -d '{"mode":"normal"}'; echo ;;
  status)
    echo "== Integration Service /health =="; curl -s "$IS/health"; echo
    echo "== Integration Service /dlq =="; curl -s "$IS/dlq"; echo
    echo "== ERP orders =="; curl -s "$ERP/orders" | python3 -c "import sys,json;d=json.load(sys.stdin);print('count=',d['count'])" 2>/dev/null || true
    echo "== WMS reservations =="; curl -s "$WMS/reservations" | python3 -c "import sys,json;d=json.load(sys.stdin);print('count=',d['count'])" 2>/dev/null || true
    echo "== nopCommerce outbox health =="; curl -s http://localhost/integration/health; echo ;;
  *) echo "unknown command: $cmd"; grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 1 ;;
esac
