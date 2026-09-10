#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts
staging=$(mktemp -d artifacts/.publish.XXXXXX)
trap 'rm -rf -- "$staging"' EXIT
dotnet publish src/Dsn.Host -c Release -o "$staging/host" --nologo -m:"${DSN_BUILD_JOBS:-2}" -nr:false
dotnet publish src/Dsn.Mock -c Release -o "$staging/mock" --nologo -m:"${DSN_BUILD_JOBS:-2}" -nr:false
# Replace only complete generated bundles, so removed assemblies cannot survive a refactor.
for target in host mock; do
  if [ -e "artifacts/$target" ]; then mv "artifacts/$target" "$staging/previous-$target"; fi
  mv "$staging/$target" "artifacts/$target"
done
