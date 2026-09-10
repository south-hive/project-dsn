ifndef DSN_MAKE_ROOT
DSN_MAKE_ROOT := 1
.DEFAULT_GOAL := help
SHELL := bash
export DSN_BUILD_JOBS ?= 2
export OFFLINE ?= 0
RUN := bash scripts/dev.sh
DEMO_ARGS ?=

.PHONY: help env doctor restore deps-update build clean check test publish demo demo-check pipeline-host pipeline-check bench-host bench-sources bench-check offline-pack offline-check
help:
	@printf '%s\n' \
	  'env / doctor    Prepare/check .NET SDK selection and project caches' \
	  'restore         Restore locked packages (OFFLINE=1 for local feed)' \
	  'build           Build solution with isolated caches' \
	  'clean           Clean C# build outputs; retain collected data' \
	  'check / test    Build and run C# tests' \
	  'demo            Start DUT app Sources + Workspace + live web View' \
	  'demo-check      Verify generated data, SQLite, HTTP and restart' \
	  'pipeline-check  Verify ordered filters and replay' \
	  'bench-check     Verify multiple application Sources' \
	  'publish         Publish Host/Mock into artifacts/' \
	  'offline-pack    Prepare vendor feeds and portable dependency ZIP' \
	  'offline-check   Check build/SDK/demo using fresh isolated offline caches' \
	  'deps-update     Explicitly regenerate NuGet lock files after dependency edits' \
	  'sdk-help        SDK commands (also make -f make/sdk.mk)' \
	  'deploy-help     Docker/CI commands (also make -f make/deploy.mk)'

env:
	$(RUN) setup

doctor: env
	$(RUN) doctor

restore: env
	$(RUN) restore DSN.sln

deps-update: env
	$(RUN) locks DSN.sln

build: restore
	$(RUN) dotnet build DSN.sln -c Release --no-restore --nologo -m:"$(DSN_BUILD_JOBS)" -nr:false

clean: env
	$(RUN) dotnet clean DSN.sln -c Release --nologo -m:"$(DSN_BUILD_JOBS)" -nr:false

check:
	bash scripts/check.sh

test: check

publish:
	bash scripts/publish.sh

demo: build
	$(RUN) python samples/e2e/run.py $(DEMO_ARGS)

demo-check: check
	$(RUN) python samples/e2e/run.py --check $(DEMO_ARGS)

pipeline-host: build
	$(RUN) dotnet src/Dsn.Host/bin/Release/net10.0/Dsn.Host.dll samples/pipeline/settings.json

pipeline-check: check
	$(RUN) python tests/pipeline.py

bench-host: build
	$(RUN) dotnet src/Dsn.Host/bin/Release/net10.0/Dsn.Host.dll

bench-sources: env
	$(RUN) env PYTHONPATH="$(CURDIR)/sdk/source/python/src" python samples/bench/source.py

bench-check: check
	$(RUN) python tests/bench.py

offline-pack: restore
	$(RUN) python scripts/offline.py pack

offline-check: env
	$(RUN) python scripts/offline.py check

include make/sdk.mk
include make/deploy.mk
endif
