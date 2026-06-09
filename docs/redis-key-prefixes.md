# Redis key-prefix registry

One shared Redis (StackExchange.Redis, one connection). Every key is namespaced by a
per-owner prefix so modules can't collide. Redis holds **only** ephemeral/fast state —
anything durable belongs in Postgres. Add a row whenever a new owner touches Redis.

| Prefix | Owner | Holds | TTL |
|---|---|---|---|
| `conn:*` | Connections subsystem | OAuth `state` (CSRF, one-shot), transient connection caches | short (≤10m) |
| `auth:*` | Auth subsystem | session / rate-limit helpers | short |
| `cb:*` | Comment Bridge (Phase 1) | dedup / processing markers | TTL'd |
| `st:*` | SlackTube (Phase 1) | event-id dedup, per-account daily quota, cancel flags, Slack status ts | TTL'd |

Defined in code at `backend/src/Hookline.Infrastructure/Connections/RedisKeys.cs`
(`conn:` / `auth:`). The shared Redis runs `--maxmemory-policy noeviction` in production,
so every key **must** self-expire or be explicitly managed.
