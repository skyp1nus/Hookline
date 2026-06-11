# Backend TODO — endpoints the control panel needs

Surfaced during the UI scope-cut / make-real pass. Every control listed here is currently **honestly
disabled** in the web app (a "Not yet" tooltip via `web/src/components/not-yet.tsx`) — never faked. This
doc specifies what each one needs on the backend so the UI can be wired for real. Ordered by priority.

The web pages already wire to the real backend wherever an endpoint exists (logs, connections list +
disconnect, comment mappings, upload-mapping create/delete, history video links). What remains below has
**no backend at all** today.

---

## P0 — Upload-mapping Active / Pause toggle (highest priority)

**Why first:** pausing a route is core control-panel behaviour. Today `MappingViewDto.Active` is hardcoded
`true` (`UploadsReadService.GetMappingsAsync`) and `ChannelMappingService` only exposes List/Create/Delete.
The UI shows the toggle disabled.

- **Endpoint:** `PATCH /api/youtube-uploads/mappings/{id:guid}` — body `{ active?: bool }` (extend later
  for other per-route fields). Return the updated mapping row.
- **Domain/migration:** add `IsActive bool NOT NULL DEFAULT true` to the upload channel-mapping entity
  (`youtube_uploads.channel_mappings` or equivalent). Surface it through `MappingViewDto.Active`.
- **Runtime:** the upload dispatcher must skip mappings where `IsActive = false`.
- **Wire-up (web):** `web/src/features/uploads/hooks.ts` → add `useToggleUploadMapping`; re-enable the
  `Switch` + Status filter in `web/src/app/(app)/uploads/mappings/page.tsx` (remove the `NotYet` wrapper).

## P1 — Alerts persistence

The Settings → Alerts switches (upload failures, quota warnings, OAuth expiry, weekly digest) are disabled;
nothing is persisted. All existing "notify" code is outbound Slack delivery, not user alert preferences.

- **Endpoints:** `GET /api/system/alerts` → `{ uploadFailures, quotaWarnings, oauthExpiry, weeklyDigest }`;
  `PATCH /api/system/alerts` → partial update of the same shape. (Per-user or per-workspace as the auth
  model dictates.)
- **Domain/migration:** an `alert_preferences` table keyed by user/workspace with one bool column per
  alert (or a JSON blob). Seed defaults: failures/quota/oauth on, digest off.
- **Wire-up (web):** add `useAlerts` / `useUpdateAlert` to `web/src/features/system/hooks.ts`; re-enable
  the switches in `web/src/app/(app)/system/settings/page.tsx`.

## P1 — Danger Zone: Pause-all automations

A host-level fan-out that flips every comment mapping **and** upload route inactive at once.

- **Endpoint:** `POST /api/system/pause-all` (and ideally `POST /api/system/resume-all`). Audited.
- **Depends on:** the P0 upload-mapping `IsActive` column + the existing comment-mapping
  `PATCH /api/youtube-comments/mappings/{id}` (already supports `isActive`). Pause-all iterates both.
- **Wire-up (web):** `web/src/app/(app)/system/settings/page.tsx` Danger Zone — add a confirm dialog
  (`ConfirmDialog`) and `usePauseAll`; re-enable the button.

## P2 — Upload-mapping per-route privacy

Today `MappingViewDto.Privacy` is the **global** default visibility echoed onto every row; there is no
per-route privacy. The UI shows it read-only.

- **Endpoint:** fold into the P0 `PATCH /api/youtube-uploads/mappings/{id}` — `{ privacy?: string }`
  (`Public|Unlisted|Private`).
- **Domain/migration:** add `Visibility` to the upload channel-mapping entity; the uploader uses the
  per-route value, falling back to the global `UploadSettings.Visibility` when unset.
- **Wire-up (web):** replace the read-only privacy cell with an editable `Select`.

## P3 — Danger Zone: Reset workspace data (destructive, irreversible)

Clear logs, history, and mappings for the workspace.

- **Endpoint:** `POST /api/system/reset` with an explicit type-to-confirm guard (e.g. require the workspace
  name in the body). Heavily audited; transactional.
- **Domain:** cascade-delete across module tables (mappings, jobs/history, audit?) — define exactly what
  "reset" wipes vs. keeps before building.
- **Wire-up (web):** type-to-confirm modal, not the plain `ConfirmDialog`.

## P3 — Danger Zone: Delete workspace (destructive, irreversible)

Single-tenant today, so there is no real "workspace" entity to delete. Needs a tenancy/account model first.

- **Endpoint:** `DELETE /api/system/workspace` (or `DELETE /api/auth/account`) — blocked on a multi-tenant
  or account-deletion design. Type-to-confirm + audit.

---

## Nice-to-have (no backend needed — deferred for scope, not blocked)

- **Logs CSV export** + **History CSV export:** can be done entirely client-side from the already-loaded
  rows (serialize → Blob download); currently honest-disabled. No endpoint required.
- **Connections "Manage":** no clear backend action. Could route to a workspace/account detail view, or
  reuse `POST /api/youtube-uploads/slack/workspaces/{id}/refresh-channels` (Slack only). Honest-disabled
  until the semantics are decided.
- **Google project creation UI:** `POST /api/youtube-uploads/google/projects` exists but has no UI, so
  Google "Connect account" shows an honest "add a Google Cloud project first" empty-state when no project
  exists. A small project-add dialog (label + client id/secret) would make Google connect one-click.
- **Per-comment Feed + standalone Channels page:** removed in this pass. The Feed had no backend
  per-comment endpoint anyway; channel tracking now lives inline in the comment mapping dialog.
