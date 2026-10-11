#!/usr/bin/env bash
# Runs on the host as `deploy`, started by CI:
#   IMAGE_TAG=<sha> GHCR_USER=<actor> bash /opt/hookline/deploy.sh   (GHCR token on stdin)
set -euo pipefail

: "${IMAGE_TAG:?}" "${GHCR_USER:?}"
export IMAGE_TAG

APP_DIR=/opt/hookline
REPO=ghcr.io/skyp1nus/hookline
SITE=hookline.caddy
declare -A CONTAINER=([backend]=hookline-backend-1 [frontend]=hookline-frontend-1)

cd "$APP_DIR"
compose() { docker compose -f docker-compose.prod.yml "$@"; }

# Every stack on this host deploys as this user — one at a time.
exec 9>/tmp/prod-deploy.lock
flock -w 300 9

# Private login for this run only, so another stack's logout can't break our pull.
DOCKER_CONFIG=$(mktemp -d)
export DOCKER_CONFIG
trap 'rm -rf "$DOCKER_CONFIG"' EXIT
if [ -d "$HOME/.docker/cli-plugins" ]; then
  ln -s "$HOME/.docker/cli-plugins" "$DOCKER_CONFIG/cli-plugins"
fi
docker login ghcr.io -u "$GHCR_USER" --password-stdin >/dev/null

declare -A PREV
for img in "${!CONTAINER[@]}"; do
  PREV[$img]=$(docker inspect -f '{{.Config.Image}}' "${CONTAINER[$img]}" 2>/dev/null | sed 's/.*://' || true)
done

compose pull --quiet
if ! compose up -d --wait --wait-timeout 300; then
  compose ps
  compose logs --tail 100
  echo "::error::Not healthy. Roll back with: cd $APP_DIR && IMAGE_TAG=${PREV[backend]:-<sha>} docker compose -f docker-compose.prod.yml up -d --wait"
  exit 1
fi

# A snippet Caddy rejects must not stay on disk — the next Caddy restart would take every site down.
dest=/opt/shared/sites/$SITE
backup=$APP_DIR/.$SITE.bak
if [ -f "$dest" ]; then cp -p "$dest" "$backup"; else rm -f "$backup"; fi
install -D -m 644 Caddyfile "$dest"
if ! docker exec shared-caddy-1 caddy reload --config /etc/caddy/Caddyfile; then
  if [ -f "$backup" ]; then mv "$backup" "$dest"; else rm -f "$dest"; fi
  docker exec shared-caddy-1 caddy reload --config /etc/caddy/Caddyfile || true
  echo "::error::Caddy rejected $SITE; the previous snippet is back in place"
  exit 1
fi
rm -f "$backup"

# Sha tags never go dangling, so `image prune` alone frees nothing. Keep the new and the previous tag.
for img in "${!CONTAINER[@]}"; do
  docker image ls "$REPO/$img" --format '{{.Tag}}' \
    | { grep -vxF -e "$IMAGE_TAG" -e "${PREV[$img]:-$IMAGE_TAG}" -e '<none>' || true; } \
    | while read -r tag; do docker image rm "$REPO/$img:$tag" >/dev/null 2>&1 || true; done
done
docker image prune -f >/dev/null
