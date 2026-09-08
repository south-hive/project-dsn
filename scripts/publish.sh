#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet publish src/Dsn.Host -c Release -o artifacts/host --nologo
dotnet publish src/Dsn.Mock -c Release -o artifacts/mock --nologo
