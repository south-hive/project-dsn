#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
bash scripts/dev.sh setup
bash scripts/dev.sh restore DSN.sln
mkdir -p artifacts
staging=$(mktemp -d artifacts/.publish.XXXXXX)
trap 'rm -rf -- "$staging"' EXIT
bash scripts/dev.sh dotnet publish src/Dsn.Host -c Release -o "$staging/host" --no-restore --nologo -m:"${DSN_BUILD_JOBS:-2}" -nr:false
bash scripts/dev.sh dotnet publish src/Dsn.Mock -c Release -o "$staging/mock" --no-restore --nologo -m:"${DSN_BUILD_JOBS:-2}" -nr:false
# Replace only complete generated bundles, so removed assemblies cannot survive a refactor.
for target in host mock; do
  if [ -e "artifacts/$target" ]; then mv "artifacts/$target" "$staging/previous-$target"; fi
  mv "$staging/$target" "artifacts/$target"
done
