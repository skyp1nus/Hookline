# Redis key-prefix registry

One shared Redis (StackExchange.Redis, one connection). Every key is namespaced by a
per-owner prefix so modules can't collide. Redis holds **only** ephemeral/fast state —
anything durable belongs in Postgres. Add a row whenever a new owner touches Redis.

| Prefix | Owner | Holds | TTL |
|---|---|---|---|
| `conn:*` | Connections subsystem | OAuth `state` (CSRF, one-shot), transient connection caches | short (≤10m) |
| `auth:*` | Auth subsystem | session / rate-limit helpers | short |
| `ytu:*` | YouTube Uploads (`Hookline.Modules.YouTubeUploads`) | event-id dedup, per-project daily quota, cancel flags, Slack status ts (48h) | TTL'd |
| `ytc:*` | YouTube Comments (`Hookline.Modules.YouTubeComments`) | reserved for ephemeral fast-path state; purged on data-reset. No live keys today — dedup is durable in Postgres (`processed_comments`) and quota is OAuth-only (no per-key cache). | TTL'd |

`conn:` / `auth:` are defined at `backend/src/Hookline.Infrastructure/Connections/RedisKeys.cs`;
`ytu:` at `backend/src/Modules/Hookline.Modules.YouTubeUploads/Infrastructure/RedisKeys.cs`;
`ytc:` at `backend/src/Modules/Hookline.Modules.YouTubeComments/Infrastructure/RedisKeys.cs`. The
shared Redis runs `--maxmemory-policy noeviction` in production, so every key **must** self-expire
or be explicitly managed.
