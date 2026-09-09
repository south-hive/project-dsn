.DEFAULT_GOAL := help
SHELL := bash
IMAGE ?= dsn:local
export DSN_IMAGE := $(IMAGE)

.PHONY: help build test check publish ci docker-config docker-build docker-push deploy deploy-image down logs ps

help:
	@printf '%s\n' \
	  'build          Build the solution (Release)' \
	  'check / test   Build and run all tests' \
	  'publish        Publish Host/Mock into artifacts/' \
	  'ci             Check, then build the Docker image (requires Docker)' \
	  'docker-config  Create local Docker settings with a random token, once' \
	  'docker-build   Build the Host image (IMAGE=dsn:local)' \
	  'docker-push    Push IMAGE to an authenticated registry' \
	  'deploy         Configure, build and start with Compose' \
	  'deploy-image   Pull and start IMAGE from a registry; skip building' \
	  'down           Stop Compose; retain stored data' \
	  'logs / ps      Follow logs / show container status'

build:
	dotnet build DSN.sln -c Release --nologo

test: check

check:
	bash scripts/check.sh

publish:
	bash scripts/publish.sh

ci: check
	$(MAKE) docker-build

docker-config:
	bash scripts/docker-config.sh

docker-build:
	docker build --pull -t "$(IMAGE)" .

docker-push:
	docker push "$(IMAGE)"

deploy: docker-config docker-build
	docker compose up -d --no-build --pull never --force-recreate

deploy-image: docker-config
	docker compose up -d --no-build --pull always --force-recreate

down:
	docker compose down

logs:
	docker compose logs --follow --tail 100 dsn

ps:
	docker compose ps
