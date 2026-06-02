> Part 1 spike artifact retained for posterity. The spike was executed and proven viable; the production implementation has since superseded this guide.

# Feasibility Spike - Execution Guide

## Prerequisites

1. **Start RabbitMQ + Database:**
   ```bash
   docker-compose up -d rabbitmq nopcommerce_database
   ```
   
   Or manually:
   ```bash
   docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
   ```
   
   Verify RabbitMQ at: http://localhost:15672 (username: guest, password: guest)

2. **Database connection configured** in nopCommerce

## Execution Steps

### Step 1: Build the Solution

```bash
cd AS-2-NopCommerce
dotnet restore src
dotnet build --no-restore src
```

### Step 2: Run nopCommerce

```bash
cd src/Presentation/Nop.Web
dotnet run
```

**Expected:** Application started, migration ran, `IntegrationEvent` table was created.

### Step 3: Verify Migration

Connect to the database and check:

```sql
-- Check if table exists
SELECT * FROM IntegrationEvent;

-- Should be empty at this point
```

### Step 4: Register the Schedule Task

Run the SQL script appropriate for the database from `docs/spike-schedule-task-registration.sql`

**For PostgreSQL:**
```sql
INSERT INTO "ScheduleTask" ("Name", "Seconds", "Type", "Enabled", "StopOnError", "LastEnabledUtc")
VALUES (
    'Spike - Publish Outbox Events',
    10,
    'Nop.Services.Integration.SpikeOutboxPublisherTask, Nop.Services',
    true,
    false,
    NOW()
);
```

**Verify:**
```sql
SELECT * FROM "ScheduleTask" WHERE "Name" LIKE '%Spike%';
```

### Step 5: Restart nopCommerce

Stop the application (Ctrl+C) and restart:

```bash
dotnet run
```

**What happened:**
1. `AppStartedEvent` was published
2. `AppStartedEventConsumer` wrote a test event to the `IntegrationEvent` table
3. `SpikeOutboxPublisherTask` ran every 10 seconds
4. The task read unpublished events and published them to RabbitMQ
5. Events were marked as published in the database

### Step 6: Verify in Database

```sql
SELECT * FROM "IntegrationEvent" ORDER BY "Id" DESC;
```

**Expected result:**
- At least one row with `EventType = 'ApplicationStarted'`
- `Published = true`
- `PublishedOnUtc` populated
- `PublishAttempts = 1`

### Step 7: Verify in RabbitMQ Management UI

1. Go to http://localhost:15672
2. Login with guest/guest
3. Click **Exchanges** tab
4. Look for the `verdemart.events` exchange (type=topic)
5. Click **Queues** tab
6. Click "Add a new queue", name it `spike-test`, create
7. Click the queue, then the "Bindings" section
8. Bind to `verdemart.events` with routing key `#` (matches all)
9. Check "Get messages" - the ApplicationStarted event should appear

### Step 8: Check Logs

Navigate to Admin area (if installed), System, Log.

**Expected log entries:**
- "Spike: AppStartedEvent received, writing to outbox"
- "Spike: Wrote IntegrationEvent to outbox (Id=1)"
- "Spike: Outbox publisher task started"
- "Spike: Found 1 unpublished events"
- "Spike: Published message to RabbitMQ: exchange=verdemart.events, routingKey=ApplicationStarted"
- "Spike: Published IntegrationEvent Id=1"
- "Spike: Outbox publisher task completed"

## Success Criteria (from risk-plan.md)

- Message appeared in RabbitMQ within 10s of startup. Verified.
- Row marked as `Published = true` in database. Verified.
- No DI registration errors or crashes. Verified.

## Troubleshooting

### Issue: Migration doesn't run
- **Check:** Migration timestamp is 2026-05-04 (future dated)
- **Fix:** Delete the migration file and recreate with the correct timestamp
- **Verify:** Check the `VersionInfo` table in the database

### Issue: Task never executes
- **Check:** SQL insert was successful
- **Check:** `Enabled = true` in the ScheduleTask table
- **Fix:** Restart the application after the SQL insert

### Issue: RabbitMQ connection fails
- **Check:** Docker container is running: `docker ps`
- **Check:** Port 5672 is accessible
- **Fix:** `docker start rabbitmq` or recreate the container

### Issue: DI resolution error
- **Check:** `IntegrationStartup.Order = 3000` (must be > 2000)
- **Check:** All services are registered as `Scoped`
- **Fix:** Verify DI registration in IntegrationStartup.cs

### Issue: Event not written to outbox
- **Check:** Logs for "Spike: AppStartedEvent received"
- **Check:** `AppStartedEventConsumer` class exists
- **Fix:** Rebuild the solution to register the consumer

### Issue: RabbitMQ.Client not found
- **Check:** Nop.Services.csproj has the package reference
- **Fix:** Run `dotnet restore src`

## Clean Database Query

To reset and test again:

```sql
DELETE FROM "IntegrationEvent";
-- Restart the application to trigger AppStartedEvent again
```

## Next Steps After Successful Spike

The spike verification passed. Follow-up actions executed in Part 2:
1. Risk 1 marked as Mitigated in risk-plan.md
2. Spike results documented under docs/evidence/
3. Spike code refactored for production:
   - "Spike" prefix removed from classes
   - RabbitMQ configuration moved to appsettings.json
   - Polly retry policies added (ERP adapter: 3 attempts, exponential backoff 1s/2s/4s)
   - `AppStartedEventConsumer` replaced with the real `OrderPlacedEvent` consumer
4. Full Part 2 implementation delivered, including the Order Integration Service at port 8083.
