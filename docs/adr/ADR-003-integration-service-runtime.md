# ADR-003: Order Integration Service Runtime - .NET Worker Service vs Python

**Status:** Implemented  
**Date:** 2026-04-26  
**Owner:** Sebastião  
**Deciders:** Full team

---

## Context

The Order Integration Service is an independently deployable component. Its runtime must be chosen between a .NET Worker Service (same stack as nopCommerce) and a Python service (faster to prototype, common in integration work).

---

## Decision

Use a .NET Worker Service (ASP.NET Core minimal API + `IHostedService`).

---

## Rationale

| Criterion | .NET Worker Service | Python (FastAPI / Celery) |
|-----------|--------------------|-----------------------------|
| Polly circuit breaker and retry | Native, mature, well-tested | Requires tenacity or custom code, less mature |
| Stack familiarity | High (C# matches the existing project) | Lower |
| Type-safe message contracts | Shared C# models possible | Requires separate schema definitions |
| Docker image size | ~200 MB runtime | ~150 MB |
| RabbitMQ client quality | `RabbitMQ.Client`, official and stable | `pika`, stable but async support is patchy |
| Structured logging | Serilog, excellent | structlog, good |

The key driver is Polly: the circuit breaker and retry requirements are first-class in the .NET ecosystem. Polly's `CircuitBreakerPolicy` and `RetryPolicy` are well-documented, which reduces implementation risk for the most critical part of the architecture (the pressure-point scenarios).

---

## Rejected Alternatives

### Python (FastAPI + tenacity)

Python would be faster to prototype the HTTP surface but the Integration Service requires a robust circuit breaker. The `tenacity` library handles retries but lacks a clean circuit breaker abstraction comparable to Polly. Given the project's existing C# code base and the reliability-critical nature of this service, Python introduces unnecessary risk.

### Python (FastAPI + custom circuit breaker)

A hand-rolled circuit breaker in Python would close the feature gap with Polly but at the cost of additional bespoke code in the most reliability-sensitive part of the system. Maintaining a custom resilience primitive is not justified when a mature library exists in the chosen stack.

---

## Consequences

- `services/order-integration-service/` is a .NET 10 project exposed on port 8083.
- Dependencies: `RabbitMQ.Client`, `Polly`, `Serilog`, `Microsoft.Extensions.Hosting`.
- `IHostedService` hosts the order.placed and sale.completed consumers and the reconciliation loop.
- `/health` is exposed via `Microsoft.AspNetCore.Diagnostics.HealthChecks`; `/dlq`, `/dlq/clear`, and `/webhooks/stock-changed` are exposed via minimal API endpoints.
- ERP adapter applies Polly retry (3 attempts, exponential backoff 1s/2s/4s); WMS adapter applies Polly circuit breaker (3 consecutive failures open the breaker for 30s).
- Idempotency: incoming messages are deduplicated on `eventId` using an in-memory `HashSet<Guid>`, sufficient for the demo footprint; a production deployment would back this with Redis or a database table.
