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
export PYTHONNOUSERSITE=1
export PYTHONPYCACHEPREFIX="$DSN_DEV_DIR/pycache"
export PIP_CONFIG_FILE=/dev/null
export PIP_CACHE_DIR="$DSN_DEV_DIR/pip-cache"
export PIP_DISABLE_PIP_VERSION_CHECK=1
unset PYTHONPATH PYTHONHOME PIP_INDEX_URL PIP_EXTRA_INDEX_URL PIP_FIND_LINKS PIP_TARGET PIP_PREFIX PIP_USER
if [[ -x "$DSN_ROOT/.dev/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$DSN_ROOT/.dev/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
if [[ -d "$DSN_DEV_DIR/venv/Scripts" ]]; then
  export PATH="$DSN_DEV_DIR/venv/Scripts:$PATH"
else
  export PATH="$DSN_DEV_DIR/venv/bin:$PATH"
fi
