# Smart Report

Ask for a report in plain language. A model that runs on your own hardware turns the question into
a plan; your database decides what the answer may contain.

A demo of one idea: **most reports shouldn't be built.** Someone needs a number, a developer
inherits a PDF generator, and six months later a renamed column breaks it silently. The data is
already sitting in Postgres. Let people ask it — under the permissions you already wrote.

---

## The claim, and how it's kept

**The model never writes SQL.** It emits a JSON `ReportSpec` naming measures and groupings from a
catalog. That spec is validated against the catalog, authorized for the person asking, and then
compiled to parameterized SQL by the server using its own metadata. Text from the question reaches
the database only as a bound parameter value.

```
question + principal
   └─▶ IReportPlanner      the model sees catalog keys — no schema, no connection
       └─▶ SpecValidator   every key must resolve, or the plan is rejected
           └─▶ PolicyEngine    may this person use this measure? says yes or no, with a reason
               └─▶ SqlCompiler  SQL built from catalog metadata + parameters only
                   └─▶ Postgres     SET LOCAL app.* → row-level security bounds the rows
                       └─▶ report (+ what was read, and what was withheld)
```

There is no fallback lane and no flag that lets model output become SQL text. `src/core/` — where
this is enforced — has no package references and no database access at all, so the guarantee
doesn't depend on anything it can't see.

Underneath, **PostgreSQL row-level security** is the backstop. Even a bug in all of the above
returns only rows the current principal may read. The two mechanisms answer different questions:
RLS decides which *rows* come back; the policy engine decides which *measures* may be asked for —
and, unlike RLS, it can say why. An empty chart and "you may not see this" are not the same answer.

---

## Running it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Docker.

```bash
cp .env.example .env         # then fill in the two passwords
docker compose -f docker-compose.local.yml up -d
dotnet run --project src/app
```

The app migrates and seeds itself on first run in development. Open <http://localhost:5199>.

A banner across the top switches between three seeded identities. **It has no password and is
disabled outside development** — see `A12` in [CLAUDE.md](CLAUDE.md).

| Who | Sees | Blocked from |
|---|---|---|
| **Dana** — Analyst | sales and headcount, every region | any salary measure |
| **Marcus** — Regional Manager, North | everything for North | other regions' rows |
| **Priya** — HR Director | compensation, attrition, headcount | — |

### Three questions worth asking

1. `revenue by region, last quarter` — ask it as all three. Marcus gets one row and is told why.
2. `average salary by department` — Dana is refused by name; Priya gets the chart.
3. `turnover cost vs the D&I program budget` — spans two models, two queries, one report.

---

## Using a real local model

The default planner is scripted: a fixed question→plan table with no model behind it. That keeps
the demo runnable anywhere and keeps the governance tests free of a model's variability.

For the real thing:

```bash
ollama pull qwen2.5
```

```jsonc
// appsettings.Development.json
{ "Ai": { "Provider": "ollama", "Model": "qwen2.5" } }
```

LM Studio and llama.cpp work too — set `"UseOpenAiCompatibleApi": true`.

**It will not fall back.** If the model server isn't reachable you get an error saying so, never a
scripted answer dressed up as a local one. The provider is named under every report for the same
reason.

---

## Tests

```bash
dotnet test
```

72 tests. The ones that matter aren't about rendering:

| | |
|---|---|
| `RlsTests` | the same query, three principals, three row counts — against a real Postgres |
| `PrincipalLeakTests` | a pooled connection carries no identity into the next request |
| `RlsCoverageTests` | fails the build if any table lacks RLS, or any view lacks `security_invoker` |
| `PlannerContractTests` | hostile model output dies at the validator |
| `GovernanceEndToEndTests` | an Analyst asking for salaries gets a refusal and **no compiled SQL at all** |

`RlsTests`, `PrincipalLeakTests` and `RlsCoverageTests` need Docker and **fail rather than skip**
without it. Row-level security can't be verified against a fake database, and a security test that
quietly skips still reports green.

### Proving it without the app in the picture

```bash
psql "postgresql://smartreport_app:…@localhost:5433/smartreport"
```
```sql
SET app.role = 'analyst';                       SELECT count(*) FROM orders;  -- 920
SET app.role = 'regional_manager';
SET app.region_id = '1';                        SELECT count(*) FROM orders;  -- 303
RESET app.region_id;                            SELECT count(*) FROM orders;  -- 0, not 920
SET app.role = '';                              SELECT count(*) FROM orders;  -- 0
DELETE FROM orders;                             -- ERROR: permission denied
```

The last two are the interesting ones. `current_setting(…, true)` returns NULL when unset, and
`set_config` can't store NULL, so "no role" arrives as an empty string — a policy that checked only
`IS NOT NULL` would wave an unconfigured connection straight through. It did, until
`RlsTests.An_unconfigured_connection_sees_nothing` caught it.

---

## Layout

```
CLAUDE.md     Safe Build Pack v2.1, adapted for ASP.NET Core — the rules this repo is held to
src/app/      Razor Pages, the SSE endpoint, security middleware — presentation only
src/core/     catalog, specs, policy, SQL compiler, the pipeline — no I/O, BCL only
src/ai/       planner implementations: scripted, and a local model over HTTP
src/data/     EF Core + Npgsql: entities, migrations, RLS policies, seed
tests/        the governance suite
docs/         motion.md (every animation), vendor.md (the pinned front-end libraries)
```

Dependencies point inwards — `app` → `core`/`ai`/`data`, and `core` → nothing.
`core` holds the pipeline as well as the rules, because the order of
plan → validate → authorize → compile → execute *is* the guarantee; it isn't a detail of how a
web request happens to be handled. `ArchitectureTests` fails the build if `core` ever acquires a
dependency.

## Notes for anyone extending this

- **Widening what it can answer means adding to `DataCatalog`.** That's the only way, by design.
  An off-catalog question gets an honest refusal rather than an improvised query.
- **A new table needs RLS, forced, with per-action policies, in the migration that creates it.**
  `RlsCoverageTests` will fail if you forget.
- **Every policy must deny on an unset `app.*`.** See the note above about empty strings.
- GSAP, Lenis and Three.js are vendored in `wwwroot/lib/` because the CSP allows no third-party
  script origins. `docs/vendor.md` has the versions and source URLs.

## This is a demo

It is not production software. The sign-in switcher is unauthenticated, the data is invented, and
nothing here has been through a security review. The patterns are the point; the code is an
illustration of them.
