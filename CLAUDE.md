# Smart Report

A demo: clients of a fictional 3PL (Northpoint Fulfillment) ask for a report in plain language, a
model plans it, and the answer is bounded by permissions that already exist in the database.

**Stack:** ASP.NET Core 10 (Razor Pages) · EF Core 10 + PostgreSQL 17 · Tailwind v4 via the
standalone CLI (no npm) · hand-rolled inline-SVG charts · any model through
`Microsoft.Extensions.AI` (scripted by default, `groq`/`anthropic`/`openai`/`ollama` by config).

```
src/app/     Razor Pages, the SSE endpoint, security middleware — presentation only
src/core/    catalog, specs, policy, SQL compiler, ReportPipeline — NO I/O, BCL only
src/ai/      IReportPlanner + IReportNarrator: scripted, and any provider via IChatClient
src/data/    EF Core + Npgsql: entities, migrations, RLS policies, seed
tests/       xUnit — the governance suite
```

**Dependencies point inwards.** `app` → `core`, `ai`, `data`. `ai` → `core`. `data` → `core`.
`core` → nothing. `ArchitectureTests` fails the build if that stops being true.

`core` holds the pipeline as well as the rules, because the *order* of plan → validate →
authorize → compile → execute is the guarantee, not a detail of how a web request is handled.
`app` keeps only what genuinely needs `HttpContext`: the pages, the SSE endpoint, the security
headers and the cookie-to-principal lookup.

## The one rule this project exists to demonstrate

**The model never writes SQL.** It emits a JSON `ReportSpec`, which is validated against a
catalog, authorized by `PolicyEngine`, and compiled to parameterized SQL by `SqlCompiler` using
identifiers read from catalog metadata. Question text reaches the database only as parameter
*values*.

There is no fallback lane, no "advanced mode", no feature flag that lets model output become SQL
text. If you are ever tempted to add one, that is a change to the product's central claim — raise
it, don't implement it.

Underneath that, Postgres row-level security is the backstop: even a bug in all of the above
returns only rows the current principal may see.

---

# Safe Build Rules

_Adapted from **Safe Build Pack v2.1** by Nathaniel James, which was written for Next.js +
Supabase. Rule IDs and the **Why** of each rule are unchanged. **Dev note** has been re-pointed at
ASP.NET Core, and rules that needed interpretation carry a `Translated:` line saying how. Where no
rule clearly covers a case, follow the reasoning in the closest rule's **Why** and say which way
you went._

> **Instructions for the AI assistant:** Follow every rule below whenever you write, edit, or
> review code in this project. If the user asks for something that breaks a rule, do not silently
> comply. Explain the risk in one or two plain sentences and offer the safe way to do it.
>
> - Apply them to code you write or change. If existing code breaks a rule, leave it unless the
>   user asks; mention it once and offer to fix it.
> - Ask before acting only when the choice is hard to undo — a schema, auth, or policy change.
> - These rules take priority over style or structure preferences: a security gap costs more than
>   an untidy folder.

## Core (always on)

### C1. Keep secrets on the server
**Rule:** Never put API keys, connection strings, or tokens in anything the browser receives. Read
secrets only through `IConfiguration` in server code. Never serialize a config object into a
`<script>` block, a `data-` attribute, or `window.*`.
**Why:** Anything sent to the browser can be read by every visitor. Anyone can open their browser
tools and copy the key.
**Translated:** Razor Pages renders on the server, so there is no `NEXT_PUBLIC_` equivalent to
police. The equivalent hazard here is hand-rolled config blobs passed to client JS, and connection
strings in a committed `appsettings.json`.
**Dev note:** `dotnet user-secrets` in development, environment variables in production. Client JS
gets values through explicit, named, non-secret attributes on an element — never a config dump.

### C2. Never commit secrets
**Rule:** Keep real secrets in user-secrets or `.env` (git-ignored). Commit
`appsettings.example.json` with names and empty values. Never hardcode a password or key in source,
even "just for testing." If a secret was ever committed, rotate it — deleting it is not enough.
**Why:** Bots scan GitHub for leaked keys within minutes.
**Dev note:** `appsettings.Development.json` and `.env` are git-ignored. Removing a secret in a
later commit leaves it in git history; rotation is the only real fix.

### C3. Every server entry point checks who is calling
**Rule:** Every Razor Page handler (`OnGet`, `OnPost`, …) and every endpoint that reads or changes
private data must call `RequireUser()` **inside the handler**. Do not rely only on `[Authorize]`,
on routing, or on hiding things in the UI.
**Why:** Hiding a button doesn't stop someone from calling the code behind it directly.
**Translated:** The pack's warning about Server Actions compiling to public POST endpoints applies
exactly to Razor Page handlers — `OnPostDeleteAsync` is a public POST at
`/page?handler=Delete`. `[Authorize]` is the outer layer, never the only one.
**Dev note:** `RequireUser()` lives in `src/app/Security/`. It redirects for pages and returns 401
JSON for API endpoints. Anything deliberately public carries a comment saying why.

### C4. Validate all input on the server
**Rule:** Validate every bound model, route value, and query parameter on the server with
FluentValidation. Reject unexpected fields. Never trust an ID, price, role, or region sent from the
browser — read the principal from the session.
**Why:** Anyone can edit what their browser sends. If the server trusts a price or user ID from the
browser, people can change it.
**Dev note:** Mark anything not user-supplied `[BindNever]`; model-bind to purpose-built DTOs rather
than entities, so over-posting can't reach a column you didn't mean to expose.

### C5. Show safe error messages
**Rule:** Never send stack traces, database errors, or internal details to the user. Log the full
error server-side and return a short generic message.
**Why:** Detailed errors tell attackers how your app is built and where to poke.
**Dev note:** `UseExceptionHandler` + `ProblemDetails`. The developer exception page is
development-only. A `PolicyDecision` denial is **not** an error — it's a deliberate, user-facing
message and should stay specific.

### C6. No raw HTML or string-built queries
**Rule:** Avoid `@Html.Raw`. Never build SQL by joining strings with user input. Use EF Core's
query builder or fully parameterized SQL.
**Why:** Raw HTML from users can run scripts in other people's browsers. String-built queries let
attackers run their own database commands.
**Translated:** This project's entire `SqlCompiler` design is this rule taken seriously — see "The
one rule this project exists to demonstrate" above. Razor auto-encodes output by default.
**Dev note:** When raw SQL is unavoidable, every value is an `NpgsqlParameter` and every identifier
comes from `DataCatalog`, never from a string the user or the model supplied.

### C7. Rate limit anything expensive or sensitive
**Rule:** Rate limit sign-in, and **every endpoint that spends model inference** — above all
`/api/reports/ask`.
**Why:** Without limits, one person or bot can run up your AI bill, or try thousands of passwords.
**Dev note:** `AddRateLimiter` with a fixed window keyed by user ID, and a stricter anonymous limit
by IP. A local model costs GPU time and wall-clock rather than API credits, but an unbounded
endpoint is still trivially abusable.

### C8. Set security headers
**Rule:** Set `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`,
`X-Frame-Options: DENY` (and CSP `frame-ancestors 'none'`), `Strict-Transport-Security`, and a
Content Security Policy with no `unsafe-inline`.
**Why:** These stop common browser attacks, like other sites loading yours in a hidden frame to
trick users into clicking.
**Dev note:** **This is why Tailwind is built by a standalone binary into `wwwroot/css/app.css`
rather than loaded from a CDN.** A CSP worth having doesn't allow arbitrary third-party script or
style origins. Inline scripts and styles need a per-request nonce. Class names must appear
literally in source — Tailwind generates by scanning text, so `fill-series-${i}` produces a class
that does not exist.

### C9. Be careful with packages
**Rule:** Only install well-known, actively maintained packages. Confirm a package exists on
nuget.org under that exact name and is current before adding it. Don't add a dependency for
something a few lines of code can do.
**Why:** AI tools sometimes invent package names, and attackers publish malware under those names.
**Dev note:** Versions live in one place, `Directory.Packages.props`, so the whole dependency set
is readable at a glance. It is deliberately short: `Npgsql.EntityFrameworkCore.PostgreSQL`,
`Microsoft.EntityFrameworkCore.Design`, `FluentValidation`, `Microsoft.Extensions.Options`,
`Microsoft.Extensions.Logging.Abstractions`, and for the planner `Microsoft.Extensions.AI`,
`Microsoft.Extensions.AI.OpenAI` and `Anthropic` — three packages that buy provider independence
and native structured output, which is what makes an invented metric key impossible rather than
merely rejected. Plus test-only `xunit`,
`Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`, `Npgsql`. Charts are hand-rolled
SVG rather than a charting library, for this reason among others. `src/core/` has **zero** package
references and must stay that way — `ArchitectureTests` enforces it.

### C10. Never disable security to make an error go away
**Rule:** When a security control blocks something, find the cause and fix it there. Don't turn off
RLS, drop an auth check, suppress a nullable warning, or widen CORS to get something working.
**Why:** The quick fix that makes the error vanish is often the one that leaves the app wide open.
**Dev note:** `permission denied` or an unexpectedly empty result means a **missing or wrong
policy**, or an unset `app.*` setting — not a reason to disable RLS. `core/` builds with
`TreatWarningsAsErrors`; fix the cause rather than the warning.

### C11. Explain security-relevant changes
**Rule:** Whenever you change auth, database access, RLS policies, configuration, or the catalog,
finish with a short plain-English summary: what changed, who can now access what, and any risk.
**Why:** You should always know who can see and change your users' data.

---

## Login & user accounts

_**Translated:** the pack's A-rules are written for Supabase Auth. This project uses ASP.NET Core
cookie authentication, so each rule below keeps its intent and drops the Supabase mechanics._

### A1. One way to build the principal
**Rule:** Authentication state comes from the auth cookie. Authorization state — the client
account and the role — is loaded from the database per request by `PrincipalContext` /
`PrincipalDirectory`. Never hand-roll a second path.
**Why:** The right setup keeps the session in secure cookies and the permissions in one place.
**Dev note:** Cookie auth with `HttpOnly`, `Secure`, `SameSite=Lax`, signed and encrypted by ASP.NET
Core Data Protection.

### A2. Verify the user on the server
**Rule:** When server code decides whether someone may see or change data, resolve the principal
server-side through `PrincipalContext`. Never make an access decision from an unvalidated cookie
value, a claim the client could influence, or anything the model returned.
**Why:** Cookies can be faked. The decision must be made against something the server trusts.
**Translated:** Supabase's `getUser()`-not-`getSession()` distinction maps to: the cookie proves
*identity*; the database supplies *authority*.

### A3. Protect in layers with one helper
**Rule:** `[Authorize]` on the page model is the outer layer. `RequireUser()` inside every handler
is the real one. Both.
**Why:** If one layer is skipped or broken, the other still protects your users.
**Dev note:** Pages redirect to sign-in; API endpoints return 401 JSON.

### A4. Take the user ID from the session only
**Rule:** Never accept a user ID, client ID, or role from the request body, route, or query to
decide whose data to read. Take it from `PrincipalContext`. This includes the conversational
context a follow-up sends: it steers the planner and is re-validated and re-authorized like any
other plan.
**Why:** If the app trusts an ID from the browser, anyone can swap in someone else's ID and see
their data.
**Dev note:** This is IDOR (OWASP A01). It is also exactly what `app.client_id` would leak if it
were ever set from a request value rather than from the resolved principal.

### A5. Never let the user supply their own role
**Rule:** Roles live in the `client_users` table and are loaded server-side on every request. The
cookie carries a principal key only. Never store a role, plan, or permission in a place the user can edit,
and never trust a role claim baked into a cookie at sign-in time.
**Why:** Users can edit anything they hold, so they could make themselves an admin.
**Translated:** Supabase's `user_metadata` vs `app_metadata` split maps to: identity in the cookie,
authority in the database. A role claim minted at sign-in is also **stale** after a permission
change, which is its own bug.

### A6. Elevated database access is explicit and rare
**Rule:** The app connects as `smartreport_app`, a non-owner role with RLS enforced. The owner
connection string is for migrations only, never for request handling.
**Why:** The owner role skips row-level security. Whoever has it can read everything.
**Translated:** Supabase's `service_role` key maps to the Postgres owner role here.

### A7. Make redirects safe
**Rule:** Validate any `returnUrl` with `Url.IsLocalUrl()`; otherwise redirect to `/`.
**Why:** Otherwise attackers can send your users a real login link that drops them on a fake site
afterward.

### A8. Strong sign-in settings
**Rule:** Not applicable to the demo's dev-only switcher. **If real authentication is ever added:**
minimum 8-character passwords, confirmation, lockout on repeated failure.
**Translated:** Kept as a placeholder so the gap is visible rather than forgotten.

### A9. Don't reveal who has an account
**Rule:** Sign-in and reset messages read the same whether or not the account exists.
**Why:** Otherwise attackers can check which emails are signed up to your app.

### A10. Never cache one user's data for everyone
**Rule:** Every authenticated route and the SSE stream send `Cache-Control: no-store`. Never add
output caching or response caching to anything that reads the principal.
**Why:** A caching mistake can show one person's account page to someone else.
**Dev note:** Only `/` is cacheable. `/portal/*` and `/api/reports/*` are not.

### A11. Log out properly
**Rule:** `SignOutAsync` then redirect. After logout, private pages must not be reachable, including
with the back button.
**Why:** On a shared computer, the next person shouldn't be able to press Back and see the previous
user's account.

### A12. The dev sign-in switcher never ships
**Rule:** `/dev/signin-as/{key}` has no password. It returns 404 unless
`IHostEnvironment.IsDevelopment()`, and the UI shows a visible banner whenever it is reachable.
**Why:** It is an unauthenticated endpoint that hands out any identity. In production it would be a
complete authentication bypass.
**Translated:** New rule, no pack equivalent — added because this demo deliberately ships an
insecure affordance and the risk must be written down rather than remembered.

---

## Database (PostgreSQL)

_**Translated:** the pack's D-rules are written for Supabase Postgres. The mechanism — row-level
security — is identical; only the client and the dashboard differ. `auth.uid()` becomes
`current_setting('app.*')`, set per request inside a transaction._

### D12. The model may choose a shape, not assert a fact
**Rule:** A requested chart form is honoured only where the data supports it. A line across
unordered categories, or a ring with too many slices, is replaced — and the substitution is shown
on the panel, never silent.
**Why:** A chart is a claim. A line between carriers claims the gap between them means something.
**Dev note:** The rule lives in `ChartChoice` in `core` and is covered by `ChartChoiceTests`.

### D1. Turn on RLS for every table, in the same migration
**Rule:** Every table gets `ENABLE ROW LEVEL SECURITY` **and** `FORCE ROW LEVEL SECURITY`, with its
policies, in the same migration that creates it. Never disable RLS.
**Why:** Without RLS, any bug above the database exposes the whole table.
**Dev note:** `FORCE` matters here: without it the table owner bypasses policies, and migrations run
as the owner. `RlsCoverageTests` fails the build if any public table lacks RLS.

### D2. Write one policy per action, deny by default
**Rule:** Separate policies for `select`, `insert`, `update`, `delete`, scoped `TO smartreport_app`.
Every policy must treat an **unset** `app.*` setting as deny.
**Why:** Each person can only see and change their own rows.
**Dev note:** `current_setting('app.role', true)` returns NULL when unset — a policy that doesn't
check for NULL **grants everything to an unconfigured connection**. Always:
```sql
USING (current_setting('app.role', true) IS NOT NULL AND ( … ))
```

### D3. Only make data public on purpose
**Rule:** Never write `USING (true)` for private data. Reference data that is genuinely shared
(`report_templates`) may be readable, scoped to `select` only, with a comment saying why.
**Why:** "true" means everyone.

### D4. Keep owner access out of normal queries
**Rule:** Request handling uses the `smartreport_app` connection so RLS applies. The owner
connection is used only by `dotnet ef database update` and the seeder.
**Why:** The owner skips RLS completely, so a bug there affects every row.

### D5. Make views respect RLS
**Rule:** Every view is created `WITH (security_invoker = true)`. Prefer no views at all.
**Why:** By default a view runs as its owner and ignores your privacy rules, creating a hidden back
door.
**Dev note:** Postgres 15+. This is not optional and not cosmetic — a `security_definer` view over
an RLS-protected table silently returns every row.

### D6. Principal settings are transaction-scoped
**Rule:** Set `app.*` with `SET LOCAL` inside a transaction, never with plain `SET`.
**Why:** Connections are pooled and reused. A session-level setting leaks the previous request's
identity to whoever gets that connection next.
**Translated:** No pack equivalent — Supabase's PostgREST handles this. We do it ourselves, so the
hazard is ours. `PrincipalLeakTests` asserts a returned connection carries no `app.*` setting.

### D7. Query safely
**Rule:** Explicit column lists, always. Never `SELECT *`. Values are parameters, identifiers come
from `DataCatalog`.
**Why:** Returning whole rows can leak columns you forgot were there.

### D8. Schema changes go in migrations
**Rule:** Every schema change — including RLS policies — is an EF Core migration. Never change
schema by hand against a running database. Destructive SQL needs explicit confirmation.
**Why:** Migrations are a record of every change, so you can review security and undo mistakes.

### D10. Aggregates across clients carry a minimum cohort
**Rule:** Any figure computed across more than one client must come from a pre-aggregated table
with no `client_id` column, and every query against it carries a minimum-cohort predicate —
declared as a `MandatoryPredicate` on the catalog model **and** repeated in the table's policy.
Never compute a peer figure by aggregating live client rows.
**Why:** "The median of three clients" is a thin disguise over three named companies. Row-level
security answers *whose rows*; it does not answer *what can be inferred from a total*.
**Translated:** No pack equivalent — the pack covers row access, not disclosure. Added because
this product answers comparison questions, which is exactly where that distinction bites.
**Dev note:** The floor is `DataCatalog.MinimumCohortSize`, mirrored in `RowLevelSecurity`.
`BenchmarkDisclosureTests` asserts the predicate is present in every compiled benchmark query,
that sub-floor rows are unreadable, and that such rows exist — otherwise the test passes against
an empty table and proves nothing.

### D11. Northpoint's own data is not client data
**Rule:** Tables holding the operator's commercial information (`carrier_contract_rates`) have no
`client_id` and require a role no client user holds. A client asking for one gets a refusal with
an authorized alternative, never a filtered-to-empty result.
**Why:** The portal reads the same database the business runs on. What is safe for a customer to
see is a smaller set than what is there.

### D9. Check before you ship
**Rule:** `RlsCoverageTests` runs the "tables without RLS" query and fails if anything is
unprotected. After any schema change, run the test suite and sign in as a second principal to
confirm the boundary holds.
**Why:** Testing as another user is the fastest way to catch a leak before your users do.
**Translated:** The pack makes this a manual pre-launch step with the Supabase Security Advisor.
Here it is an automated test, so a new table cannot ship unprotected even if nobody remembers to
look.

---

## Before you finish

Check your work against this list, fix anything that fails, then tell the user in two to four plain
sentences what changed, what you checked, and anything they need to decide.

- [ ] No secrets in committed config, in client JS, or hardcoded anywhere.
- [ ] Every handler touching private data resolves the principal through `PrincipalContext`;
      client and role never come from the request.
- [ ] All input validated server-side; DTOs, not entities, bound from requests.
- [ ] Every new table has RLS **enabled and forced** with per-action policies in the same
      migration, and every policy denies on unset `app.*`.
- [ ] Any cross-client aggregate has a minimum-cohort floor in the compiler *and* the policy.
- [ ] A refusal gives a reason, and a remedy the asker is actually allowed to use.
- [ ] Every new view is `security_invoker = true`.
- [ ] No string-built SQL. No new path from model output to SQL text.
- [ ] `no-store` on authenticated routes; nothing user-specific is cached.
- [ ] New endpoints that cost inference are rate limited.
- [ ] CSP still passes with no new CDN origins and no `unsafe-inline`.
- [ ] Error messages carry no stack traces or database details.
- [ ] `dotnet test` green, including `RlsCoverageTests` and `PrincipalLeakTests`.
- [ ] Security-relevant changes summarized in plain English.

_Safe Build Pack v2.1 by Nathaniel James, adapted for ASP.NET Core._
