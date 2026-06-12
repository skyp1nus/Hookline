# Backend TODO — control-panel endpoints

Tracks the controls that were once **honestly disabled** ("Not yet" via `web/src/components/not-yet.tsx`).
The close-out pass below built the missing endpoints and wired the controls for real, or removed/relabelled
the ones with no real backend. **Zero "backend pending" controls remain.**

---

## YouTube Comments → OAuth-only (API-key subsystem removed)

Comment **monitoring** (poll + reply + deep-scan) no longer uses YouTube Data API keys. It resolves a
`youtube.force-ssl` access token for the channel's **owning Google account** through the SharedKernel
`IGoogleChannelCredentials.GetChannelCredentialAsync` contract — the **same** credential the "Reject on
YouTube" button uses, so monitoring-gating and reject-gating are consistent by construction. Comments still
depends only on the SharedKernel contract (one ProjectReference); Uploads stays the sole implementer.
Boundary proven with an illegal-ref experiment (a real Comments→Uploads type-use turns
`Modules_do_not_reference_other_modules` **RED**; reverted → GREEN). *Note: a bare ProjectReference alone is
elided by the compiler and does NOT trip the assembly-reference guard — the experiment needs actual type
usage to bite.*

**Removed entirely:** the shared `connections.api_keys` table + `YouTubeApiKey` entity/store +
`IYouTubeApiKeyConnections` accessor + `ConnectionType.YouTubeApiKey` + `YouTubeApiKeyDisconnected` event
(SharedKernel/Infrastructure); the Comments `IYouTubeApiKeyProvider`/`YouTubeApiKeyProvider`/`ApiKeyService`,
the `quota_usage` table + `QuotaUsage` entity + its disconnect handler, key-rotation, and
`IYouTubeClient.ValidateKeyAsync`/`GetChannelAsync`; the `GET/POST/PATCH/DELETE /api/youtube-comments/keys`
routes; the web **Connections → Keys** page/route/nav/⌘K entry + the mapping-form's API-key gate. Two
`DropTable` migrations (`connections.api_keys`, `youtube_comments.quota_usage`) run under the boot advisory
lock. `RequiredConnections` now lists Google as **required** (was optional).

**Behavioral change (intended):** with API keys you could monitor any public channel; with OAuth you can
only monitor channels a connected, force-ssl Google account **owns** (our own channels). The add-channel
picker offers only those (`GET /api/youtube-comments/youtube/channels/available`); an empty list is the honest
"connect Google to enable monitoring" gated state.

**Honest gated state (where it lives):** the **run-time** poll/sweep is the single source of truth — if no
force-ssl credential resolves for a channel, the job sets `mapping.LastError` ("connect/re-consent Google to
enable monitoring") + a Warning audit and returns (no crash, visible in the mappings table + dashboard
errors KPI), and **self-heals** on the next tick once the account connects. *Deviation from the plan's
"don't schedule" wording:* the dynamic scheduler is **not** gated. Rationale — the credential is short-lived
and re-resolved per poll, so run time is the only correct place to evaluate capability; a schedule-time gate
would mint tokens on boot and need a restart to recover after reconnect, whereas the run-time gate
self-heals. The picker guarantees a mapping is created only for a monitorable channel, so the "no account"
state is the disconnect-after-create edge case. Revisit if a scheduler-level skip is wanted.

**Estimated quota (dashboard):** with per-key accounting gone, the dashboard shows an **approximation** (UI
labelled "≈ estimated"), not metered usage: `Σ active mappings (pollsPerDay×2 + sweepsPerDay×30)` against the
single project's `DailyQuotaUnits` ceiling (10,000). `sweepEstimate = 30u` is a deliberately **conservative**
over-estimate (real sweep cost varies); the ceiling assumes **one** Google project and must rise to the sum
if a second is ever connected. Prefer over- to under-estimating so the operator never silently hits the cap.

**Out of scope / flagged:** the **Uploads** Phase-0 mock (`web/src/lib/mock-data.ts`) still has a `key`
column on upload routes + a couple of `prod-yt-0x` strings — uploads-domain legacy sample, untouched here.
A real per-project Redis spend counter (`ytc:quota:{projectId}:{ptDate}`, mirroring Uploads' `QuotaService`)
is a cheap follow-on if a metered figure is ever wanted alongside the estimate.

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
