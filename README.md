# Smart Report — Northpoint Fulfillment

A demo of one idea: **most reports shouldn't be built.** Someone needs a number, a developer
inherits a PDF generator, and six months later a renamed column breaks it silently. The data is
already in Postgres. Let people ask it — under the permissions you already wrote.

The demo is a fictional third-party logistics company, **Northpoint Fulfillment**, and the Smart
Reports portal it gives its clients. Sign in as someone at Trailhead Coffee Co. and ask about
their orders.


---

## The claim, and how it is kept

**The model never writes SQL.** It emits a JSON `ReportSpec` naming measures from a catalog. That
spec is validated against the catalog, authorized for the person asking, and compiled to
parameterized SQL by the server from its own metadata. Text from the question reaches the database
only as a bound parameter value.

```
question + principal
   └─▶ IReportPlanner      the model sees catalog keys — no schema, no rows, no connection
       └─▶ SpecValidator   every key must resolve, or the plan is rejected
           └─▶ PolicyEngine    may this person use this measure? yes or no, with a reason
               └─▶ SqlCompiler  SQL built from catalog metadata + parameters only
                   └─▶ Postgres     SET LOCAL app.* → row-level security bounds the rows
                       └─▶ report (+ what was read, and what was held back)
```

There is no fallback lane and no flag that lets model output become SQL text. `src/core/` — where
this is enforced — has **no package references and no database access**, and `ArchitectureTests`
fails the build if that stops being true.

Three things a reviewer usually wants to check, and where to check them:

| Claim | Where it is enforced | Test |
|---|---|---|
| The client account comes from the session, never from the model's prompt | `PrincipalDirectory` reads it from the database each request | `TenantIsolationTests` |
| The database blocks other clients' rows even if the query asks for them | RLS policies in the creating migration | `TenantIsolationTests.Naming_another_client_in_the_query_returns_nothing` |
| Benchmarks come from anonymized totals with a minimum cohort | `MandatoryPredicates` in the compiler **and** the table's policy | `BenchmarkDisclosureTests` |

---

## The three moments

**1. "How does our on-time rate compare to other Northpoint clients?"**

The question clients really ask, and exactly where a naive text-to-SQL bot leaks other customers'
data. The answer is *yes, safely*: your 97.2%, a peer median of 95.8%, and a stated safeguard —
benchmarks use anonymized totals from 20+ clients, so no single client's figures can be recovered.

Three independent defences: `benchmark_metrics` has **no `client_id` column at all**, so there is
nothing to attribute; the `benchmarks` catalog model carries a mandatory `client_count >= 20`
predicate that `SqlCompiler` welds into every query; and the table's RLS policy repeats the floor.
Northpoint had only seven clients in early 2025 — ask about those months and the rows are simply
not there.

**2. "What does Northpoint pay UPS per label?"**

> Carrier contract rates are Northpoint's internal data. *You can see billed cost per shipment
> instead.*

`carrier_contract_rates` lives in the same database and no client-facing role can read it. The
redirect is itself authorized — `PolicyEngine` only offers an alternative after checking the asker
may actually use it, because a suggestion that leads to a second refusal is worse than none.

**3. "Show our fees and invoice breakdown."**

> Billing is restricted to the account owner. *Ask Priya Shah, Trailhead Coffee Co.'s account
> owner, for access.*

A reason, a named person, and a way forward. Switch to Priya and the same question answers.

Cross-client isolation is enforced everywhere and mentioned once, in the provenance line under
every report: *Ran 6 read-only queries on shipments, orders, products — this account's data only.*
It is table stakes, not a moment.

---

## Running it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Docker. No npm.

```bash
cp .env.example .env         # then fill in the two passwords
docker compose -f docker-compose.local.yml up -d
dotnet run --project src/app
```

The build fetches the Tailwind CLI on first run, and the app migrates and seeds itself in
development. Open <http://localhost:5199>.

Seeding writes about 430,000 rows and takes roughly twenty seconds — the report's figures are
aggregations over real orders, not constants, so the data has to exist.

| Sign in as | Role | Sees | Refused |
|---|---|---|---|
| **Dana Whitfield** | Warehouse Coordinator | shipping, orders, inventory, returns, benchmarks | billing → named to Priya |
| **Priya Shah** | Account Owner | all of the above **＋** fees and invoices | Northpoint's internal costs |

The switcher has no password and is **disabled outside development** — see `A12` in
[CLAUDE.md](CLAUDE.md).

---

## Using a real model

The default planner is scripted: a fixed question→plan table, no model, no key. It keeps the demo
runnable anywhere and the governance tests free of a model's variability.

For the real thing, set a provider and a key:

```bash
dotnet user-secrets set "Ai:ApiKey" "sk-..." --project src/app
```

```jsonc
// appsettings.Development.json — never the key itself (C1/C2)
{ "Ai": { "Provider": "anthropic", "Model": "claude-opus-5-5" } }
```

`anthropic` · `openai` · `ollama` · `openai-compatible` (Groq, OpenRouter, Together, vLLM, LM
Studio — set `Ai:BaseUrl`). One `IChatClient`; `ChatClientFactory` is the only file that names a
vendor.

**The model is handed a JSON Schema generated from the catalog**, in which the metric and
dimension fields are `enum`s of the keys that actually exist. An invented measure is structurally
impossible rather than rejected after the fact. `SpecValidator` still runs, because schema
enforcement varies by provider.

**It will not fall back.** If the provider is unreachable you get an error saying so, never a
scripted answer wearing a model's name — and the rail names the planner under every report.

> **What leaves the machine.** With a hosted provider, the question and the catalog's *key names*
> are sent. **Rows never are** — the model only ever sees measure names, which is a direct
> consequence of planning a spec instead of writing SQL. With `ollama` or `openai-compatible`
> pointed at your own server, nothing leaves at all.

---

## Tests

```bash
dotnet test
```

90 tests. The ones that matter are not about rendering:

| | |
|---|---|
| `BenchmarkDisclosureTests` | the cohort floor is in every benchmark query; the model cannot reference a client; sub-floor rows are unreadable **and really exist**, so the check is not vacuous |
| `GovernanceEndToEndTests` | all three moments through the real pipeline, plus the exact October figures |
| `TenantIsolationTests` | two clients, different rows; naming another client returns nothing |
| `PrincipalLeakTests` | a pooled connection carries no identity into the next request |
| `RlsCoverageTests` | fails the build if any table lacks RLS or any view lacks `security_invoker` |
| `ArchitectureTests` | `core` has no dependencies, and the pipeline lives in it |

`RlsTests`, `TenantIsolationTests`, `BenchmarkDisclosureDatabaseTests` and friends use
Testcontainers and **fail rather than skip** without Docker. Row-level security cannot be verified
against a fake database, and a security test that quietly skips still reports green.

### Proving it without the app in the picture

```bash
psql "postgresql://smartreport_app:…@localhost:5433/smartreport"
```
```sql
SET app.role = 'coordinator'; SET app.client_id = '1';
SELECT count(*) FROM orders;                               -- Trailhead's orders
SELECT count(*) FROM orders WHERE client_id = 2;           -- 0, not Bluefin's
SELECT count(*) FROM benchmark_metrics WHERE client_count < 20;  -- 0
SELECT count(*) FROM carrier_contract_rates;               -- 0
SET app.role = '';          SELECT count(*) FROM orders;   -- 0, not everything
DELETE FROM orders;                                        -- ERROR: permission denied
```

The last two are the interesting ones. `current_setting(…, true)` returns NULL when unset and
`set_config` cannot store NULL, so "no role" arrives as an empty string — a policy checking only
`IS NOT NULL` would wave an unconfigured connection straight through. One did, until
`RlsTests.An_unconfigured_connection_sees_nothing` caught it.

---

## Layout

```
CLAUDE.md     Safe Build Pack v2.1, adapted for ASP.NET Core — the rules this repo is held to
src/app/      Razor Pages, the SSE endpoint, security middleware — presentation only
src/core/     catalog, specs, policy, SQL compiler, the pipeline — no I/O, BCL only
src/ai/       planner implementations: scripted, and any provider via Microsoft.Extensions.AI
src/data/     EF Core + Npgsql: entities, migrations, RLS policies, seed
tests/        the governance suite
docs/         palette.md — the chart palette and why it has exactly four slots
```

Dependencies point inwards: `app` → `core`/`ai`/`data`, and `core` → nothing.

## Notes for anyone extending this

- **Widening what it can answer means adding to `DataCatalog`.** That is the only way, by design.
- **A new table needs RLS, forced, with per-action policies, in the migration that creates it.**
  `RlsCoverageTests` fails if you forget.
- **Every policy must deny on an unset `app.*`.** See the note above about empty strings.
- **Do not add a fifth chart colour by eye** — re-run the validator; `docs/palette.md` explains why
  there are four.
- Tailwind is a standalone binary fetched by `scripts/get-tailwind.sh` and run from the build.
  Class names must appear literally in source: `fill-series-${i}` produces a class that does not
  exist.

## This is a demo

Not production software. The sign-in switcher is unauthenticated, Northpoint and its clients are
invented, and nothing here has been through a security review. The patterns are the point; the
code is an illustration of them.
