#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$DSN_ROOT"
command=${1:-doctor}; shift || true
case "$command" in
  setup)
    mkdir -p "$DSN_DEV_DIR"
    if [[ ! -f "$DSN_DEV_DIR/venv/pyvenv.cfg" ]]; then
      python3 -m venv "$DSN_DEV_DIR/venv"
    fi
    python -c 'import sys; assert sys.prefix != sys.base_prefix, "DSN venv is not active"'
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
  python-tools)
    python -c 'import sys; assert sys.prefix != sys.base_prefix, "Run make env first"'
    flags=(--index-url https://pypi.org/simple)
    if [[ "${OFFLINE:-0}" == 1 ]]; then flags=(--no-index --find-links "$DSN_ROOT/vendor/python"); fi
    exec python -m pip install "${flags[@]}" --require-hashes -r requirements-dev.lock
    ;;
  doctor)
    printf 'DSN root: %s\nIsolated environment: %s\nNuGet packages: %s\n' "$DSN_ROOT" "$DSN_DEV_DIR" "$NUGET_PACKAGES"
    dotnet --version
    python -c 'import sys; print("Python:", sys.executable); print("venv:", sys.prefix != sys.base_prefix)'
    ;;
  *) exec "$command" "$@" ;;
esac
