# Aegis — AI-Powered Smart Infrastructure Management Platform

Aegis is an operations platform for the organisations that keep physical infrastructure running:
water utilities, power distributors, municipal road authorities. It tracks assets, ingests incident
reports written in plain language, schedules preventive and predictive maintenance, dispatches work
orders to field technicians, and answers operational questions against live data.

> **Status: in active construction.** This repository is being built one vertical slice at a time.
> The section [Build log](#build-log) records what is complete and verified versus what is planned.
> Nothing is documented here as working unless it builds and its tests pass.

---

## Table of contents

- [Why this exists](#why-this-exists)
- [Architecture](#architecture)
- [Architectural decisions](#architectural-decisions)
- [Technology choices](#technology-choices)
- [Repository layout](#repository-layout)
- [Getting started](#getting-started)
- [Testing strategy](#testing-strategy)
- [Build log](#build-log)

---

## Why this exists

Infrastructure operators run on a stack of disconnected tools: a GIS layer nobody outside the
engineering team can read, a spreadsheet of asset serial numbers, a shared inbox of public
complaints, and a maintenance schedule that is calendar-based rather than condition-based. The
consequences are ordinary and expensive — a pump fails between scheduled inspections, a reported
leak takes three days to reach the crew that could fix it, and nobody can answer "which assets in
the northern district are overdue for inspection?" without a morning of manual work.

Aegis targets four specific gaps:

| Gap | Approach |
| --- | --- |
| Reports arrive as unstructured prose | An LLM extracts asset, location, category and severity from free text, and a human confirms |
| Maintenance is calendar-based | Condition and telemetry-driven scheduling alongside fixed intervals |
| Field crews lose connectivity | Offline-first mobile client with an explicit sync and conflict-resolution protocol |
| Operational questions need an analyst | A data-grounded assistant that queries the domain via tool use rather than guessing |

---

## Architecture

### The dependency rule

Aegis is a **modular monolith** organised as Clean Architecture. Dependencies point strictly
inward; nothing in an inner ring knows an outer ring exists.

```mermaid
flowchart TD
    subgraph Outer["Aegis.Api — composition root"]
        A1["Controllers"]
        A2["SignalR hubs"]
        A3["Middleware"]
        A4["DI wiring"]
    end

    subgraph Infra["Aegis.Infrastructure — adapters"]
        I1["EF Core / SQL Server"]
        I2["Redis cache"]
        I3["JWT issuance"]
        I4["Claude API client"]
        I5["PDF / Excel"]
        I6["Blob storage"]
    end

    subgraph App["Aegis.Application — orchestration"]
        P1["Commands and queries"]
        P2["MediatR handlers"]
        P3["Pipeline behaviours"]
        P4["Port interfaces"]
        P5["FluentValidation"]
    end

    subgraph Dom["Aegis.Domain — the business"]
        D1["Entities and aggregates"]
        D2["Value objects"]
        D3["Domain events"]
        D4["Invariants"]
    end

    Outer --> Infra
    Outer --> App
    Infra --> App
    App --> Dom

    style Dom fill:#1e3a5f,stroke:#4a90d9,color:#fff
    style App fill:#2d4a3e,stroke:#5cb85c,color:#fff
    style Infra fill:#4a3a2d,stroke:#d9a04a,color:#fff
    style Outer fill:#3d2d4a,stroke:#a04ad9,color:#fff
```

`Aegis.Domain` has **no package references at all** — not EF Core, not MediatR. This is enforced by
a test (`tests/Aegis.Domain.UnitTests/Architecture/DependencyTests.cs`), not by convention, because
architecture that lives only in a document decays one reasonable-sounding exception at a time.

### How a request flows

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant Ctrl as Controller
    participant Pipe as MediatR pipeline
    participant H as Handler
    participant D as Domain
    participant DB as SQL Server
    participant Ev as Event dispatcher

    C->>Ctrl: HTTP request + JWT
    Ctrl->>Pipe: Send(command)
    Note over Pipe: Logging → Validation → Authorization<br/>→ Tenant scope → Caching → Transaction
    Pipe->>H: command
    H->>D: invoke domain method
    D-->>H: state change + domain events raised
    H->>DB: SaveChangesAsync
    DB-->>H: committed
    H->>Ev: dispatch events (post-commit only)
    Ev-->>C: SignalR push to subscribed clients
    H-->>Ctrl: Result&lt;T&gt;
    Ctrl-->>C: 200 / 400 / 404 / 409 + ProblemDetails
```

The ordering constraint worth calling out: **domain events dispatch only after the transaction
commits.** If they fired inside the handler, a rollback would leave notifications sent and audit
rows written for a change that never happened.

### Module boundaries

Modules communicate through domain events and public contracts — never by reaching into each
other's entities. That single rule is what makes a later extraction to microservices a mechanical
refactor rather than a rewrite.

```mermaid
flowchart LR
    Identity["Identity<br/><i>users, roles, tokens</i>"]
    Orgs["Organizations<br/><i>tenants, districts</i>"]
    Assets["Assets<br/><i>pipes, pumps, transformers</i>"]
    Incidents["Incidents<br/><i>NL intake, triage</i>"]
    WO["Work Orders<br/><i>dispatch, completion</i>"]
    Maint["Maintenance<br/><i>preventive, predictive</i>"]
    Ai["AI<br/><i>extraction, assistant, vision</i>"]
    Notif["Notifications<br/><i>SignalR</i>"]
    Audit["Audit<br/><i>activity history</i>"]
    Docs["Documents<br/><i>PDF, Excel</i>"]
    Analytics["Analytics<br/><i>executive dashboards</i>"]

    Incidents -. IncidentReported .-> WO
    Incidents -. IncidentReported .-> Notif
    Assets -. AssetConditionChanged .-> Maint
    Maint -. MaintenanceDue .-> WO
    WO -. WorkOrderCompleted .-> Assets
    WO -. WorkOrderAssigned .-> Notif
    Ai --> Incidents
    Ai --> Assets
    Identity --> Orgs

    Audit -. subscribes to all .-> Notif
    Analytics -. reads projections .-> Assets
    Docs -. renders .-> WO

    style Ai fill:#3d2d4a,stroke:#a04ad9,color:#fff
    style Audit fill:#2d3d4a,stroke:#4a90d9,color:#fff
```

Solid arrows are direct dependencies; dotted arrows are asynchronous domain events.

---

## Architectural decisions

Each decision below records the alternative that was rejected and why — a decision without a
discarded alternative is not a decision, it is a default.

### 1. Modular monolith, not microservices on day one

**Chosen:** one deployable API partitioned by module.

Microservices solve organisational scaling problems — independent deployment by independent teams —
at the cost of distributed transactions, network partition handling, and eleven-service local
development. None of those costs buy anything before there are multiple teams. Aegis is structured
so that extraction is possible (modules own their tables, communicate by event, never share
entities), and deferred until it is justified.

### 2. Application depends on EF Core; Domain depends on nothing

**Chosen:** `Aegis.Application` references `Microsoft.EntityFrameworkCore` (the abstraction
package, not the SQL Server provider). `Aegis.Domain` references nothing at all.

This is a documented compromise, recorded here rather than quietly made. `IAegisDbContext` exposes
`DbSet<T>`, and the purist alternative — exposing `IQueryable<T>` — costs `Include`,
`AsNoTracking`, `ExecuteUpdateAsync`, and every async terminal operator, since `ToListAsync` and
friends are EF Core extension methods rather than LINQ. Recovering them means hand-rolling an async
query abstraction larger than the thing it hides.

What actually matters is preserved: no provider, no connection string, no migrations, and no
`DbContext` base class in Application. The layer depends on a *query model*, not on a database, and
`LayerDependencyTests` asserts it never names SQL Server or Redis.

### 3. Repository Pattern only where it earns its place

**Chosen:** handlers depend on `IAegisDbContext`; dedicated repositories only for aggregates with
non-trivial reconstitution.

`DbContext` already *is* a Unit of Work and `DbSet<T>` already *is* a repository. Wrapping every
entity in `IGenericRepository<T>` adds a layer that forwards calls verbatim, and it destroys EF
Core's ability to compose a query across includes, filters and projections into one SQL statement —
generic repositories tend to force `.ToList()` at the boundary and filter in memory. Exposing the
context behind an interface keeps Application persistence-agnostic and unit-testable without paying
that price.

### 4. `Result<T>` for expected failures, exceptions for genuine faults

**Chosen:** business outcomes return `Result<T>`; invariant violations throw `DomainException`.

"Asset not found" is not exceptional — it is a routine outcome of a lookup. Modelling it as an
exception hides the failure from the method signature, costs a stack unwind on a common path, and
moves control flow into a `catch` far from the decision. A method returning `Result<Asset>` declares
failure as part of its contract. Exceptions remain for the cases where the correct response is a
bug report, and are caught once by global middleware that emits RFC 7807 `ProblemDetails`.

### 5. Tenant isolation is infrastructural, not disciplinary

**Chosen:** shared database, `ITenantOwned` marker, EF Core global query filters.

The alternative — every handler remembers `.Where(x => x.OrganizationId == current)` — fails the
moment one developer forgets once, and the failure mode is cross-tenant data disclosure. Global
query filters mean a developer writing `_db.Assets.Where(a => a.Status == Active)` gets
tenant-scoped results without knowing tenancy exists. Database-per-tenant offers stronger isolation
but adds a catalog database, dynamic connection resolution and fan-out migrations; that cost is
real and is not justified at this scale.

The honest trade-off: global filters can be bypassed with `IgnoreQueryFilters()`, so its use is
restricted to a small audited set of system operations.

### 6. UUIDv7 primary keys

**Chosen:** `Guid.CreateVersion7()` rather than `Guid.NewGuid()` or `int` identity.

Random v4 GUIDs as a clustered index key cause page splits and index fragmentation on every insert,
which on a table of millions of telemetry-adjacent rows is a measurable write penalty. UUIDv7 is
time-ordered, so inserts append to the end of the B-tree like an identity column, while keeping the
properties that matter here: ids generatable client-side (essential for the offline mobile client,
which must create work order records with no server round trip) and non-enumerable in URLs.

### 7. Domain events collected at save, dispatched after commit

**Chosen:** a `SaveChanges` interceptor harvests events into a scoped buffer; `UnitOfWorkBehavior`
drains and dispatches that buffer after `CommitAsync` returns.

The obvious implementation — dispatch from the interceptor's `SavedChangesAsync` — is subtly wrong
here. `SaveChangesAsync` is called by the handler, but the commit happens *later*, in the pipeline
behaviour after the handler returns. So `SavedChangesAsync` fires inside an **uncommitted**
transaction, and a rollback would leave notifications sent and caches invalidated for a change that
never happened. Splitting collection from dispatch is what makes "after commit" actually mean after
commit.

**Known limitation, stated plainly:** if the process dies between commit and dispatch, those events
are lost. Closing that window requires a transactional outbox — persist events as rows in the same
transaction, deliver them at least once from a background processor. That is the correct end state
and it is planned; the buffer is the honest intermediate step, not a finished one.

### 8. Central Package Management

**Chosen:** all versions in `Directory.Packages.props`.

Version drift between projects in a seven-project solution produces `MethodNotFoundException` at
runtime rather than a compile error. One file, one version, one place to audit.

Two package choices are deliberate and worth recording: **Shouldly** rather than FluentAssertions
(version 8 requires a paid commercial licence), and **NSubstitute** rather than Moq (the SponsorLink
telemetry episode). **MediatR is pinned to 12.x** for the same reason — 13.0 moved to a commercial
licence.

---

## Technology choices

### Backend

| Concern | Choice | Rationale |
| --- | --- | --- |
| Runtime | .NET 9 / ASP.NET Core 9 | Long-term support path, mature tooling, strong async story |
| Data access | EF Core 9 + SQL Server | Global query filters and interceptors are what make tenancy and auditing automatic |
| Spatial | NetTopologySuite | `geography` column type gives real distance and containment queries in SQL, not post-filtering in C# |
| Messaging | MediatR 12 | In-process dispatch with a pipeline that gives cross-cutting concerns one home |
| Validation | FluentValidation | Rules live beside the command, testable without a HTTP request |
| Cache | Redis | Shared across instances, and the backplane SignalR needs when scaled out |
| Real-time | SignalR | WebSocket transport with automatic fallback and a Redis backplane |
| Logging | Serilog | Structured logs; correlation ids queryable rather than grep-able |
| Mapping | Mapster | Source-generated, no runtime reflection cost |
| Documents | QuestPDF + ClosedXML | Both permissively licensed with no native dependencies |

### Frontend

| Concern | Choice | Rationale |
| --- | --- | --- |
| Framework | Next.js (App Router) + TypeScript | Server components for dashboards, client components where interaction demands it |
| Styling | Tailwind CSS | Design tokens as constraints, and dark mode without a parallel stylesheet |
| Server state | TanStack Query | Caching, background refetch and optimistic updates — the hard parts of a live dashboard |
| Client state | Zustand | Small, unopinionated, no provider pyramid |
| Motion | Framer Motion | Meaningful transitions; honours `prefers-reduced-motion` |
| Maps | Leaflet + OpenStreetMap | No API key, no per-tile billing, no vendor lock-in |

The state-management split is deliberate: **TanStack Query owns anything that came from the server,
Zustand owns anything that did not.** Mixing them — copying fetched data into a client store — is
the most common source of stale UI in dashboards of this kind.

---

## Repository layout

```
Aegis/
├── src/
│   ├── Aegis.Domain/           # Entities, value objects, domain events. Zero dependencies.
│   ├── Aegis.Application/      # CQRS handlers, validators, port interfaces.
│   ├── Aegis.Infrastructure/   # EF Core, Redis, JWT, Claude client, PDF/Excel.
│   └── Aegis.Api/              # Controllers, hubs, middleware, composition root.
├── tests/
│   ├── Aegis.Domain.UnitTests/        # Domain logic + architecture enforcement.
│   ├── Aegis.Application.UnitTests/   # Handler behaviour against substituted ports.
│   └── Aegis.Api.IntegrationTests/    # Full stack against Testcontainers SQL Server + Redis.
├── web/                        # Next.js frontend. Vercel's Root Directory must point here.
├── docs/                       # Architecture decision records, ER diagram, API reference.
├── Directory.Build.props       # Compiler settings for every project.
├── Directory.Packages.props    # Central package versions.
└── global.json                 # Pins the .NET SDK to 9.0.
```

---

## Getting started

### Live demo

**https://aegis-plum-omega.vercel.app** — the frontend, deployed with a seeded estate of 468 assets
across five districts.

No API is deployed, so the app runs in demo mode: `web/src/lib/api.ts` serves an in-memory dataset
whenever `NEXT_PUBLIC_API_URL` is unset. The demo path implements the same signatures, filtering,
sorting and paging semantics as the real client, so no component branches on which one is active —
which is what stops the demo rotting into a separate, half-true version of the product.

Set `NEXT_PUBLIC_API_URL` and every call goes to the real Aegis API instead.

### Prerequisites

- .NET SDK 9.0
- Docker Desktop (SQL Server, Redis, and the integration test suite)
- Node.js 20+ (frontend)

### Run everything with Docker Compose

```bash
docker compose up -d
```

Brings up SQL Server, Redis and the API. The API waits on both dependencies' **health checks**,
not merely on their containers starting — SQL Server accepts TCP connections well before it can
answer a query, so a plain `depends_on` produces a first-run migration failure roughly every time.

| Service | Endpoint |
| --- | --- |
| API | http://localhost:8080 |
| Swagger | http://localhost:8080/swagger |
| Liveness | http://localhost:8080/health/live |
| Readiness | http://localhost:8080/health/ready |
| SQL Server | `localhost,1433` |
| Redis | `localhost:6379` |

### Configuration and secrets

**No connection string is committed to this repository**, and that is load-bearing for two separate
reasons:

1. A credential in source control is published, and rotating it afterwards does not un-publish it.
2. In minimal hosting, `appsettings.json` **outranks** a test host's in-memory configuration. A
   connection string sitting in that file silently overrides what the integration suite injects, so
   the API connects to a database that is not there. This was a real CI failure, not a hypothetical.

Supply it per environment instead:

```bash
# Local development
dotnet user-secrets --project src/Aegis.Api \
  set "ConnectionStrings:Database" "Server=localhost,1433;Database=Aegis;User Id=sa;Password=...;TrustServerCertificate=True"

# Containers — docker-compose already sets this
ConnectionStrings__Database=...

# Applying migrations against a real server
AEGIS_MIGRATIONS_CONNECTION=...
```

Startup fails immediately and explicitly when the connection string is absent, rather than on the
first request.

### Build and test locally

```bash
dotnet restore
dotnet build            # warnings are errors, so this is also the style gate
dotnet test             # unit tests; integration tests need Docker running
```

The integration suite starts its own throwaway SQL Server and Redis via Testcontainers. The first
run pulls roughly 1.6 GB of images; later runs reuse them.

### Database migrations

```bash
dotnet tool restore

dotnet dotnet-ef migrations add <Name> \
  --project src/Aegis.Infrastructure \
  --startup-project src/Aegis.Api \
  --output-dir Persistence/Migrations
```

CI fails the build if the model has changes with no corresponding migration, which catches the
commit that edits an entity and forgets the schema before it reaches a real database.

### Continuous integration

`.github/workflows/ci.yml` runs three jobs on every push and pull request:

| Job | Gate |
| --- | --- |
| `build-and-test` | Release build (warnings as errors), unit tests, integration tests with coverage |
| `verify-migrations` | Fails on undeclared model changes; publishes an idempotent SQL script |
| `docker` | Builds the API image so a broken Dockerfile surfaces at review, not release |

---

## Testing strategy

Three tiers, each answering a different question:

| Tier | Question it answers | Dependencies |
| --- | --- | --- |
| Domain unit tests | Are the business rules correct? | None — pure objects |
| Application unit tests | Does the handler orchestrate correctly? | Substituted ports |
| API integration tests | Does the whole stack behave? | Real SQL Server + Redis via Testcontainers |

Integration tests deliberately avoid the EF Core in-memory provider. It does not enforce unique
constraints, does not translate the same SQL, and does not exercise global query filters the way the
real provider does — so a green in-memory suite would prove very little about tenant isolation,
which is precisely the property most needing proof.

A fourth category, **architecture tests**, asserts the dependency rule itself and fails the build if
an inner layer acquires an outward reference.

---

## Build log

### Complete and verified

**Increment 0 — Foundation** *(build green, 0 warnings)*

- Seven-project solution with the dependency rule wired and enforced
- Central Package Management; warnings treated as errors solution-wide
- `.editorconfig` encoding naming, async correctness and security analyzer policy
- Domain kernel: `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `IDomainEvent`,
  `Result`/`Result<T>`, `Error`, `DomainException`
- Entity contracts: `ITenantOwned`, `IAuditableEntity`, `ISoftDeletable`
- Architecture test proving `Aegis.Domain` references nothing outside the BCL

**Increment 1 — Persistence and cross-cutting concerns** *(build green, 87 tests passing)*

*Application ports* — `IAegisDbContext`, `ICurrentUser`, `ITenantContext`, `ICacheService`,
`IRequestContext`, `IDomainEventCollector`, `IDomainEventDispatcher`.

*CQRS contracts* — `ICommand`, `ICommand<T>`, `IQuery<T>` and their handlers, plus the
`ICacheableQuery` and `ITransactionless` opt-in markers.

*Pipeline* — five behaviours whose registration order is a functional specification:

```
Request → Logging → Performance → Validation → Caching → UnitOfWork → Handler
```

*Persistence* — `AegisDbContext` with reflection-driven global query filters, plus three
interceptors whose order is load-bearing:

| Interceptor | Responsibility |
| --- | --- |
| `PersistenceMetadataInterceptor` | Stamps tenant + audit fields; converts deletes into soft deletes |
| `AuditTrailInterceptor` | Writes append-only audit rows, reading the stamped values |
| `DomainEventCollectionInterceptor` | Harvests domain events for post-commit dispatch |

*Querying* — `PaginatedQuery` with a hard page-size ceiling, `PagedResult<T>`, and composable
`WhereIf` / `ApplySort` / `ToPagedResultAsync` helpers with whitelist-validated dynamic sorting.

*API* — Serilog with correlation ids, `GlobalExceptionHandler` emitting RFC 7807 `ProblemDetails`,
`CorrelationIdMiddleware`, `TenantResolutionMiddleware`, `ApiControllerBase`, Swagger, and split
liveness/readiness health checks.

*Schema* — initial migration creating the `audit.AuditTrail` table with three purpose-built indexes.

**Increment 1a — Integration suite, Docker and CI** *(CI fully green: 171 tests)*

Container-backed suite proving tenant isolation against real SQL Server, Docker Compose stack,
multi-stage container image, and a three-job GitHub Actions pipeline.

Three real defects were caught by CI rather than by review, each on a seam local builds cannot see:

| Defect | Why local builds missed it |
| --- | --- |
| `appsettings.json` connection string overrode the test host | The committed value happened to be right on a dev machine |
| `ConfigureAppConfiguration` applies after service registration | Only fails where the injected value is the *only* source |
| Dockerfile omitted `.editorconfig` | The image built under a stricter analyzer ruleset than every other build |

**Increment 2 (domain layer) — Identity** *(93 domain tests)*

`User`, `Role`, `RefreshToken`, `EmailAddress`, `PasswordHash` and the permission catalogue, with
time-boxed lockout, security-stamp rotation on any privilege change, and refresh-token rotation
with reuse detection that revokes the whole chain.

### Planned

| # | Increment | Delivers |
| --- | --- | --- |
| 2b | Identity (application + API) | Registration, login, JWT issuance, refresh endpoint, permission policies |
| 3 | Organizations | Tenants, districts, membership, invitations |
| 4 | Assets | Asset hierarchy, spatial data, condition tracking, pagination and filtering |
| 5 | Incidents | Natural-language intake with Claude extraction, triage, deduplication |
| 6 | Work orders | Dispatch, assignment, completion, offline sync protocol |
| 7 | Maintenance | Preventive schedules and predictive scoring |
| 8 | Real-time | SignalR hubs with Redis backplane |
| 9 | Analytics | Executive dashboards and read projections |
| 10 | Documents | PDF and Excel generation |
| 11 | AI assistant | Tool-use grounded in tenant-scoped data |
| 12 | Vision | Image upload with AI-assisted defect analysis |
| 13 | Frontend | Next.js application and reusable component library |
| 14 | DevOps | Docker Compose and GitHub Actions CI/CD |

---

## Licence

Not yet determined.
