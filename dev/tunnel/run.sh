#!/usr/bin/env bash
# Start the stable Hookline cloudflared dev tunnel from the local config.
#
# This only RUNS the tunnel. The one-time human setup it depends on (cloudflared login, `tunnel create`,
# and the DNS route for hookline-dev.danielhub.dev) is documented in docs/dev/local-testing.md (HUMAN TODO)
# and cannot be done by an agent. Copy cloudflared.config.example.yml to cloudflared.config.yml and fill
# in the tunnel id + credentials path first.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="${DIR}/cloudflared.config.yml"

if ! command -v cloudflared >/dev/null 2>&1; then
  echo "cloudflared is not installed. Install it (e.g. 'brew install cloudflared') — see docs/dev/local-testing.md." >&2
  exit 1
fi

if [ ! -f "${CONFIG}" ]; then
  echo "Missing ${CONFIG}." >&2
  echo "Copy cloudflared.config.example.yml to cloudflared.config.yml and fill in the tunnel id + credentials path." >&2
  exit 1
fi

echo "Starting cloudflared tunnel from ${CONFIG} (hookline-dev.danielhub.dev -> http://localhost:8080)…"
exec cloudflared tunnel --config "${CONFIG}" run
