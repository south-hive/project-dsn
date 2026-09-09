#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -e settings.docker.json ]; then
  echo 'Keeping existing settings.docker.json'
  exit 0
fi
token=$(od -An -N32 -tx1 /dev/urandom | tr -d ' \n')
if [ "${#token}" -ne 64 ]; then
  echo 'Could not generate a Docker access token' >&2
  exit 1
fi
# The bind-mounted file must be readable by the container's non-root user.
# It contains a local-use token and is excluded from Git and the build context.
(set -o noclobber; umask 022; cat > settings.docker.json <<EOF
{
  "bind": "0.0.0.0",
  "dataDirectory": "/data",
  "users": {
    "operator": { "token": "$token", "workspaces": ["echo", "hex"] }
  }
}
EOF
)
echo 'Created settings.docker.json; use the operator token for HTTP requests.'
