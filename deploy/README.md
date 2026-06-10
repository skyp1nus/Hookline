# Hookline — deployment artifacts (LOCAL PREP)

Production-ready deploy config for **danielhub.dev**. Nothing here touches a live
server: these are committed artifacts + a local prod-profile harness that proves
they work. Actual deployment is a separate, gated step (see the go-live checklist).

Topology (arch guide §16): Hookline runs as **one backend container + one frontend
container** on the existing shared Hetzner `web` network, behind the **shared Caddy**,
using the **shared Postgres + Redis**. It owns no infrastructure and publishes no host
ports — only the shared Caddy is public.

```
browser ──HTTPS──▶ shared Caddy (danielhub.dev)
                     ├─ /slack/* /google/* /linkedin/* /hangfire ─▶ hookline-backend-1:8080
                     └─ everything else (incl /api/* BFF)         ─▶ hookline-frontend-1:3000
                                                                       └─(server-side)─▶ backend
backend ─▶ shared Postgres (schema-per-module) + Redis (ytu:/ytc:/conn:/auth: prefixes)
```

## Files

| File | Role |
|------|------|
| `docker-compose.prod.yml` | Prod stack: 2 services, external `web` net, pinned names, no host ports, `ASPNETCORE_ENVIRONMENT=Production`, `COOKIE_SECURE=true`, GHCR images. |
| `Caddyfile` | Site block for the shared Caddy → `/opt/shared/sites/hookline.caddy`. Automatic HTTPS. Routes providers + `/hangfire` to backend, rest to frontend. |
| `.env.prod.example` | Every required prod var with comments. Copy to `/opt/hookline/.env` and fill. Placeholders contain `change-me` so an unfilled copy fails the boot guard. |
| `local/` | **Local prod-profile smoke harness** — brings the prod compose up on a laptop to verify it. Not used in deployment. |

## OAuth redirect URIs to register (checklist — apply later in Google Cloud / Slack)

Register these **callback** URIs **byte-for-byte** (the backend passes the configured
`RedirectUri` verbatim to both the authorize URL and the token exchange; any mismatch =
`redirect_uri_mismatch`). All assume the public origin is `https://danielhub.dev`.

**Google Cloud Console** → the OAuth client(s) → *Authorized redirect URIs* (every
per-project Google client used for Uploads must include this exact URI):
- [ ] `https://danielhub.dev/google/youtube-uploads/oauth/callback`

> There is **no** `/google/youtube-comments/*` URI — the Comments module has no Google
> OAuth. Its YouTube access is via **API keys** entered in-app, not OAuth. Do not register one.

**Slack app(s)** → *OAuth & Permissions* → *Redirect URLs*:
- [ ] `https://danielhub.dev/slack/youtube-uploads/oauth/callback`
- [ ] `https://danielhub.dev/slack/youtube-comments/oauth/callback`

**Slack app (Uploads)** → these are **webhook Request URLs**, *not* OAuth redirects:
- [ ] *Event Subscriptions* → Request URL: `https://danielhub.dev/slack/youtube-uploads/events`
- [ ] *Interactivity & Shortcuts* → Request URL: `https://danielhub.dev/slack/youtube-uploads/interactivity`

> The `.../oauth/start` paths are app-initiated install entry points — do **not** register them.

These URIs are derived automatically from `App__PublicBaseUrl` in `.env` (compose composes
`${App__PublicBaseUrl}/<path>`), so set `App__PublicBaseUrl=https://danielhub.dev` and they line up.

## Server prerequisites (one-time, on the shared host — for later)

1. `web` network exists: `docker network create web` (already there if other apps run).
2. Hookline Postgres role + DB (init script only runs on a fresh volume, so on the live
   box create them manually):
   ```
   docker exec -it shared-postgres-1 psql -U postgres -c "CREATE ROLE hookline LOGIN PASSWORD '<HOOKLINE_DB_PASSWORD>';"
   docker exec -it shared-postgres-1 psql -U postgres -c "CREATE DATABASE hookline OWNER hookline;"
   ```
   Add `HOOKLINE_DB_PASSWORD=<same>` to `/opt/shared/.env` and the shared compose's
   postgres env for consistency; it must equal the password in `ConnectionStrings__Postgres`.
3. `/opt/hookline/.env` from `.env.prod.example`, all real secrets filled
   (`openssl rand -base64 36`; AES key `-base64 48`). **Never change `TokenEncryption__Key`
   after launch** — it makes every stored token undecryptable.

## First bring-up (for later, on the server)

```
cd /opt/hookline
docker compose -f docker-compose.prod.yml pull        # or build, if not using GHCR
docker compose -f docker-compose.prod.yml up -d
install -D -m 644 Caddyfile /opt/shared/sites/hookline.caddy
docker exec shared-caddy-1 caddy reload --config /etc/caddy/Caddyfile
```
The backend migrates every schema under an advisory lock and seeds the bootstrap admin on
boot, then refuses to serve if any security secret is weak. Then: open the panel, log in as
`Bootstrap__AdminEmail`, do the one-time **Create-Owner**.

## CI / CD (`.github/workflows/ci.yml`)

- `backend` + `frontend` jobs build/test on every push/PR (`.slnx` incl. architecture
  tests, .NET 10; bun build). These run **for real** today.
- `docker` (build + push both images to GHCR) and `deploy` (scp compose + Caddy snippet,
  pull, `up -d`, hot-reload Caddy) are **DISABLED** behind a one-line gate.

**To enable deployment** — on the `docker` job, change `if: false` →
`if: github.ref == 'refs/heads/main'`. The `deploy` job (`needs: [docker]`) then runs on
main automatically. First set repo secrets `DEPLOY_HOST` and `DEPLOY_SSH_KEY`, and confirm
`GH_REPO` / image names. `NEXT_PUBLIC_BACKEND_URL` is baked at image-build time as
`https://danielhub.dev` (a runtime env can't override an inlined value).

## Hangfire

The dashboard at `/hangfire` is routed backend-direct by `Caddyfile` and gated by the
backend's admin authorization filter (admin only). The Caddy route is what makes it
reachable in prod — it is **not** exposed unprotected.

## Domain note

This config uses **`danielhub.dev`** (the apex), as specified. The shared-host convention in
the legacy templates is a per-app subdomain (`<app>.danielhub.dev`, e.g.
`hookline.danielhub.dev`), and the arch guide's top usage-note also says
`hookline.danielhub.dev`. If the apex is already taken on the shared box, switch the site
address in `Caddyfile`, `App__PublicBaseUrl` in `.env`, the `NEXT_PUBLIC_BACKEND_URL`
build-arg in `ci.yml`, and the registered OAuth URIs to the chosen host — they must all match.

## Local prod-profile verification (no deploy)

Proves the prod artifacts work end-to-end on a laptop, under Production env with
`COOKIE_SECURE=true`, behind a local Caddy (internal CA) so the Secure cookie sticks.

```
docker network create web 2>/dev/null || true
docker compose --project-directory . --env-file deploy/local/.env.prod.local \
  -f deploy/docker-compose.prod.yml -f deploy/local/docker-compose.prod-local.yml up --build -d
bash deploy/local/smoke.sh
# teardown:
docker compose --project-directory . \
  -f deploy/docker-compose.prod.yml -f deploy/local/docker-compose.prod-local.yml down -v
```

`smoke.sh` checks: `/health` 200 → `bootstrap-state` (ownerExists=false) → login as bootstrap
admin (200, Secure `hl_session`) → `/api/auth/me` Admin → one-time Create-Owner (200 Owner) →
second Create-Owner rejected (403) → `bootstrap-state` (ownerExists=true).

## Go-live checklist (the gated sequence to run later)

1. **DNS** — point the chosen host (`danielhub.dev` or `<app>.danielhub.dev`) at the Hetzner box.
2. **Server prereqs** — create the `hookline` role + DB; add `HOOKLINE_DB_PASSWORD` to
   `/opt/shared/.env`; create `/opt/hookline/.env` from `.env.prod.example` with real secrets.
3. **OAuth** — register the 3 callback URIs (Google client(s) + both Slack apps) and the 2
   Slack webhook Request URLs; set matching `App__PublicBaseUrl`.
4. **Enable CI deploy** — flip the `docker` job `if: false` → `if: github.ref == 'refs/heads/main'`;
   set `DEPLOY_HOST` + `DEPLOY_SSH_KEY` secrets; merge to `main`.
5. **Verify prod** — `/health` green; log in; one-time Create-Owner; smoke an upload + a comment.
6. **Decommission** — once Hookline is serving, redirect/retire the old subdomains and stop the
   legacy slacktube / comment-bridge stacks (free the RAM on the 4 GB box).
