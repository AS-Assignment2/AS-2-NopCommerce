# OSPOS Setup Documentation

## Deployment Status

**OSPOS MySQL Database:** Running and configured  
**OSPOS Web UI:** Not operational (HTTP 500) - not required for adapter  
**Database Schema:** Minimal schema loaded

## Database Access

- **Host:** ospos_mysql (Docker network)
- **Port:** 3306
- **Database:** ospos
- **User:** root
- **Password:** ospospass

## Database Schema

Created minimal schema required for adapter operation:

### Tables

1. **ospos_items** - Product catalog
   - item_id (PK)
   - item_number (SKU) - UNIQUE
   - name
   - unit_price
   - quantity

2. **ospos_sales** - Sale transactions
   - sale_id (PK)
   - sale_time (TIMESTAMP)
   - employee_id
   - sale_status

3. **ospos_sales_items** - Sale line items
   - sale_id (FK)
   - item_id (FK)
   - quantity_purchased
   - item_unit_price

4. **ospos_employees** - Employee records
   - person_id (PK)
   - username
   - Default admin user created

## Testing

### Insert Test Product

```sql
INSERT INTO ospos_items (item_number, name, unit_price, quantity) 
VALUES ('SKU-001', 'Product Name', 10.00, 100);
```

### Create Test Sale

```sql
INSERT INTO ospos_sales (employee_id, sale_time, sale_status) 
VALUES (1, NOW(), 'COMPLETED');

SET @sale_id = LAST_INSERT_ID();

INSERT INTO ospos_sales_items (sale_id, item_id, line, quantity_purchased, item_cost_price, item_unit_price) 
VALUES (@sale_id, 1, 1, 2, 8.00, 10.00);
```

### Verify Adapter Query

```sql
SELECT 
    s.sale_id,
    s.sale_time,
    i.item_number AS sku,
    si.quantity_purchased AS quantity
FROM ospos_sales s
JOIN ospos_sales_items si ON s.sale_id = si.sale_id
JOIN ospos_items i ON si.item_id = i.item_id
WHERE s.sale_time > '2020-01-01 00:00:00'
ORDER BY s.sale_time ASC
LIMIT 100;
```

## Product Catalog Sync

Products are inserted into the ospos_items table to match nopCommerce SKUs.

Example sync script:

```sql
-- Clear existing test data
DELETE FROM ospos_items WHERE item_number LIKE 'TEST%';

-- Insert real products from nopCommerce
-- (Export from nopCommerce, transform to SQL INSERT statements)
INSERT INTO ospos_items (item_number, name, unit_price, quantity) VALUES
('WIDGET-001', 'Premium Widget', 29.99, 50),
('GADGET-002', 'Smart Gadget', 49.99, 30),
('TOOL-003', 'Professional Tool', 99.99, 20);
```

## Access

- **MySQL CLI:** `docker exec -it ospos_mysql mysql -u root -pospospass ospos`
- **OSPOS Web UI:** Not operational - adapter polls database directly

## Notes

- OSPOS web UI returns HTTP 500 - likely due to incomplete configuration
- **For adapter purposes, this is acceptable** - adapter queries MySQL directly
- Manual SQL inserts simulate POS sales for testing
- In production, OSPOS web UI would be fully configured for cashier use
