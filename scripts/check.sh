#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build Dsn.sln -c Release --nologo
dotnet run --project tests/Dsn.Tests -c Release --no-build
