# Hookline

Modular monolith: .NET 10 backend + Next.js BFF. Tools plug in module-by-module (today: **YouTube
Uploads**, **YouTube Comments**).

## Docs

- **[Local testing](docs/dev/local-testing.md)** — painless local dev: stable tunnel, Google OAuth on
  localhost, Slack Socket Mode (dev-only), and the signed-payload harness. **Start here to run things
  locally.** (Future agent sessions: the "Agent notes" section at the top records the invariants.)
- [Architecture guide](docs/hookline-architecture-guide.md) — the authoritative backend spec.
- [Adding a module](docs/adding-a-module.md).

## Quick start

```bash
cp .env.example .env     # fill in secrets as needed (see docs/dev/local-testing.md)
make up                  # postgres + redis + backend + frontend
make test                # build + full backend test suite
```
