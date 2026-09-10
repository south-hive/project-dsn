#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build DSN.sln -c Release --nologo -m:"${DSN_BUILD_JOBS:-2}" -nr:false
dotnet run --project tests/Dsn.UnitTests -c Release --no-build
dotnet run --project tests/Dsn.IntegrationTests -c Release --no-build
