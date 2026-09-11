#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$DSN_ROOT"
command=${1:-doctor}
if [[ $# -gt 0 ]]; then shift; fi
require_sdk() {
  if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --version; then
    printf 'DSN: compatible .NET SDK not available (see %s/global.json).\nInstall .NET SDK 10.0 (10.0.100 or newer) for this OS/CPU, or place the full SDK at %s/.dev/dotnet/ (including dotnet and sdk/).\nThen retry make doctor. Termux requires a bionic-compatible SDK.\nGuide: %s/docs/development-environment.md\n' "$DSN_ROOT" "$DSN_ROOT" "$DSN_ROOT" >&2
    return 1
  fi
}
case "$command" in
  setup)
    mkdir -p "$DSN_DEV_DIR"
    require_sdk
    ;;
  restore|locks)
    require_sdk >/dev/null
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
    require_sdk
    printf 'dotnet: %s\n' "$(command -v dotnet)"
    ;;
  python|python3)
    dsn_select_python
    exec "$DSN_PYTHON" "$@"
    ;;
  dotnet)
    case "${1:-}" in
      build|restore|publish|pack|run|test|msbuild|clean|--version) require_sdk >/dev/null ;;
    esac
    exec dotnet "$@"
    ;;
  *) exec "$command" "$@" ;;
esac
