# Hookline — Phase 0 Discovery Findings

_Generated during Step 1 (Discover). Backend = modular monolith (.NET 10), frontend = Next.js 16 + shadcn (radix-nova) + Tailwind v4, one Postgres (schema per module), one Redis, one Caddy, one BFF._

## 0. Sources of truth — status

| Source | Role | Status |
|---|---|---|
| Architecture guide | **Backend** spec | ⚠️ **Not on disk / not reachable from the CLI.** Reconstructed from the Phase-0 brief (which summarises it heavily). **Please paste it into the repo / give a path so backend contracts can be verified against the original.** Treat every "DanielHub" in it as "Hookline". |
| Claude Design bundle | **Frontend** spec | ✅ Fetched (new URL `gurrEGQ6…`), unpacked to `design/hookline/`, read in full (6 chats + `Hookline.html` + all `.jsx` + `styles.css` + `icons.jsx`). |

## 1. Existing repos (ground truth for Phase 0 contracts)

Confirmed paths (siblings of this repo under `/Users/vladyslav/Desktop/dev/`):

### `slacktube` → Hookline module **`slacktube`** (UI: "YouTube Uploads", route ids `ytu-*`)
- **.NET 10, single ASP.NET project** `SlackTube.Api` (Domain / Data / Services / Endpoints / Jobs). Hangfire 1.8 + Postgres, StackExchange.Redis 2.13, Google.Apis 1.74.
- **Secrets:** AES-256-GCM, key = `SHA256(TokenEncryption__Key)`, payload `[nonce(12)|tag(16)|ct]`, base64. **No key-version header** (Hookline adds a 1-byte version → rotatable).
- **Auth:** `X-Admin-Token` header only; **no user system**. Slack events HMAC-verified; OAuth state-in-cookie.
- **Redis prefix `slacktube:`** — `dedup:slack:*`, `cancel:job:*`, `uploads:youtube:*`, `quota:youtube:*`, `usage:*`.
- **Multiple credential sets per provider** already (`google_oauth_clients` + `google_accounts`).
- **Frontend `web/`** = Next 16.2 + React 19 + Tailwind 4 + TanStack Query v5; **BFF proxy `/api/admin/[...path]`** attaches server-only `X-Admin-Token`; **cookie session** (HMAC, httpOnly, SameSite=lax, 8h) carrying **no user identity** (boolean valid). `BACKEND_URL` (server) + `NEXT_PUBLIC_BACKEND_URL` (client OAuth redirects).

### `comment-bridge` (`YouTubeBridge`) → Hookline module **`comment-bridge`** (UI: "YouTube Comments", route ids `ytc-*`)
- **.NET 8, 5 clean-arch projects:** Domain → Application → Infrastructure → Worker → Api. EF Core 8 + `EFCore.NamingConventions` (snake_case). Hangfire + Postgres, Polly v8, SlackNet.
- **Auth (the closest precedent for Hookline's user system):** `admin_users` (email unique, **bcrypt** `BCrypt.Net-Next`), self-signed **JWT in httpOnly cookie** (7d). **Bootstrap admin** seeded from `Auth:BootstrapAdmin:*` if the table is empty (random password logged if blank). No roles yet. `audit_logs` + `IAuditLogger`. CLI `reset-password`.
- **Secrets:** ASP.NET **Data Protection** (DPAPI) with key-ring in `data_protection_keys` (in-DB, machine-bound → **not portable**).
- **OAuth:** Slack v2 (state cookie); YouTube via **API keys** (no OAuth), multiple keys, quota rotation.
- **Frontend `frontend/`** = Next 16.2 + React 19 + TanStack v5, **direct-to-backend** (`NEXT_PUBLIC_API_URL`, `credentials:'include'`), shadcn + Tailwind v4 + CVA + recharts + react-hook-form + zod + sonner; error shape `{error:string}`.

### `hetzner-den-infra` → shared production stack (`/opt/shared`)
- **One Caddy** (owns 80/443, routes by **container name**, `sites/<app>.caddy` snippets, hot-reload), **one Postgres** (db-per-app: `commentbridge`, `slacktube`), **one Redis** (`noeviction`, TTL keys). External `web` network, no app publishes ports. Hetzner, **4 GB RAM** budget. Domains today: `comment-bridge.danielhub.dev`, `slacktube.danielhub.dev`.

**Implication:** Hookline targets exactly this stack and is a strict simplification — **one** backend + **one** frontend replace two of each (RAM falls). One Hookline Postgres **database** with **schema per module** (`auth`, `connections`, `audit`, `_sample`, later `comment_bridge`/`slacktube`), not db-per-app.

## 2. Design (frontend) — what to build

- **Brand:** "Hookline" (ex-DanielHub). Logo = fishhook-on-a-line, violet→indigo gradient (`BrandMark` SVG in `icons.jsx`). Dark **by default**, working light toggle (next-themes, persisted). Geist + Geist Mono (mono `tnum`). Email `@hookline.io`. Sentence case, Linear/Vercel restraint, 1px borders.
- **Tokens (`styles.css`, oklch, ported verbatim):** primary `oklch(0.55 0.22 264)` light / `oklch(0.62 0.2 264)` dark; full neutral scale; `ok/warn/danger/info` + `*-bg`; `--radius:0.625rem`; 4-step shadow scale; keyframes `spin/pulse-dot/shimmer/barstripes/fadeIn`; `.skeleton`, `.mono`, focus-ring.
- **Shell (`shell.jsx`):** sidebar-07 collapsible **icon rail (256 / 64 px)**, top = `BrandMark` + "Hookline" + **platform switcher** (YouTube active; **LinkedIn + Web disabled, "SOON" badge**). Groups: **Tools** (YouTube Comments → Dashboard/Feed/Channels/Mappings; YouTube Uploads → Queue/History/Mappings), **Connections** (Slack workspaces, Google/YouTube, YouTube API keys), **System** (Logs, Settings). User menu in footer. **Header 56px** sticky+blur: sidebar toggle · breadcrumbs (`Platform › Tool › Sub-page`) · ⌘K palette · notifications bell · theme toggle.
- **⌘K palette:** Pages (per-platform) + Connections + System + **Actions** (Queue an upload / Add YouTube API key / Add comment mapping / Toggle theme). Substring filter, arrow/enter/esc.
- **UI primitives (`ui.jsx`):** Button/Card/Badge/Avatar/Input/Select/Switch/Progress/Tooltip/Menu/Separator/Skeleton/Table/Pagination → shadcn (radix-nova). **Hand-built:** `StatusDot` (pulse), `Kbd`, `BrandMark`.
- **Pages now:** **Overview** (Needs attention → Metrics 4-grid → Recent activity 3fr + Connections health 2fr; 600 ms skeleton). Everything else = polished **Placeholder** (dashed card, `ROUTE_TITLES`).
- **Scope OUT (design):** auth/login screens (not designed), real backend, mobile-optimised, websockets, exports.

## 3. IA reconciliation — module-first backend ↔ platform-first frontend

One typed **`web/lib/nav.ts`** is the single source for **sidebar + breadcrumbs + ⌘K**. Platform = global context (localStorage `hl-platform`), only **YouTube** active. Route ids carry the module mapping: **`ytc-*` → `comment-bridge`**, **`ytu-*` → `slacktube`**.

| Route id | Path | Breadcrumb | Group / Tool | Module |
|---|---|---|---|---|
| `overview` | `/` | Overview | — | — |
| `ytc-dashboard` | `/comments` | YouTube Comments › Dashboard | Tools / Comments | comment-bridge |
| `ytc-feed` | `/comments/feed` | YouTube Comments › Feed | Tools / Comments | comment-bridge |
| `ytc-channels` | `/comments/channels` | YouTube Comments › Channels | Tools / Comments | comment-bridge |
| `ytc-mappings` | `/comments/mappings` | YouTube Comments › Mappings | Tools / Comments | comment-bridge |
| `ytu-queue` | `/uploads/queue` | YouTube Uploads › Queue | Tools / Uploads | slacktube |
| `ytu-history` | `/uploads/history` | YouTube Uploads › History | Tools / Uploads | slacktube |
| `ytu-mappings` | `/uploads/mappings` | YouTube Uploads › Mappings | Tools / Uploads | slacktube |
| `conn-slack` | `/connections/slack` | Connections › Slack workspaces | Connections | (shared) |
| `conn-google` | `/connections/google` | Connections › Google / YouTube | Connections | (shared) |
| `conn-keys` | `/connections/keys` | Connections › YouTube API keys | Connections | (shared) |
| `logs` | `/system/logs` | System › Logs | System | (host) |
| `settings` | `/system/settings` | System › Settings | System | (host) |

## 4. Redis key-prefix registry (repo doc; keep updated)

| Prefix | Owner | Purpose | TTL |
|---|---|---|---|
| `conn:*` | Infrastructure (connections/OAuth) | OAuth `state`, connection caches | short |
| `auth:*` | Auth subsystem | session/assertion nonces, login throttle | short |
| `slacktube:*` | module `slacktube` (Phase 1) | dedup / quota / cancel / usage | TTL'd |
| `commentbridge:*` | module `comment-bridge` (Phase 1) | poll watermarks / dedup | TTL'd |

(Single shared Redis, `noeviction` — every Hookline key must self-expire or be explicitly managed.)

## 5. Phase 0 task list

**Backend `backend/` (`Hookline.*`)**
1. `Hookline.slnx`; `src/Hookline.Host` (only Web SDK), `Hookline.SharedKernel`, `Hookline.Infrastructure`, `src/Modules/` (empty), `tests/Hookline.ArchitectureTests`. Directory.Build.props + CPM + `global.json` (.NET 10).
2. **SharedKernel:** `IModule`; `IEventBus` + integration-event base; Connections accessors (`ISlackConnections`, `IGoogleConnections`, `IConnectionCatalog`, `ConnectionRequirement`); `ICurrentUser` (+ role/identity); `ISecretProtector`; `ISettingsStore`; `IJobScheduler`; persistence conventions (`EncryptedString` converter, design-time helpers); Common (ProblemDetails, paging envelope, `Result`).
3. **Infrastructure:** snake_case + UTC conventions, per-context `MigrationsHistoryTable("__ef_migrations_history","<schema>")`; **AES-256-GCM protector with a 1-byte key-version header**; in-process `IEventBus`; Hangfire (Postgres, secured `/hangfire`); Serilog (`module` + correlation id); Connections store skeleton (schema `connections`, **multi-cred per provider**) + OAuth start/callback skeleton; **auth subsystem** (below); **system principal** for jobs + shared `IAuditLog`/`audit_logs`.
4. **Host `Program.cs`** composition root: shared services → explicit module list (no reflection) → migrate each module DbContext under `pg_advisory_lock` → `MapEndpoints` + `RegisterJobs`.
5. **`Hookline.Modules.Sample`:** `/api/_sample/ping` + one no-op recurring Hangfire job, schema `_sample`.
6. **Architecture tests** in build: no module→module refs; modules → SharedKernel (+ allowed Infrastructure) only; `Domain` folders infra-free.

**Auth (the real Hookline user system)**
7. `users` (id, email unique, bcrypt `password_hash`, role, status, created_at/by, last_login_at). Roles enum: **Owner / Admin / Member**.
8. **First-run bootstrap:** empty `users` → seed one bootstrap **Admin** from `Bootstrap__AdminEmail`/`Bootstrap__AdminPassword`.
9. **One-time Create-Owner:** while no Owner exists, signed-in admin creates exactly one Owner; backend **rejects** creating/granting Owner once one exists (then only an Owner may grant it).
10. **BFF session** (httpOnly cookie minted by Next BFF) + BFF→backend `X-Admin-Token` **plus a short-lived signed assertion of user id+role** → backend resolves `ICurrentUser` per request. Webhooks/OAuth callbacks bypass (signature/state). Authorization = per-feature policy keyed off `ICurrentUser.Role`.

**Frontend `web/`**
11. Scaffold Next 16 (Bun, App Router) + Tailwind v4 + shadcn radix-nova + next-themes (dark default). Port `styles.css` → Tailwind v4 oklch `@theme` tokens; Geist/Geist Mono.
12. `lib/nav.ts` (typed, platform-aware, lucide) → sidebar + breadcrumbs + ⌘K.
13. Shell (sidebar-07 + platform switcher + header + ⌘K + user menu), Overview (from `overview.jsx`), Placeholders for the rest. Shared UI primitives via shadcn MCP + custom `StatusDot`/`Kbd`/`BrandMark`.
14. Login + one-time Owner-bootstrap screens (design-consistent; not in bundle).
15. **BFF:** `app/api/[...path]` proxy + `login`/`logout` handlers (server adds `X-Admin-Token` + signed identity); `lib/api/` client + ProblemDetails→sonner; `lib/auth/` session helpers. "Connect" → `${NEXT_PUBLIC_BACKEND_URL}/{provider}/oauth/start`.

**Dev / ops**
16. One `docker-compose` (Postgres + Redis + backend + frontend) + one `.env.example` + local **no-auth toggle**. `/health` (DB + Redis). CI skeleton: backend build/test incl. arch tests → `bun run build`; docker/deploy = stubs.
17. `docs/adding-a-module.md` + hub README + this findings note + Redis registry. New module cost = **1 module project + 1 frontend feature area + 1 nav entry, zero edits to existing modules**.

## 6. Three confirmations — recommendations

1. **Production domain → `hookline.io`** (matches the rename + design). One Caddy site `hookline.io`: OAuth/webhook/`/hangfire` → backend, everything else → Next BFF. Old `*.danielhub.dev` belong to the standalone apps Hookline supersedes → 301 or retire. _Need: confirm you control `hookline.io` DNS._
2. **Data migration → none.** slacktube data is created only via OAuth + admin endpoints (no seed); comment-bridge `MIGRATION.md` is an **infra** runbook (host move, identical schema, no transform). Even if wanted, encrypted secrets are **not portable** (slacktube AES key ≠ comment-bridge machine-bound DPAPI keys). → Hookline starts greenfield; connections **re-onboarded via OAuth re-consent** in Phase 1.
3. **Bootstrap-admin lifecycle (recommended):** seed one bootstrap **Admin** only when `users` is empty (idempotent; env inert once any user exists). It is a normal persistent Admin row. After the one-time Create-Owner it **stays active** (no auto-disable → avoids lockout); the Owner disables/repurposes it via Settings → Users, nudged from the dashboard. _Alternative (stricter, not recommended): auto-disable the bootstrap admin the moment an Owner is created._

## 7. Open flags
- **Architecture guide missing** — backend contracts (exact interface shapes, schema names, the "adding a module" section) are reconstructed; verify against the original before the backend build.
- Design `openQuestions` (breadcrumb clickability, team/danger-zone interactivity, notification center) — all default to the design's static mockups for Phase 0.
</content>
</invoke>
