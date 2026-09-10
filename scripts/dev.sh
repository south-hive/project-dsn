#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$DSN_ROOT"
command=${1:-doctor}; shift || true
case "$command" in
  setup)
    mkdir -p "$DSN_DEV_DIR"
    dotnet --version
    ;;
  restore|locks)
    config="$DSN_ROOT/NuGet.Config"
    flags=(--locked-mode)
    if [[ "$command" == locks ]]; then flags=(--force-evaluate -p:RestoreLockedMode=false); fi
    if [[ "${OFFLINE:-0}" == 1 ]]; then
      config="$DSN_ROOT/NuGet.Offline.Config"
      flags+=(-p:NuGetAudit=false)
    fi
    if [[ $# == 0 ]]; then set -- DSN.sln; fi
    exec dotnet restore "$@" --configfile "$config" "${flags[@]}" --disable-parallel --nologo
    ;;
  doctor)
    printf 'DSN root: %s\nIsolated environment: %s\nNuGet packages: %s\n' "$DSN_ROOT" "$DSN_DEV_DIR" "$NUGET_PACKAGES"
    dotnet --version
    printf 'dotnet: %s\n' "$(command -v dotnet)"
    ;;
  *) exec "$command" "$@" ;;
esac
