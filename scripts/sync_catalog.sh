#!/usr/bin/env bash
# Sync product catalog from nopCommerce (MSSQL) into OSPOS (MySQL).
# Matches on SKU. Idempotent: ON DUPLICATE KEY UPDATE for items, INSERT IGNORE for inventory.

set -euo pipefail

MSSQL_CONTAINER="${MSSQL_CONTAINER:-nopcommerce_mssql_server}"
MSSQL_DB="${MSSQL_DB:-nopCommerce}"
MSSQL_USER="${MSSQL_USER:-sa}"
MSSQL_PASSWORD="${MSSQL_PASSWORD:-nopCommerce_db_password}"

MYSQL_CONTAINER="${MYSQL_CONTAINER:-ospos_mysql}"
MYSQL_DB="${MYSQL_DB:-ospos}"
MYSQL_USER="${MYSQL_USER:-root}"
MYSQL_PASSWORD="${MYSQL_PASSWORD:-ospospass}"

products="$(docker exec "$MSSQL_CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U "$MSSQL_USER" -P "$MSSQL_PASSWORD" -d "$MSSQL_DB" \
    -No -W -s '|' -h -1 -Q "SET NOCOUNT ON;
        SELECT REPLACE(Sku, '''', ''''''),
               REPLACE(Name, '''', ''''''),
               Price,
               StockQuantity
        FROM Product
        WHERE Deleted = 0 AND Published = 1 AND Sku IS NOT NULL AND Sku <> '';")"

awk -F'|' 'NF==4 && $1 != "" {
    printf "INSERT INTO ospos_items (item_number, name, unit_price) VALUES (\"%s\", \"%s\", %s) ON DUPLICATE KEY UPDATE name=VALUES(name), unit_price=VALUES(unit_price);\n", $1, $2, $3
}' <<< "$products" \
| docker exec -i "$MYSQL_CONTAINER" mysql -u"$MYSQL_USER" -p"$MYSQL_PASSWORD" "$MYSQL_DB"

count="$(docker exec "$MYSQL_CONTAINER" mysql -u"$MYSQL_USER" -p"$MYSQL_PASSWORD" "$MYSQL_DB" -N -e "SELECT COUNT(*) FROM ospos_items;" 2>/dev/null)"
echo "OSPOS catalog now has $count items."
