# Local testing — tunnels, Google OAuth on localhost, Slack Socket Mode, signed-payload harness

This guide makes local testing of Hookline painless and kills the "the tunnel URL changes every run, now
re-register it in 7 places" problem. There are three independent ways to exercise inbound providers
locally, from least to most setup:

1. **Signed-payload harness** — zero tunnel, zero Slack. Fires a valid, locally-signed Slack request
   straight at the backend. Best for fast inner-loop testing of the upload + reject code paths.
2. **Slack Socket Mode** (dev-only) — a real Slack app over a WebSocket, no public URL at all. Best for
   true end-to-end Slack testing (click the real button) without a tunnel.
3. **Stable tunnel** — a fixed public HTTPS host (`hookline-dev.danielhub.dev`) that forwards to the
   backend. Needed only for **Google OAuth** (Google needs a reachable redirect) and for testing the
   canonical **HTTP webhook** path. Stable means you register the URLs **once**.

---

## Agent notes (future Claude Code sessions)

Read this before touching anything in here. These are the invariants this setup deliberately honours:

- **Provider URLs derive from one var.** `docker-compose.yml` composes every redirect/webhook from
  `${TUNNEL_URL:-http://localhost:8080}/<path>`. Keep that mechanism — do not hard-code provider URLs.
  With `TUNNEL_URL` **unset**, everything resolves to `http://localhost:8080`.
- **Slack is per-module, with NO cross-module dispatcher.** There are TWO Slack apps — one for
  *YouTube Uploads*, one for *YouTube Comments*. Each module owns its own inbound handling end to end.
  The dev Socket Mode service is **per module** (`SlackSocketModeService` in each module's
  `Infrastructure/`); the WebSocket transport is duplicated per module **on purpose** — a module sees only
  the SharedKernel, and a Slack-aware WS client does not belong in the kernel, so duplicating ~a screen of
  dev-only transport beats leaking Slack into shared contracts or building a shared dispatcher.
- **HTTP webhooks are the canonical production path.** The signature-verified
  `/slack/<module>/events|interactivity` endpoints are unchanged and remain the prod transport. Socket Mode
  and the harness are **additive** dev tooling. The Slack interactivity/events handler bodies were
  refactored into reusable `internal static` methods
  (`ProcessEventCallbackAsync` / `DispatchBlockActionsAsync`) so the HTTP endpoint and the Socket Mode
  client call the **same** business logic and stay behaviourally identical.
- **Socket Mode is dev-only and boot-guarded.** `…:Slack:SocketMode:Enabled` defaults to `false` and is
  **refused at boot in Production** by `GuardSecurityConfig` (same pattern as `Auth:DevNoAuth`). Socket Mode
  envelopes are pre-authenticated by the WSS handshake, so — unlike HTTP — there is no per-message
  `X-Slack-Signature` to check; HTTP signature verification is untouched and stays the prod guard.
- **YouTube Comments has no Google callback and no inbound comment webhook.** It polls via YouTube Data API
  **keys**. Its only inbound Slack surface is the "Reject on YouTube" interactivity button.
- **Naming discipline.** Only the names *YouTube Uploads* / *YouTube Comments* (and the slugs
  `youtube-uploads` / `youtube-comments`). No legacy tool names anywhere. Architecture/boundary tests must
  stay green.
- **No real secrets in git.** `.env.example` / `docker-compose.yml` carry only commented example slots.

### Code (agent-owned) vs human-only

| Agent can do (code/config in this repo)                                  | Human-only (external consoles — an agent cannot)                |
| ------------------------------------------------------------------------ | --------------------------------------------------------------- |
| Tunnel scaffold (`dev/tunnel/*`), Makefile, `.env.example` slots         | `cloudflared tunnel login`, `tunnel create`, DNS route          |
| Socket Mode services, options, boot guard, guard tests                   | Enable Socket Mode + generate App-Level Tokens in each Slack app |
| Signed-payload harness + signature-parity tests                          | Register OAuth redirect URIs in Google + both Slack apps         |
| `docker-compose.yml` env wiring (`SocketMode__Enabled`, `AppToken`)      | Paste real `xapp-…` tokens + signing secrets into `.env`         |

Everything in the left column is already done and verified (`dotnet build` + full test suite green,
including architecture/boundary + signature-parity + boot-guard tests). The right column is the
**HUMAN TODO** at the bottom.

---

## 1. Stable tunnel (fixes the "URL changes every run" problem)

The default `cloudflared tunnel run`-without-a-name and `trycloudflare.com` quick tunnels mint a **new**
random hostname every run, which is exactly what forces re-registration everywhere. A **named** tunnel with
a DNS route gives you a permanent host: `hookline-dev.danielhub.dev`.

### Cloudflare (recommended)

Scaffold is in `dev/tunnel/`:

- `cloudflared.config.example.yml` — routes `hookline-dev.danielhub.dev → http://localhost:8080`
  (ingress + the required `http_status:404` catch-all), with `<TUNNEL_ID>` / credentials placeholders.
- `run.sh` — runs the tunnel from your local `cloudflared.config.yml`.

After the one-time human setup (see HUMAN TODO), run it:

```bash
make tunnel        # = ./dev/tunnel/run.sh
```

Then point the stack at the stable host by setting it in `.env`:

```dotenv
TUNNEL_URL=https://hookline-dev.danielhub.dev
```

> `TUNNEL_URL` **must exactly match** every redirect/webhook URI registered in Google + both Slack apps.
> Register the stable host once and you never touch the consoles again.

> **Gotcha — OAuth state cookie / origin match.** The provider OAuth *start* (the panel's "Connect"
> button) and the *callback* must share an origin, or the CSRF `state` cookie is set on one host and
> missing on the other → the callback fails with `invalid_state` and the account is never stored. The
> start origin is `NEXT_PUBLIC_BACKEND_URL`; the callback origin is `TUNNEL_URL`. So when you front the
> backend with a tunnel, set **`NEXT_PUBLIC_BACKEND_URL` to the same host as `TUNNEL_URL`** — in the root
> `.env` (the dockerised frontend's build-arg) **and** in `web/.env.local` if you run the frontend
> locally with `npm run dev` (then restart that dev server so `NEXT_PUBLIC_*` re-inlines). Leave
> `BACKEND_URL` (the server-side BFF→backend hop) on `http://localhost:8080`.

### ngrok (alternative)

ngrok gives the same "stable host" property with a **reserved static domain** (paid feature). After
`ngrok config add-authtoken <token>` and reserving a domain in the dashboard:

```bash
ngrok http 8080 --domain=dev-hookline.ngrok.app   # one-liner; --domain is the reserved static domain
```

Then set `TUNNEL_URL=https://dev-hookline.ngrok.app` in `.env`. The reserved domain is the key bit — a
plain `ngrok http 8080` mints a fresh random URL each run (the very problem we are avoiding).

---

## 2. Google OAuth on localhost

Google OAuth works **without any tunnel**: a `localhost` redirect URI is allowed for OAuth clients. With
`TUNNEL_URL` unset, `docker-compose.yml` resolves:

```
YouTubeUploads__Google__RedirectUri = http://localhost:8080/google/youtube-uploads/oauth/callback
```

(That is the `${TUNNEL_URL:-http://localhost:8080}` default in action — already correct, nothing to fix.)

So you can connect a Google account fully on `localhost`. Because the redirect must match **exactly**,
register **both** callback URIs in the Google OAuth client (HUMAN TODO) so it works with **or** without the
tunnel:

- `http://localhost:8080/google/youtube-uploads/oauth/callback`
- `https://hookline-dev.danielhub.dev/google/youtube-uploads/oauth/callback`

> *Comments* has no Google callback — it polls via YouTube Data API keys. (A force-ssl Google account
> connected through *Uploads* is what enables the "Reject on YouTube" write-back; there is no separate
> Comments Google client.)

---

## 3. Slack Socket Mode (dev-only, no tunnel)

Socket Mode opens a WebSocket from the backend to Slack, so **inbound** Slack (events for Uploads, the
"Reject on YouTube" button for Comments) reaches you with **no public Request URL**. It is **per module**
(separate Slack apps) and is OFF by default.

### Enable it

In `.env` (see `.env.example` for the commented slots):

```dotenv
# YouTube Uploads app
UPLOADS_SLACK_SOCKET_MODE=true
UPLOADS_SLACK_APP_TOKEN=xapp-1-...        # App-Level Token, scope connections:write

# YouTube Comments app
COMMENTS_SLACK_SOCKET_MODE=true
COMMENTS_SLACK_APP_TOKEN=xapp-1-...
```

These map to `YouTubeUploads__Slack__SocketMode__Enabled` / `YouTubeUploads__Slack__AppToken` (and the
Comments equivalents) in `docker-compose.yml`. Start the stack and the per-module `SlackSocketModeService`
connects and logs `Socket Mode WebSocket connected` / `hello (connected)`:

```bash
make up && make logs
```

Each envelope is ACKed and dispatched to the **same** reusable handler the HTTP webhook uses, so behaviour
is identical to production. You still need the bot installed in the workspace (OAuth) and the channels
mapped — Socket Mode only replaces the inbound transport, not the connections.

### Why it is dev-only

Socket Mode envelopes are authenticated by the WSS handshake (the app-level token), so there is **no**
per-message `X-Slack-Signature`. Production must keep the signature-verified HTTP path. The boot guard
enforces this: `…:Slack:SocketMode:Enabled=true` in any non-Development environment **fails the boot** —
exactly like `Auth:DevNoAuth`. (See `GuardSecurityConfigTests`.) `deploy/docker-compose.prod.yml` carries
no Socket Mode keys at all.

---

## 4. Signed-payload harness (zero tunnel, deterministic)

The harness (`backend/tools/Hookline.DevTools.SlackHarness`) builds a valid, locally-signed Slack request
and POSTs it straight at the backend — no tunnel, no Slack account. The signature is computed by
`SlackRequestSigner`, which is proven **byte-for-byte identical** to the backend's `SlackSignatureVerifier`
by `SlackHarnessParityTests` in **both** module test projects (so a drift fails the build, not a live 401).

```bash
# Make sure the backend is running (make up). Put the signing secrets in .env, then:

make slack-fire-upload     # POST a Slack EVENT → /slack/youtube-uploads/events (triggers an upload)
make slack-fire-reject     # POST a Slack INTERACTIVITY → /slack/youtube-comments/interactivity (Reject button)
```

Pass extra fields with `ARGS`:

```bash
make slack-fire-upload ARGS="--channel C0123 --text 'Title :: My video' "
make slack-fire-reject ARGS="--mapping <guid> --comment <commentId> --response-url https://my.sink/x"
```

Secrets resolve from env (`SLACK_SIGNING_SECRET` for Uploads, `SLACK_COMMENTS_SIGNING_SECRET` for Comments;
override with `--secret`). `HOOKLINE_BASE_URL` (default `http://localhost:8080`) sets the target. A `200`
means the signature passed the real verifier and the handler ran; a `401` means the secret does not match
the backend's configured secret.

> The harness exercises the **HTTP** path (it signs like Slack does). Socket Mode (above) exercises the
> WebSocket path. Both funnel into the same reusable handlers, so testing either validates the shared logic.

---

## Manual one-time setup — HUMAN ONLY (Claude Code cannot do these)

These are external-console / secret-paste actions an agent cannot perform. Do them once.

### A. Cloudflare tunnel (or ngrok)

```bash
cloudflared tunnel login                       # opens a browser; authorise the zone danielhub.dev
cloudflared tunnel create hookline-dev         # prints a TUNNEL UUID + a credentials JSON path
cloudflared tunnel route dns hookline-dev hookline-dev.danielhub.dev
```

Then fill the config:

```bash
cp dev/tunnel/cloudflared.config.example.yml dev/tunnel/cloudflared.config.yml
# edit it: replace <TUNNEL_ID> with the UUID, and set credentials-file to the printed JSON path
make tunnel                                    # runs the tunnel
```

**ngrok alternative:** `ngrok config add-authtoken <token>`, reserve a static domain in the dashboard, then
`ngrok http 8080 --domain=<your-reserved-domain>`.

### B. Google OAuth client (APIs & Services → Credentials → your OAuth 2.0 Client)

Add **both** Authorized redirect URIs (so localhost and the tunnel both work):

- `http://localhost:8080/google/youtube-uploads/oauth/callback`
- `https://hookline-dev.danielhub.dev/google/youtube-uploads/oauth/callback`

Paste the client into `.env`: `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`.

### C. Both Slack apps (one per module)

For **each** app (YouTube Uploads app, then YouTube Comments app):

1. **Basic Information → App-Level Tokens** → *Generate Token and Scopes* → add scope `connections:write`
   → copy the `xapp-…` token.
2. **Socket Mode** → toggle **Enable Socket Mode** on.
3. **Event Subscriptions** (Uploads app only) and **Interactivity & Shortcuts** (both apps) → keep them
   **enabled**. In Socket Mode you do **not** need a Request URL — leave it blank or pointed at the stable
   host; events/interactions flow over the socket.
4. **OAuth & Permissions** → register the redirect URL on the stable host (one-time):
   - Uploads app: `https://hookline-dev.danielhub.dev/slack/youtube-uploads/oauth/callback`
   - Comments app: `https://hookline-dev.danielhub.dev/slack/youtube-comments/oauth/callback`
   - (Optionally also the `http://localhost:8080/...` variants if you install without the tunnel.)

Paste into `.env`:

```dotenv
# Uploads app
SLACK_SIGNING_SECRET=...            # Basic Information → Signing Secret
SLACK_CLIENT_ID=...
SLACK_CLIENT_SECRET=...
UPLOADS_SLACK_SOCKET_MODE=true
UPLOADS_SLACK_APP_TOKEN=xapp-1-...

# Comments app
SLACK_COMMENTS_SIGNING_SECRET=...
SLACK_COMMENTS_CLIENT_ID=...
SLACK_COMMENTS_CLIENT_SECRET=...
COMMENTS_SLACK_SOCKET_MODE=true
COMMENTS_SLACK_APP_TOKEN=xapp-1-...
```

### D. Stable host

Set `TUNNEL_URL=https://hookline-dev.danielhub.dev` in `.env`. It must match every URI registered in B and
C. Leave it unset to fall back to `http://localhost:8080` (Google-on-localhost + Socket Mode + harness all
still work without a tunnel).
