#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
bash scripts/dev.sh setup
bash scripts/dev.sh restore DSN.sln
bash scripts/dev.sh dotnet build DSN.sln -c Release --no-restore --nologo -m:"${DSN_BUILD_JOBS:-2}" -nr:false
bash scripts/dev.sh dotnet tests/Dsn.UnitTests/bin/Release/net10.0/Dsn.UnitTests.dll
bash scripts/dev.sh dotnet tests/Dsn.IntegrationTests/bin/Release/net10.0/Dsn.IntegrationTests.dll
