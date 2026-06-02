# Architectural Evolution of nopCommerce — Scenario C

Group assignment for **Software Architectures** (MEI · Universidade de Aveiro · Prof. Cláudio Teixeira).
Project name: **VerdeMart** — an omnichannel retail scenario built on top of nopCommerce.

## Team

| Name | Student ID |
|---|---|
| Martim Santos | 114614 |
| Sebastião Teixeira | 114624 |
| Duarte Santos | 113304 |
| Henrique Teixeira | 114588 |

---

## What this project is

Scenario C from the assignment brief: nopCommerce becomes the **commerce core** of a wider
operational ecosystem (ERP, warehouse, physical POS). The architectural pressure point is
**dependency degradation** — when the WMS goes down, the system must:

1. Keep accepting orders (0% blocked).
2. Make the degradation **visible** in real time.
3. **Recover** automatically — drain queued work, reconcile state — when the dependency returns.

The repository contains the runnable architectural demonstration: nopCommerce extended with the
**Outbox pattern**, a RabbitMQ-based integration backbone with **dead-letter queues**, a
**circuit-breaker**-protected adapter to the WMS, a real **OSPOS** point of sale wired in
through a polling adapter, and a **live observability dashboard**. Everything orchestrated by
a single `docker compose up`.

---

## How to run

### Prerequisites

- Docker & Docker Compose (v2)
- Ports free on the host: `80`, `1433` (optional), `5672`, `15672`, `8001`, `8002`, `8080`, `8085`, `8090`

### Start the full stack

```bash
docker compose up -d --build
docker compose logs -f nopcommerce_web   # watch nopCommerce come up
```

First run will show the **nopCommerce installation wizard** at `http://localhost`. Use:

- **DB type:** MSSQL
- **Server name:** `nopcommerce_database`
- **DB name:** `nopCommerce`
- **User / Password:** `sa` / `nopCommerce_db_password`
- Check *"Create database"* and *"Install sample data"*

### Service endpoints

| Service | URL | Notes |
|---|---|---|
| nopCommerce storefront | http://localhost | Web checkout |
| nopCommerce admin | http://localhost/admin | Schedule tasks, integration health |
| nopCommerce integration health | http://localhost/integration/health | JSON status |
| OSPOS | http://localhost:8080 | Physical POS UI |
| Observability Dashboard | http://localhost:8090 | Live circuit / DLQ / outbox state |
| RabbitMQ management | http://localhost:15672 | `guest` / `guest` |
| ERP stub | http://localhost:8001 | `POST /orders`, `POST /admin/mode` |
| WMS stub | http://localhost:8002 | `POST /reservations`, `POST /admin/mode` |
| WMS event adapter | http://localhost:8085 | `POST /webhooks/stock-changed` |

### Reset

```bash
docker compose down -v
rm -f src/Presentation/Nop.Web/App_Data/dataSettings.json
docker compose up -d --build
```

---

## Documentation map

| Category | Document | What's in it |
|---|---|---|
| Architecture report | [docs/architecture-report.md](docs/architecture-report.md) | The integrated final report — read first |
| Drivers & QA | [docs/architecture/drivers-and-qa-scenarios.md](docs/architecture/drivers-and-qa-scenarios.md) | Business drivers, 5 quality attribute scenarios, framework choice (ADD) |
| Current state | [docs/architecture/current-state-analysis.md](docs/architecture/current-state-analysis.md) | Baseline nopCommerce analysis |
| Bounded contexts | [docs/architecture/bounded-contexts.md](docs/architecture/bounded-contexts.md) | Domain boundaries, ownership, ACLs |
| Target architecture | [docs/architecture/target-architecture.md](docs/architecture/target-architecture.md) | C4 diagrams and component responsibilities |
| Evolution roadmap | [docs/architecture/evolution-roadmap.md](docs/architecture/evolution-roadmap.md) | Phases 0–3 from baseline to pressure-point demo |
| Risk plan | [docs/architecture/risk-plan.md](docs/architecture/risk-plan.md) | Risks, severities, mitigations, spike evidence |
| ADR-001 | [docs/adr/ADR-001-messaging-rabbitmq-vs-kafka.md](docs/adr/ADR-001-messaging-rabbitmq-vs-kafka.md) | Why RabbitMQ over Kafka |
| ADR-002 | [docs/adr/ADR-002-reliability-outbox-vs-direct-publish.md](docs/adr/ADR-002-reliability-outbox-vs-direct-publish.md) | Outbox pattern over direct publish |
| ADR-003 | [docs/adr/ADR-003-integration-service-runtime.md](docs/adr/ADR-003-integration-service-runtime.md) | .NET Worker Service for the Integration Service |
| ADR-004 | [docs/adr/ADR-004-wms-real-vs-stub.md](docs/adr/ADR-004-wms-real-vs-stub.md) | Stubs for ERP/WMS, real OSPOS for POS |
| OSPOS setup | [docs/ospos-setup.md](docs/ospos-setup.md) | OSPOS database schema and access |
| OSPOS adapter | [docs/ospos-adapter.md](docs/ospos-adapter.md) | Polling adapter design and config |
| Spike guide | [docs/spike-execution-guide.md](docs/spike-execution-guide.md) | Outbox feasibility spike (executed during Part 1) |
| Evidence pack | [docs/evidence/](docs/evidence/) | Health snapshots, dashboard states, scenario traces, timings |
| Presentations | [docs/ppt/](docs/ppt/) | Checkpoint and final decks |
| Task tracking | [TODO.md](TODO.md) | Implementation checklist (Parts 1 and 2) |

---

## Repository layout

```
.
|--- src/                                  # nopCommerce source (ASP.NET Core 10)
|   |--- Libraries/Nop.Services/Integration/   # Outbox publisher, RabbitMQ publisher, consumers
|   |--- Libraries/Nop.Core/Domain/Events/     # IntegrationEvent entity
|   |--- Presentation/Nop.Web/                 # Web host (storefront + admin + /integration/health)
|--- services/                             # Surrounding systems
|   |--- erp-stub/                             # FastAPI ERP simulator with failure injection
|   |--- wms-stub/                             # FastAPI WMS simulator with mode control
|   |--- wms-event-adapter/                    # Webhook -> RabbitMQ bridge (stock changes)
|   |--- ospos-adapter/                        # .NET polling adapter for OSPOS sales
|   |--- dashboard/                            # Observability dashboard (polls /health every 2s)
|--- docs/                                 # Architecture report, ADRs, evidence pack
|--- scripts/                              # Helper scripts (e.g. catalog sync)
|--- ospos_minimal_schema.sql              # Auto-loaded OSPOS schema for the MySQL container
|--- docker-compose.yml                    # Single-command orchestration of the full stack
|--- Dockerfile                            # nopCommerce web image
```

---

## Acknowledgements

This project is a fork of [nopCommerce](https://www.nopcommerce.com/), the open-source
ASP.NET Core eCommerce platform. The nopCommerce codebase is licensed under the
[nopCommerce Public License](LICENSE.md); modifications made for this assignment live
under the same terms.
