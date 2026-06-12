# Hookline dev helpers. See docs/dev/local-testing.md for the full local-testing guide.
# Pass extra harness flags with ARGS, e.g.  make slack-fire-upload ARGS="--text 'hi' --channel C123"
.PHONY: up down logs tunnel slack-fire-upload slack-fire-reject build test

HARNESS := dotnet run --project backend/tools/Hookline.DevTools.SlackHarness --
ARGS ?=

up: ## Start the local stack (postgres + redis + backend + frontend)
	docker compose up -d --build

down: ## Stop the local stack
	docker compose down

logs: ## Tail the backend logs
	docker compose logs -f backend

tunnel: ## Run the stable cloudflared dev tunnel (one-time human login + DNS first; see docs)
	./dev/tunnel/run.sh

slack-fire-upload: ## POST a locally-signed Slack EVENT that triggers a YouTube Uploads upload (no tunnel)
	$(HARNESS) fire-upload $(ARGS)

slack-fire-reject: ## POST a locally-signed Slack INTERACTIVITY for the Comments "Reject on YouTube" button
	$(HARNESS) fire-reject $(ARGS)

build: ## dotnet build the backend solution
	dotnet build backend/Hookline.slnx

test: ## dotnet test the backend solution (incl. architecture + parity tests)
	dotnet test backend/Hookline.slnx
