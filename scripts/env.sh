#!/usr/bin/env bash
# Source from a subprocess/wrapper; never change HOME or the user's global tool settings.
DSN_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export DSN_ROOT
export DSN_DEV_DIR="${DSN_DEV_DIR:-$DSN_ROOT/.dev}"
export NUGET_PACKAGES="$DSN_DEV_DIR/nuget/packages"
export NUGET_HTTP_CACHE_PATH="$DSN_DEV_DIR/nuget/http-cache"
export NUGET_PLUGINS_CACHE_PATH="$DSN_DEV_DIR/nuget/plugins-cache"
export DOTNET_CLI_HOME="$DSN_DEV_DIR/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export MSBUILDDISABLENODEREUSE=1
if [[ -x "$DSN_ROOT/.dev/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$DSN_ROOT/.dev/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi

# Resolve Python only for optional Python commands; .NET setup never invokes it.
dsn_select_python() {
  local candidate
  if [[ -n "${DSN_PYTHON:-}" ]]; then
    if command -v "$DSN_PYTHON" >/dev/null 2>&1 && "$DSN_PYTHON" -c 'import sys; sys.exit(sys.version_info < (3, 10))' >/dev/null 2>&1; then
      export DSN_PYTHON
      return 0
    fi
  else
    for candidate in python3 python; do
      if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'import sys; sys.exit(sys.version_info < (3, 10))' >/dev/null 2>&1; then
        DSN_PYTHON=$(command -v "$candidate")
        export DSN_PYTHON
        return 0
      fi
    done
  fi
  printf 'DSN: Python 3.10+ is required for this optional command. Install python3 or set DSN_PYTHON to its executable path. .NET build/check do not require Python.\n' >&2
  return 1
}
