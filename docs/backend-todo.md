# Backend TODO — control-panel endpoints

Tracks the controls that were once **honestly disabled** ("Not yet" via `web/src/components/not-yet.tsx`).
The close-out pass below built the missing endpoints and wired the controls for real, or removed/relabelled
the ones with no real backend. **Zero "backend pending" controls remain.**

---

## ✅ Resolved (close-out pass)

### P0 — Upload-mapping Active / Pause toggle
- **Endpoint:** `PATCH /api/youtube-uploads/mappings/{id:guid}` — body `{ active?: bool }`.
- **Domain/migration:** `IsActive bool NOT NULL DEFAULT true` on `youtube_uploads.channel_mappings`
  (migration `AddChannelMappingIsActive`; the add-column backfills existing routes to **active**).
- **Scheduler reaction:** uploads are **event-driven** (no per-mapping recurring job). A paused route is
  skipped at ingest (`SlackIngestService` enqueues nothing) — the gate is read fresh on every Slack
  message, so pausing reacts immediately and is **restart-safe with no reconcile**. `MappingViewDto.Active`
  now reflects the column (was hardcoded `true`).
- **Web:** `useUpdateUploadMapping`; the row `Switch` is live (refetch + toast).

### P1 — Alerts persistence
- **Endpoints:** `GET /api/system/alerts`, `PATCH /api/system/alerts` (partial).
- **Storage:** shared `ISettingsStore` under `system:alerts:*` (no new table). Defaults
  failures/quota/oauth on, digest off. Audited on change.
- **Web:** `useAlerts` / `useUpdateAlerts`; the 4 switches persist + survive a restart.
- **Scope note:** persists the *preference* only — alert **delivery** (emailing on a failure, etc.) is a
  separate feature, not built. The UI copy says "delivery is rolling out" rather than implying it fires.

### P1 — Danger Zone: Pause-all
- **Endpoint:** `POST /api/system/pause-all`. Host-level fan-out over `IMaintenanceControl` (a SharedKernel
  contract each module implements — the host never names a module type, so the module-boundary arch tests
  hold). Comments pauses each mapping **and tears down its recurring poll + reply sweep**; uploads flips
  the route flag (ingest gate does the rest). Each module audits its slice + a host `system.pause-all`.
- **Web:** `usePauseAll` behind a `ConfirmDialog`.

### Danger Zone: Reset (operational-only)
- **Endpoint:** `POST /api/system/reset`, body `{ confirm: "RESET" }` (type-to-confirm; 400 otherwise).
  Transactional per module, audited (the `system.reset` entry is written **after** the wipe and audit logs
  are **never** cleared).
- **Wipes:** `upload_jobs` + `job_state_history`; `processed_comments` + `pending_deliveries` +
  `quota_usage`; advances every comment mapping's watermark; purges the `ytu:*` / `ytc:*` Redis namespaces
  (best-effort via the shared `ICachePurge`).
- **Keeps:** mappings/routes, upload settings, **connections + secrets**, and the **audit log**.
- **Web:** type-`RESET`-to-confirm dialog (`useResetData`).

### CSV export (Logs + History)
- **Endpoints (server-side, filtered):** `GET /api/system/logs/export.csv?module=&level=&q=` and
  `GET /api/youtube-uploads/upload-history/export.csv?account=&status=&q=`. Stream `text/csv`; the web
  client turns the body into a named file download (the BFF proxy forwards content-type but not
  content-disposition, so the filename is set client-side). Shared RFC-4180 writer in `SharedKernel/Common/Csv`.
- **Web:** real Export buttons (`lib/download.ts`).

### Connections "Manage" — **removed**
No distinct backend purpose (Disconnect already covers the real action), so the button was removed rather
than left disabled.

### Per-route privacy — kept **global-only** (honest label)
Privacy stays the global default visibility (set in Settings). The mapping row shows it as a read-only
"global default" label (no `NotYet`, no fake editable control). Deliberately not per-route.

---

## Deferred / out of scope (with why)

- **Delete workspace / account:** the Settings button was **removed** (not disabled). Single-tenant today —
  there is no real account to delete. Blocked on a multi-tenant / account-deletion design. Disconnect
  (per connection) + Reset (operational) already cover the real destructive actions.
- **Alert delivery:** only the preference is persisted (see P1 scope note). Actually sending alerts
  (email/Slack on failure, quota, oauth-expiry, weekly digest) is a separate feature.
- **Resume-all:** only `pause-all` is wired; resume is per-mapping via the P0 toggle. A `POST
  /api/system/resume-all` could be added later for symmetry.
- **Per-route privacy:** intentionally global-only (above). Revisit only if per-route is genuinely wanted.

---

## Intentional empty-states (not "backend pending")

- **Google "Connect account":** disabled with "Add a Google Cloud project (client id/secret) first." This is
  a real precondition, not a missing backend — `POST /api/youtube-uploads/google/projects` exists; a small
  project-add dialog would make Google connect one-click. The `NotYet` here is an honest precondition gate.

---

## Audit follow-ups (independent review)

### Fixed in the review pass
- **Dead shell controls removed.** The "zero fake controls" claim above had three escapees that the `NotYet`
  grep couldn't catch (plain handler-less elements): UserMenu **Account** (no page → removed) and **Settings**
  (now routes to `/system/settings`); the SiteHeader **Notifications bell** + its fake "live" dot (removed —
  no notifications backend to drive it); and the Dashboard **hardcoded "prod-yt-02 at 87%" quota banner**
  (fabricated, removed — the real "Needs attention" card already surfaces live quota warnings). The sweep now
  holds for handler-less/dead controls, not just `NotYet`.
- **Danger-Zone fan-out is now partial-failure-safe.** The host loops `IMaintenanceControl` sequentially and
  each module's op is its own transaction, so a later module throwing used to leave earlier modules already
  changed, skip the host audit, and surface a bare 500. Now each module runs in its own try/catch: failures
  don't abort the others, the host **always** writes `system.pause-all` / `system.reset` (marked `PARTIAL` with
  the failed module + error), and the response carries `partial: true` + `failed[]` so the UI warns instead of
  claiming full success. Operational-only wipe, so there is still no cross-module rollback — by design.
- **Reset over-deletion is now test-locked.** `UploadsMaintenanceTests` seeds a Google project (its encrypted
  client secret), an account binding, and a Slack-channel cache row, then asserts they all SURVIVE the reset —
  guards against a future `RemoveRange` silently widening the operational wipe into config/secrets.

### Recorded (not fixed — known, accepted)
- **CSV export window ≠ on-screen set.** The export scans a wider window (up to 5000 rows) than the page shows
  (history 100 / logs one 50-row page), so it is a filtered **superset**, not "exactly what I see"; the
  account/status dropdowns only reflect the visible rows. Working as intended, but operators may see older
  matching rows in the file. Also: **no test guards the status-literal contract** between the History `Select`
  (`done`/`failed`/`canceled`, one-L) and `StatusLabel` — correct today, but a future rename would silently
  break the filter with nothing failing. Add a contract test if `StatusLabel` is ever touched.
- **`ICachePurge` does NOT make modules Redis-free** (correcting an overstatement in the close-out notes). It
  keeps the **new maintenance-control** code off a direct `StackExchange.Redis` dependency, but YouTubeUploads
  already takes a direct `IConnectionMultiplexer` from Phase 1 (QuotaService / DedupService / CancellationFlags
  / ApiUsageService / SlackStatusService) and still PackageReferences StackExchange.Redis. Do not treat
  "modules don't reference Redis directly" as an invariant — it is false for Uploads today.

### Note for later (not now)
- **Danger-Zone is gated on `IsAuthenticated` only, no role check.** `POST /api/system/{pause-all,reset}` (and
  alerts) accept any authenticated principal; `ICurrentUser.HasAtLeast(role)` is not called. Fine for the
  single-admin install today, but **harden to Owner/Admin-only before multi-user** lands.
