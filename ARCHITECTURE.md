# Architecture

Fit Recovery Log is an offline-first personal health tracker with three clients over one
cloud backend:

- **Mobile** — .NET MAUI Blazor Hybrid (iOS), local-first SQLite, works fully offline, syncs.
- **Web** — Blazor WebAssembly, online-only, reads/writes the cloud API.
- **API** — Azure Functions (.NET isolated) + Azure SQL, the sync hub.

It is deliberately built as a **Clean Architecture / Domain-Driven Design** solution: the
business rules live in a dependency-free domain, application use cases orchestrate them, and
every client and the server are thin adapters over the same core.

## Dependency rule

Dependencies point inward only. The domain knows nothing about EF, HTTP, MAUI, or Azure.

```
        ┌─────────────────────────── Presentation ───────────────────────────┐
        │   MAUI (iOS)        Blazor WASM (web)        Azure Functions (API)   │
        └───────────────┬───────────────┬───────────────────┬─────────────────┘
                        │               │                   │
                        ▼               ▼                   ▼
                 ┌──────────────────── Application ─────────────────────┐
                 │  use cases / command handlers · port interfaces      │
                 │  (IRepository, IHealthMirror, INutritionAnalyzer,     │
                 │   IReminderScheduler) · DTOs · Result<T>              │
                 └───────────────┬──────────────────────┬───────────────┘
                                 │                      │
                                 ▼                      ▼
                        ┌──────────────┐      ┌───────────────────────────┐
                        │    Domain    │◄─────│      Infrastructure       │
                        │ entities +   │      │ EF Core repos · sync ·    │
                        │ behavior ·   │      │ Gemini · Azure SQL        │
                        │ value objects│      └───────────────────────────┘
                        │ · domain svc │
                        │ · invariants │
                        └──────────────┘
```

## Projects

| Project | Layer | Depends on | Notes |
|---|---|---|---|
| `FitRecoveryLog.Domain` | Domain | — | Rich entities, value objects, domain services, invariants. No external deps. |
| `FitRecoveryLog.Application` | Application | Domain | Use cases, port interfaces, DTOs, `Result<T>`. |
| `FitRecoveryLog.Infrastructure` | Infrastructure | Application, Domain | EF Core repositories, sync engine, Gemini adapter. |
| `FitRecoveryLog` | Presentation (mobile) | Application | MAUI head; implements device-only ports (HealthKit, notifications). |
| `FitRecoveryLog.Web` | Presentation (web) | Application (via API) | Blazor WASM; online-only. |
| `FitRecoveryLog.Server` | Presentation (API) | Application, Infrastructure | Functions; the sync hub. |
| `FitRecoveryLog.Tests` | Tests | Domain, Application | NUnit; domain rules + use cases. |

## Why this shape (the problem it solves)

Before this refactor the model was **anemic**: entities were property bags and the business
rules lived in each client's UI code. That let the web client bypass invariants the phone
relied on (it could only do raw CRUD through the sync API). Moving the rules into a shared
**Application** layer over a rich **Domain** means every client — phone, web, server — enforces
the *same* behavior. Device-only concerns (HealthKit, local notifications) are modelled as
**ports** implemented in the mobile head, which also documents precisely why the web legitimately
can't do them.

## Tactical DDD patterns

- **Value objects** — immutable, equality-by-value, self-validating. The headline one is
  **`Macros`** (calories/protein/carbs/fat/fiber/sugar/added-sugar/sodium) with real behavior
  (`+`, `Scale(servings)`), reused by meals and drinks.
- **Aggregates** — consistency boundaries with enforced invariants:
  - **`Routine`** (root) + its ordered exercises; rules: exercise ordering, archive state,
    "deleting a routine detaches its past sessions" (history preserved).
  - **`Workout`** (session) + sets + feedback; "completing a workout sets the day type."
  - **`ExerciseLibrary` / `ExerciseDefinition`** — unique-name invariant.
- **Domain services** — logic that doesn't belong to one entity: `NutritionCalculator`
  (daily totals vs goal ranges), `ProgressionEvaluator` (rule-based progression heuristic).
- **`Result<T>`** for expected failures instead of exceptions.
- **Ports & adapters** — `IHealthMirror`, `INutritionAnalyzer`, `IReminderScheduler`,
  `IRepository<T>` declared in Application, implemented in Infrastructure or the mobile head.

## Persistence

EF Core maps the **rich domain** (private setters / backing fields via Fluent configuration) —
the domain stays free of EF attributes. SQLite on the phone, SQL Server in the cloud, from one
model; migrations per provider. Sync stays as designed (see `docs/sync-architecture.md`):
local-first, tombstones, `UpdatedAt` cursor.

## Testing & CI

- **NUnit** unit tests for domain invariants and application use cases (no infrastructure needed).
- **GitHub Actions** builds `Domain`/`Application`/`Infrastructure` and runs the tests on every
  push — the platform-independent core, so CI needs no MAUI/iOS workloads.

## Migration approach (incremental, always green)

The live 3-tier system stays deployable throughout.

1. Stand up `Domain`/`Application`/`Infrastructure`/`Tests` + CI (additive).
2. **Pilot slice — Routines**, end to end: rich `Routine` aggregate → application use cases
   (`ArchiveRoutine`, `DeleteRoutine`, …) → EF repository → phone + web call the use cases →
   tests. Reviewed before rollout.
3. Roll the same pattern out entity-group by entity-group. `FitRecoveryLog.Shared` (today's
   POCOs) is absorbed into `Domain` as entities gain behavior; no forked model at any point.
