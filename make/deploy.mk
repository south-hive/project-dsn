ifndef DSN_MAKE_ROOT
include Makefile
.DEFAULT_GOAL := deploy-help
else
IMAGE ?= dsn:local
export DSN_IMAGE := $(IMAGE)
.PHONY: deploy-help ci docker-config docker-build docker-push deploy deploy-image down logs ps
deploy-help:
	@printf '%s\n' 'ci             Run tests then build Docker image' \
	  'docker-config  Generate local token/settings once' \
	  'docker-build / docker-push IMAGE=...  Build online / publish image' \
	  'deploy         Configure, build and run with Compose' \
	  'deploy-image   Pull and run published image' 'down / logs / ps  Manage Compose'

ci: sdk-check
	$(RUN) python samples/e2e/run.py --check
	$(MAKE) -f make/deploy.mk docker-build

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
endif
