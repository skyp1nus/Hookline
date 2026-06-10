> **Usage note for the coding agent:** This project was renamed **DanielHub → Hookline**. Read every "DanielHub" below as "Hookline" — namespaces `Hookline.*`, solution `Hookline.slnx`, host `Hookline.Host`, modules `Hookline.Modules.*`. Production domain is `hookline.danielhub.dev` (dev = localhost). This document is the **authoritative backend spec**; build backend contracts from it, not from the phase brief.

---

# DanielHub — architecture guide

> A living reference for building DanielHub as a **modular monolith** that grows tool-by-tool. Today: **YouTube Uploads** (built on the shared kernel — Phase 1). Next: **YouTube Comments** (Phase 2). Tomorrow: LinkedIn, a public Website, and more. The whole point of this architecture is that adding the *next* tool is cheap and never touches the previous ones.

---

## 1. Goals & guiding principles

- **One platform, many tools.** A single deployment hosting many isolated modules behind a shared shell.
- **Each tool is a module.** It owns its own domain, data, endpoints, and jobs, and plugs into shared services — it does not reach into other modules.
- **Build the foundation once.** Auth, connections, settings, background jobs, logging, and the UI shell are built once and consumed by every module.
- **Cheap to extend.** Adding a module should cost exactly: one backend module project + one frontend feature folder + one nav-registry entry. Zero edits to existing modules.
- **Keep the exit door open.** Modules are decoupled enough that any one could later be extracted into its own service if it ever needs to scale independently — but we don't pay that cost now.

Five rules that follow from this: clear module boundaries, a shared kernel for cross-cutting concerns, schema-per-module data isolation, in-process events over direct coupling, and a single deploy.

---

## 2. The big picture

- **One ASP.NET (.NET 10) host** = HTTP API + Hangfire worker + module registrations, all in one process.
- **One Next.js (Bun) frontend** = the shell (`sidebar-07`) + per-module feature areas, with a BFF in front of the backend.
- **Shared infrastructure** on the existing Hetzner `web` network: PostgreSQL (one DB, schema per module), Redis (prefixed keys), Caddy (TLS + routing).
- **One domain:** `danielhub.dev`. The old `slacktube.danielhub.dev` 301-redirects into the hub.

The flow is: browser → shared Caddy → Next.js hub (shell + BFF) → ASP.NET host (shared kernel + modules) → Postgres / Redis / external APIs.

---

## 3. Backend solution structure

The pattern is **vertical-slice modules + a shared kernel + a thin host**. Each module is its own project so that `internal` actually hides things and boundaries can be enforced by tests.

```
backend/
  DanielHub.slnx
  src/
    DanielHub.Host/                    # the ONLY runnable project (Web SDK)
      Program.cs                       # composition root: build shared services, then load modules
      appsettings*.json

    DanielHub.SharedKernel/            # contracts + cross-cutting abstractions; references NOTHING module-specific
      Modules/IModule.cs               # the module registration contract
      Connections/                     # ISlackConnections, IGoogleConnections, IConnectionCatalog
      Auth/                            # ICurrentUser + identity contracts
      Messaging/IEventBus.cs           # in-process integration events
      Secrets/ISecretProtector.cs
      Settings/ISettingsStore.cs
      Jobs/IJobScheduler.cs
      Persistence/                     # base DbContext conventions, EncryptedString converter, design-time helpers
      Common/                          # ProblemDetails, paging envelope, Result types

    DanielHub.Infrastructure/          # shared IMPLEMENTATIONS of the SharedKernel abstractions
      Persistence/                     # naming conventions, migrations-history helper
      Secrets/                         # AES-GCM protector
      Connections/                     # OAuth flows, encrypted token store, refresh, dispatcher
      Auth/                            # users + BFF session backing
      Settings/  Jobs/  Logging/  Messaging/

    Modules/
      YouTubeUploads/                  # DanielHub.Modules.YouTubeUploads.csproj   (BUILT — Phase 1)
        YouTubeUploadsModule.cs        # implements IModule; runs on the shared kernel
        Domain/                        # UploadJob, JobState machine, ...
        Features/                      # ingest, status, upload orchestration
        Infrastructure/                # YouTubeUploadsDbContext (schema: youtube_uploads), Migrations/, Google (Drive + YouTube)
        Endpoints/                     # mapped under /api/youtube-uploads
        Jobs/                          # ingest + upload handlers
      YouTubeComments/                 # DanielHub.Modules.YouTubeComments.csproj  (Phase 2 — planned)
        YouTubeCommentsModule.cs       # implements IModule
        Domain/                        # entities, enums
        Features/                      # ApiKeys, Channels, Mappings, Dashboard, Logs (handlers + DTOs)
        Infrastructure/                # YouTubeCommentsDbContext (schema: youtube_comments), Migrations/, YouTube client
        Endpoints/                     # mapped under /api/youtube-comments
        Jobs/                          # polling job + dynamic scheduler
      # future: LinkedIn/ , Website/ , ...

  tests/
    DanielHub.ArchitectureTests/       # enforces module boundaries (fails CI on illegal references)
    DanielHub.Infrastructure.Tests/    # shared-kernel impls (e.g. the AES-GCM secret protector)
    YouTubeUploads.Tests/
    YouTubeComments.Tests/
```

**Reference rules (enforced by tests):**

- `Host` is the only Web SDK project. It references `Infrastructure` and **every** module.
- Every module references `SharedKernel` (and `Infrastructure` only if it truly needs a shared implementation type).
- **No module ever references another module.**
- `Domain` folders have no infrastructure dependencies.

This is actually *simpler* than the legacy Comment Bridge repo's 5-project clean architecture: those five projects collapse into **one** module project with internal `Domain/Features/Infrastructure/Endpoints` folders. Clean-architecture layering still exists — it's just folders inside the module rather than separate solution projects. The win is that cross-*module* isolation is now real (assembly-level), which matters far more once there are 5–10 modules.

---

## 4. The module contract

Every module implements one interface. The host discovers modules from an explicit list (predictable and debuggable — preferred over reflection scanning).

```csharp
public interface IModule
{
    string Name { get; }                                            // "comment-bridge"
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(IEndpointRouteBuilder endpoints);             // routes live under /api/{Name}
    void RegisterJobs(IJobScheduler scheduler);                     // recurring jobs
    IEnumerable<ConnectionRequirement> RequiredConnections { get; } // declares Slack / Google / etc. it needs
    DbContext? Migrate(IServiceProvider sp);                        // returns its context so the host can apply migrations
}
```

`Program.cs` becomes a short, readable composition root:

1. Build shared services (DB, Redis, secret protector, connections, auth, settings, Hangfire, logging, event bus).
2. Instantiate the explicit module list and call `RegisterServices` on each.
3. Build the app; apply each module's migrations; call `MapEndpoints` and `RegisterJobs`.

Adding a module to the system is one line in that explicit list.

---

## 5. Module boundaries & communication

A module never references another module's assembly. Cross-module interaction, in strict order of preference:

1. **Nothing.** Independent modules are the goal.
2. **Shared-kernel services.** Both YouTube Comments and YouTube Uploads consume `ISlackConnections` — that is module→shared, not module→module. Fine.
3. **In-process integration events** via `IEventBus`. Example: Connections publishes `SlackWorkspaceDisconnected(workspaceId)`; any module holding mappings to that workspace reacts and deactivates them. Events carry only IDs/primitives and live in `SharedKernel`.
4. **A public contract interface in `SharedKernel`** for a direct synchronous call — last resort, since it couples lifecycles.

Boundaries are enforced by `DanielHub.ArchitectureTests` (NetArchTest or ArchUnitNET) so an illegal reference fails CI rather than slipping in. This discipline is exactly what keeps "extract this module into its own service later" a realistic option: swap the in-process `IEventBus` for a real broker, swap a shared-DB read for an API call, and the module barely changes.

---

## 6. The Connections subsystem — the heart of the platform

This is the single most valuable shared part. **Connect an external account once; consume it from any module.** Everything else is plumbing around this.

**Connection types today:** Slack workspace (OAuth v2 bot token), Google account (OAuth refresh token → YouTube + Drive scopes), YouTube Data API key (YouTube Comments polling). **Future:** LinkedIn (OAuth2), a generic OAuth2 connection, and whatever a "Website" module needs (hosting/CMS credentials, or nothing).

**Responsibilities:** OAuth start/callback flows, encrypted token storage, token refresh, scope tracking, listing + health, and revoke/disconnect. It publishes events on connect/disconnect.

**Public surface (in `SharedKernel`):** typed accessors such as `ISlackConnections.GetBotTokenAsync(workspaceId)`, `IGoogleConnections.GetCredentialAsync(accountId)`, `IConnectionCatalog.List(type)`. A module declares its `RequiredConnections` and resolves a token at job time — it never touches token storage directly.

**Storage:** a `connections` schema (workspaces, google_accounts, api_keys), with every secret encrypted via the shared `ISecretProtector`.

**Slack app strategy (an important decision):**
- *Target:* **one hub Slack app** with one set of provider endpoints (`/slack/events`, `/slack/interactivity`, `/slack/oauth/*`) and a **dispatcher** that routes each inbound event to the module(s) that subscribed (by event type / team / channel). The Connections subsystem owns the app credentials and the raw endpoints; modules subscribe to event streams.
- *Interim:* keep each module's existing Slack app with module-scoped paths (`/slack/{module}/events`…) but still store the resulting workspace + token in the shared Connections store, so the UI is unified even before the wiring is.
- Move to the single-app dispatcher as part of consolidation, since the whole reason Slack lives in Connections is that it's a *shared* connection.
- Distribution detail to remember: installing into a foreign workspace uses the **sharable OAuth-start URL from the backend** (Manage Distribution), *not* the workspace-scoped marketplace URL.

**Google strategy:** one Google Cloud project with **Internal** OAuth consent on an org Workspace account — this bypasses External verification and the CASA assessment entirely, on the hard condition that all YouTube channels and Drive accounts live in that same Workspace domain. One OAuth client; the subsystem stores a refresh token per account; modules request only the scopes they need (Drive readonly + YouTube upload for YouTube Uploads). Keep the distinction sharp: `videos.insert` requires **OAuth2, not an API key** — API keys are only valid for comment monitoring (YouTube Comments).

---

## 7. Data & persistence

- **One PostgreSQL database, schema per module** (`youtube_uploads`, `youtube_comments`, …) plus shared schemas (`connections`, `auth`, `shared`). Isolation without the operational overhead of many databases.
- **Each module owns its own EF Core `DbContext`** mapped to its schema, with a **separate migration history**: set `MigrationsHistoryTable("__ef_migrations_history", "<schema>")` per context so histories don't collide.
- **Conventions in `SharedKernel.Persistence`:** snake_case naming (EFCore.NamingConventions), UTC timestamps, an `EncryptedString` value converter that runs values through `ISecretProtector`, and base entity conventions.
- **Migrations on startup:** the host iterates each module's `DbContext` and runs `Migrate()` (order-independent because schemas are isolated). Provide a design-time factory per context for `dotnet ef`.
- **No cross-schema joins.** If a module needs another's data, go through a contract or an event — never a SQL join. This keeps boundaries real and keeps extraction possible.

---

## 8. Background jobs & ephemeral state

- **One Hangfire server**, hosted in the host process, with PostgreSQL storage (durable; survives restarts). Modules register recurring jobs via `IJobScheduler` and enqueue work as needed. The dashboard at `/hangfire` is secured behind admin auth.
- **Job ownership:** YouTube Comments runs a recurring poll *per active mapping* (a dynamic scheduler keeps Hangfire's recurring jobs in sync with the mappings table). YouTube Uploads is event-driven (Slack event → enqueue ingest → enqueue upload) as a durable pipeline with cancel/confirm.
- **Redis (StackExchange.Redis)**, one shared connection, with a **key prefix per module** to prevent collisions: `ytu:*` (event-id dedup TTL, per-project daily quota, cancel flags, Slack status-message ts), `ytc:*`, `conn:*`, `auth:*`. The canonical prefix registry is `docs/redis-key-prefixes.md` (also Appendix B). Redis holds **only** ephemeral/fast state; anything durable belongs in Postgres.
- **Quota tracking:** per-connection daily counters in Redis (Pacific-Time day for YouTube), surfaced in the UI as progress bars. Capacity-plan against **current** quota costs — the December-2025 `videos.insert` change dramatically lowered its cost, so old documentation will mislead you. At current volumes the free default is comfortable.

---

## 9. Secrets & security

**One secret protector for the whole hub.** Two viable mechanisms:

- **AES-256-GCM with a master key from env** (`TokenEncryption__Key`): stateless, no key-ring to persist, matches the volume-less deploy. Downside: rotating the master key invalidates all stored tokens. *(Carried over from the legacy SlackTube source repo — this is what YouTube Uploads uses today, with a 1-byte version header so the ciphertext format can rotate later.)*
- **ASP.NET Data Protection with the key-ring in Postgres:** supports automatic key rotation. Downside: a key-ring table to manage. *(The legacy Comment Bridge source repo's approach.)*

**Recommendation:** standardize on **AES-GCM env-key** for simplicity and statelessness (the hub already relies on it — YouTube Uploads stores its secrets this way). When folding the legacy Comment Bridge source into the YouTube Comments module, run a one-time migration that decrypts its Data-Protection-protected secrets and re-encrypts them under the shared protector. **Never change the master key after launch.**

Everything else: all external secrets (Slack bot tokens, Google refresh tokens, YouTube API keys) encrypted at rest via the protector + `EncryptedString` converter; Slack request-signature verification on webhooks; OAuth `state` validation (CSRF) with short-TTL state cookies; least-privilege OAuth scopes per connection.

---

## 10. Auth & identity

One sign-on for the whole hub, consolidating the two legacy source apps' approaches (the Comment Bridge repo: email+password + JWT; the SlackTube repo: admin user/pass + `X-Admin-Token` + BFF session).

**Recommended model:** a shared `auth` subsystem with a `users` table (bcrypt), using the **Next.js BFF session** as the browser-facing mechanism — an httpOnly session cookie minted by the BFF, which calls the backend server-side with an admin token/header. The backend stays stateless behind the BFF. Provider webhooks and OAuth callbacks bypass auth (they're signature/state-verified instead).

Start with a single admin role but design the `users` table to allow roles later — the hub will grow and multi-user is plausible. Module-level authorization is a simple policy per feature, checked in the endpoint.

**Routing nuance (critical, do not get this wrong):** `/api/*` is the **frontend BFF**, not the backend. Only the paths that *external providers* hit go directly to the backend through Caddy: `/slack/*`, `/google/*`, future `/linkedin/*`, and `/hangfire`.

---

## 11. HTTP & routing conventions

- **App API (through the BFF):** `/api/{module}/...` (e.g. `/api/youtube-uploads/jobs`, `/api/youtube-comments/mappings`), plus shared `/api/connections/...`, `/api/auth/...`, `/api/settings/...`, `/api/overview/...`.
- **Provider endpoints (backend-direct, fixed paths):** `/slack/events|interactivity|oauth/*`, `/google/oauth/*`, future `/linkedin/oauth/*`. Owned by the Connections subsystem (module-scoped in the interim).
- **Caddy — one site block for `danielhub.dev`:** route provider paths + `/hangfire` to the backend; everything else (including the `/api/*` BFF) to the frontend. This mirrors the legacy SlackTube repo's Caddy pattern.
- Define DTO casing, ProblemDetails errors, and the pagination envelope **once** in `SharedKernel` and reuse across all modules.

---

## 12. Frontend architecture — the shell + modules

One Next.js (App Router, Bun) app. The shell is shared; modules are feature areas that render into it.

```
web/src/
  app/                       # routes — thin; they delegate to features
    (auth)/login/
    (hub)/overview/
    (hub)/comments/{dashboard,feed,channels,mappings}/
    (hub)/uploads/{queue,history,mappings}/
    (hub)/connections/{slack,google,api-keys}/
    (hub)/{logs,settings}/
    api/                     # the BFF: [...path] proxy + login/logout route handlers
  components/
    ui/                      # shadcn (radix-nova)
    shell/                   # sidebar-07, header, breadcrumbs, command palette, user menu
  features/
    overview/  comments/  uploads/  connections/   # components, hooks (React Query), api client, types
  lib/
    api/                     # BFF client + ProblemDetails handling
    auth/                    # session helpers
    nav.ts                   # NAV REGISTRY — single source of truth for the sidebar + breadcrumbs
  hooks/                     # shared hooks
```

- **Nav registry (`lib/nav.ts`):** a typed array of groups → items → routes + lucide icons. The sidebar renders from it and breadcrumbs derive from it. Adding a module = adding one entry here plus its `features/` folder.
- **Standardize the stack across modules:** Next 16 + shadcn `radix-nova` + Tailwind v4 + lucide-react + React Query + sonner + next-themes; add Recharts for dashboards; zod for form/DTO validation. Reconcile the current drift — pick **radix-ui** (shadcn `radix-nova` builds on it) over `@base-ui`, and align lucide-react versions.
- **The BFF stays the security boundary:** browser → Next BFF (`/api/*`) → backend with a server-side admin token. The "Connect" buttons navigate the browser straight to the backend `/{provider}/oauth/start` via `NEXT_PUBLIC_BACKEND_URL` (build-time inlined), as the legacy SlackTube repo does.

---

## 13. Adding a new module — the playbook

This is the payoff. For a hypothetical module `Foo` (read: LinkedIn, Website, …):

**Backend**
1. Create project `DanielHub.Modules.Foo` (references `SharedKernel`, plus `Infrastructure` only if needed). Internal folders: `Domain / Features / Infrastructure / Endpoints / Jobs`.
2. Add `FooDbContext` mapped to schema `foo` with its own migrations-history table; add a design-time factory and an initial migration.
3. Implement `FooModule : IModule` — `RegisterServices`, `MapEndpoints` (under `/api/foo`), `RegisterJobs`, and declare `RequiredConnections`.
4. Add the module to the host's explicit module list (one line).
5. If it needs a new connection type, add it to the Connections subsystem (OAuth flow + token storage + typed accessor); otherwise reuse an existing one.
6. Add unit tests; the architecture-boundary test passes automatically.

**Frontend**
7. Add `features/foo/` (components, hooks, api client).
8. Add the route group `app/(hub)/foo/...`.
9. Add one entry to `lib/nav.ts`.

Done — **no existing module changes.** Concretely: a LinkedIn module is a new OAuth connection + posting/scheduling features; a "Website" module might serve or manage a public site and may need a new connection (hosting/CMS) or none at all.

---

## 14. Cross-cutting concerns

- **Config:** a strongly-typed `FooOptions` per module bound from its config section; shared options for DB / Redis / Slack / Google / Auth.
- **Observability:** Serilog structured logging to a shared sink, request logging, correlation IDs, health checks (`/health` covering DB + Redis), and the secured Hangfire dashboard.
- **Audit:** a shared `IAuditLog` + table; modules write entries through it, and the "Logs" page reads it with a per-module filter.
- **Errors:** ProblemDetails everywhere, surfaced consistently as sonner toasts on the frontend.
- **Resilience:** Polly pipelines for all upstream calls (YouTube / Slack / Google / LinkedIn) — reuse the pattern from the legacy Comment Bridge repo's `Resilience` folder.

---

## 15. Testing

- **Unit tests per module** (e.g. YouTube Uploads' template parser, YouTube Comments' quota-aware key selection).
- **Architecture tests** asserting: no module references another module; modules reference only `SharedKernel` (+ allowed infra); `Domain` has no infra dependencies.
- **Integration tests** with Testcontainers (Postgres + Redis) for endpoints and the job pipeline.
- **Frontend:** type-check + lint in CI; component/interaction tests where they earn their keep.

---

## 16. Deployment & ops

- **Fits the existing shared Hetzner stack:** shared Caddy + Postgres + Redis on the external `web` network. The hub adds **one backend container + one frontend container**, replacing the two app-pairs. One domain (`danielhub.dev`); redirect the old subdomain.
- **One Caddy site block** (provider paths + `/hangfire` → backend; the rest → frontend BFF), **one `.env`**. The backend runs every module's migrations on boot and self-heals interrupted jobs.
- **CI/CD — one pipeline:** `backend` (build/test the `.slnx` incl. architecture tests, .NET 10) → `frontend` (bun build) → `docker` (push both images on `main`) → `deploy` (scp compose + Caddy snippet, pull, `up -d`, hot-reload Caddy). Reuse the existing deploy-workflow shape.
- Generate fresh secrets; never reuse dev values; never change the master encryption key after launch.

---

## 17. Migration path (two apps → unified hub)

Path 1 is a real refactor, so do it in phases:

- **Phase 0 — Foundation.** Stand up `DanielHub.Host` + `SharedKernel` + `Infrastructure` + the Connections and Auth subsystems (skeletons) + the Next.js shell (`sidebar-07`, nav registry, BFF, theme).
- **Phase 1 — Absorb SlackTube first (DONE).** The legacy SlackTube source repo (already .NET 10, single-project, Redis + Hangfire) is now the **YouTube Uploads** module — `Hookline.Modules.YouTubeUploads`, schema `youtube_uploads`, Redis `ytu:*`, routes `/api/youtube-uploads/*`. Its Slack/Google accounts moved into the shared Connections store; secrets, settings, audit and jobs run on the shared kernel; the module contract is validated end-to-end.
- **Phase 2 — Absorb Comment Bridge into the YouTube Comments module** — `Hookline.Modules.YouTubeComments`, schema `youtube_comments`, Redis `ytc:*`, routes `/api/youtube-comments/*`. Bump the legacy Comment Bridge / YouTubeBridge source repo to .NET 10; fold its five clean-architecture projects into one module project (Domain/Features/Infrastructure/Endpoints folders); re-encrypt its Data-Protection secrets under the shared AES-GCM protector (one-time); move its YouTube API keys + Slack workspaces into Connections.
- **Phase 3 — Unify auth.** Single login; retire the two old deployments; point `danielhub.dev` at the hub; 301 the old subdomain.
- **Phase 4+ — Grow.** Add LinkedIn, Website, and the rest as new modules using the playbook in §13.

---

## 18. Decision log (the "why")

- **Modular monolith over microservices** — one deploy, shared auth/connections, simple ops at this scale; boundaries kept clean so extraction stays possible.
- **Project-per-module over folders-in-host** — real `internal` encapsulation + assembly-level boundary tests.
- **Schema-per-module over database-per-module** — isolation without many-DB overhead.
- **In-process events over direct calls** — decoupling + future extraction.
- **AES-GCM env-key over Data Protection** — stateless, matches the volume-less deploy (accept the rotation tradeoff).
- **BFF session over backend JWT for the browser** — backend stays stateless; one consistent auth surface.
- **Single Slack app + dispatcher (target) over per-module apps** — Slack is a shared connection; one app, routed internally.

---

## Appendix A — current vs. target, at a glance

| Concern | Comment Bridge (legacy repo) | SlackTube (legacy repo) | Hookline (current / target) |
|---|---|---|---|
| Runtime / layout | .NET 8, 5 projects (clean arch) | .NET 10, 1 project (vertical slice) | .NET 10 — host + one module project per tool (vertical slice) |
| Secrets | Data Protection (DB key-ring) | AES-GCM (env key) | AES-GCM (env key), one shared protector |
| Slack client | SlackNet | raw `HttpClient` | one shared client (pick one) |
| Auth | email+pwd + JWT | admin + `X-Admin-Token` + BFF session | `users` table + BFF session |
| Jobs | Hangfire (PG) + dynamic scheduler | Hangfire (PG) + Redis state | one Hangfire server; Redis with per-module prefixes |
| Connections | YouTube API keys, own Slack app | Google OAuth, own Slack app | shared Connections subsystem; one Slack app |
| Database | own DB | own DB (shared PG) | one DB, schema per module |
| Frontend libs | Next 16 + radix-ui + Recharts | Next 16 + @base-ui + zod | one shell; radix-ui + Recharts + zod |
| Domain | (subdomain) | `slacktube.danielhub.dev` | `danielhub.dev` (old subdomain → 301) |

---

## Appendix B — Redis key-prefix registry

| Prefix | Owner | Holds |
|---|---|---|
| `ytu:*` | YouTube Uploads (Phase 1, built) | event-id dedup, per-project daily quota, cancel flags, Slack status ts (48h) |
| `ytc:*` | YouTube Comments (Phase 2, planned) | dedup / processing markers (ephemeral) |
| `conn:*` | Connections | OAuth state, transient connection caches |
| `auth:*` | Auth | session/rate-limit helpers |

*(Add a row whenever a new module touches Redis.)*
