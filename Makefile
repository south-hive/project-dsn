.DEFAULT_GOAL := help
SHELL := bash
IMAGE ?= dsn:local
export DSN_BUILD_JOBS ?= 2
export DSN_IMAGE := $(IMAGE)

.PHONY: help build test check publish ci docker-config docker-build docker-push deploy deploy-image down logs ps sdk-build sdk-pack sdk-check workspace-sdk sample-host sample-python sample-cpp

help:
	@printf '%s\n' \
	  'build          Build the solution (Release)' \
	  'check / test   Build and run all tests' \
	  'publish        Publish Host/Mock into artifacts/' \
	  'ci             Check server/SDKs, then build the Docker image' \
	  'sdk-build      Build C++ SDK, tests and temperature source' \
	  'sdk-pack       Package Python/C++ Source SDKs and Workspace contracts' \
	  'sdk-check      Check server, SDKs and cross-language end-to-end flow' \
	  'workspace-sdk  Pack Dsn.Contracts for external Workspace authors' \
	  'pipeline-host  Start local Source -> filters -> SQLite example' \
	  'pipeline-check Validate app pipeline, replay and SQLite restart' \
	  'bench-host     Build/start standalone DSN with web Presenter' \
	  'bench-sources  Send three synthetic app processes to bench Workspace' \
	  'sample-host    Build/start Host with the temperature Workspace' \
	  'sample-python  Send Python temperature samples' \
	  'sample-cpp     Build/send C++ temperature samples' \
	  'docker-config  Create local Docker settings with a random token, once' \
	  'docker-build   Build the Host image (IMAGE=dsn:local)' \
	  'docker-push    Push IMAGE to an authenticated registry' \
	  'deploy         Configure, build and start with Compose' \
	  'deploy-image   Pull and start IMAGE from a registry; skip building' \
	  'down           Stop Compose; retain stored data' \
	  'logs / ps      Follow logs / show container status'

build:
	dotnet build DSN.sln -c Release --nologo -m:"$(DSN_BUILD_JOBS)" -nr:false

test: check

check:
	bash scripts/check.sh

publish:
	bash scripts/publish.sh

ci: sdk-check
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

sdk-build:
	cmake -S sdk/source/cpp -B artifacts/source-cpp -DCMAKE_BUILD_TYPE=Release -DDSN_BUILD_SAMPLES=ON -DDSN_BUILD_TESTS=ON
	cmake --build artifacts/source-cpp --parallel 2

workspace-sdk:
	dotnet pack src/Dsn.Contracts -c Release -o artifacts/sdk --nologo

sdk-pack: sdk-build workspace-sdk
	python -m pip wheel --no-deps --wheel-dir artifacts/sdk sdk/source/python
	cmake --install artifacts/source-cpp --prefix "$(CURDIR)/artifacts/sdk/cpp"

sdk-check: check
	$(MAKE) sdk-pack
	python -m unittest discover -s tests/sdk -v

sample-host: build
	dotnet run --project src/Dsn.Host -c Release --no-build -- samples/temperature/settings.json

sample-python:
	PYTHONPATH="$(CURDIR)/sdk/source/python/src" python samples/temperature/source.py

sample-cpp: sdk-build
	artifacts/source-cpp/temperature_source

.PHONY: bench-host bench-sources
bench-host: build
	dotnet run --project src/Dsn.Host -c Release --no-build

bench-sources:
	PYTHONPATH=sdk/source/python/src python samples/bench/source.py

.PHONY: bench-check
bench-check: check
	python tests/bench.py

.PHONY: pipeline-host pipeline-check
pipeline-host: build
	dotnet run --project src/Dsn.Host -c Release --no-build -- samples/pipeline/settings.json

pipeline-check: check
	python tests/pipeline.py

.PHONY: release-pack
# Prepare artifacts/release/RELEASE-NOTES.md before packaging. Publishing to GitHub is separate.
release-pack: publish sdk-pack
	python scripts/release-pack.py "$(VERSION)"
