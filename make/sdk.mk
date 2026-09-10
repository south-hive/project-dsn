ifndef DSN_MAKE_ROOT
include Makefile
.DEFAULT_GOAL := sdk-help
else
.PHONY: python-tools sdk-help sdk-build sdk-pack sdk-check workspace-sdk sample-host sample-python sample-cpp release-pack
sdk-help:
	@printf '%s\n' 'sdk-build      C++ SDK, tests and temperature example' \
	  'workspace-sdk  Pack Dsn.Contracts NuGet' 'sdk-pack       Pack Python/C++/C# SDKs' \
	  'sdk-check      C# + SDK integration tests' 'sample-host    Temperature Host' \
	  'sample-python / sample-cpp  Temperature Sources' \
	  'release-pack VERSION=vX.Y.Z  Package published files; prepare release notes first'

sdk-build: env
	$(RUN) cmake -S sdk/source/cpp -B artifacts/source-cpp -DCMAKE_BUILD_TYPE=Release -DDSN_BUILD_SAMPLES=ON -DDSN_BUILD_TESTS=ON
	$(RUN) cmake --build artifacts/source-cpp --parallel "$(DSN_BUILD_JOBS)"

workspace-sdk: restore
	$(RUN) dotnet pack src/Dsn.Contracts -c Release -o artifacts/sdk --no-restore --nologo -m:"$(DSN_BUILD_JOBS)" -nr:false

python-tools: env
	bash scripts/python-sdk.sh setup

sdk-pack: sdk-build workspace-sdk python-tools
	bash scripts/python-sdk.sh wheel
	$(RUN) cmake --install artifacts/source-cpp --prefix "$(CURDIR)/artifacts/sdk/cpp"

sdk-check: check
	$(MAKE) -f make/sdk.mk sdk-pack
	$(RUN) python -m unittest discover -s tests/sdk -v

sample-host: build
	$(RUN) dotnet src/Dsn.Host/bin/Release/net10.0/Dsn.Host.dll samples/temperature/settings.json

sample-python: env
	$(RUN) env PYTHONPATH="$(CURDIR)/sdk/source/python/src" python samples/temperature/source.py

sample-cpp: sdk-build
	$(RUN) artifacts/source-cpp/temperature_source

release-pack: publish sdk-pack
	$(RUN) python scripts/release-pack.py "$(VERSION)"
endif
