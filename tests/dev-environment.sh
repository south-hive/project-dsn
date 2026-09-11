#!/usr/bin/env bash
# Regression coverage for machines lacking an SDK, Python aliases, or both.
set -euo pipefail
cd "$(dirname "$0")/.."
root=$PWD
fixture=$(mktemp -d)
trap 'rm -rf -- "$fixture"' EXIT
mkdir -p "$fixture/scripts" "$fixture/bin"
cp scripts/dev.sh scripts/env.sh "$fixture/scripts/"
cp global.json "$fixture/"
for tool in dirname mkdir; do ln -s "$(command -v "$tool")" "$fixture/bin/$tool"; done
invoke() { PATH="$fixture/bin" DSN_DEV_DIR="$fixture/.dev" DSN_PYTHON="${test_python:-}" "$BASH" "$fixture/scripts/dev.sh" "$@"; }
expect_failure() {
  local output
  if output=$(invoke "$@" 2>&1); then echo "Unexpected success: $*" >&2; exit 1; fi
  [[ "$output" == *".dev/dotnet/"* && "$output" == *"make doctor"* ]]
}
expect_failure setup
expect_failure restore
expect_failure dotnet build
printf '#!%s\nexit 42\n' "$BASH" > "$fixture/bin/dotnet"
chmod +x "$fixture/bin/dotnet"
expect_failure setup
# A repository-local SDK is selected ahead of an incompatible system SDK.
mkdir -p "$fixture/.dev/dotnet"
printf '#!%s\nprintf "10.0.111\\n"\n' "$BASH" > "$fixture/.dev/dotnet/dotnet"
chmod +x "$fixture/.dev/dotnet/dotnet"
invoke setup >/dev/null
invoke doctor >/dev/null
[[ ! -e "$fixture/.dev/venv" ]]
if output=$(invoke python --version 2>&1); then exit 1; fi
[[ "$output" == *"DSN_PYTHON"* ]]
# Only python3 exists; arguments and PYTHONPATH must survive dispatch.
cat > "$fixture/python-body" <<'BODY'
if [[ "${2:-}" == 'import sys; sys.exit(sys.version_info < (3, 10))' ]]; then exit 0; fi
printf '%s|%s|%s\n' "${0##*/}" "$*" "${PYTHONPATH:-}"
BODY
{ printf '#!%s\n' "$BASH"; cat "$fixture/python-body"; } > "$fixture/bin/python3"
chmod +x "$fixture/bin/python3"
[[ "$(PYTHONPATH=sample invoke python sample.py 'two words')" == 'python3|sample.py two words|sample' ]]
# python fallback, then explicit executable selection (including a spaced path).
mv "$fixture/bin/python3" "$fixture/bin/python"
[[ "$(invoke python3 --version)" == 'python|--version|' ]]
cp "$fixture/bin/python" "$fixture/custom python"
test_python="$fixture/custom python"
[[ "$(invoke python --version)" == 'custom python|--version|' ]]
test_python="$fixture/missing-python"
if invoke python --version > /dev/null 2>&1; then exit 1; fi
printf 'PASS SDK recovery hints, local SDK selection, Python dispatch, no automatic venv\n'
