#!/usr/bin/env bash
# Optional Python SDK packaging only; never used by the .NET build/test environment.
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$DSN_ROOT"
export PYTHONNOUSERSITE=1
export PYTHONPYCACHEPREFIX="$DSN_DEV_DIR/pycache"
export PIP_CONFIG_FILE=/dev/null
export PIP_CACHE_DIR="$DSN_DEV_DIR/pip-cache"
export PIP_DISABLE_PIP_VERSION_CHECK=1
unset PYTHONPATH PYTHONHOME PIP_INDEX_URL PIP_EXTRA_INDEX_URL PIP_FIND_LINKS PIP_TARGET PIP_PREFIX PIP_USER
case "${1:-}" in
  setup)
    flags=(--index-url https://pypi.org/simple)
    if [[ "${OFFLINE:-0}" == 1 ]]; then flags=(--no-index --find-links "$DSN_ROOT/vendor/python"); fi
    python3 -m pip install "${flags[@]}" --require-hashes --upgrade \
      --target "$DSN_DEV_DIR/python-tools" -r requirements-dev.lock
    ;;
  wheel)
    export PYTHONPATH="$DSN_DEV_DIR/python-tools"
    python3 -m pip wheel --no-index --no-deps --no-build-isolation \
      --wheel-dir artifacts/sdk sdk/source/python
    ;;
  *) echo 'Usage: bash scripts/python-sdk.sh setup|wheel' >&2; exit 2 ;;
esac
