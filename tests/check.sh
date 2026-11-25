#!/usr/bin/env bash
# Runs the local verification suite (Release build across all target frameworks, net10.0 tests under the shared
# spec-sql lock, package-contents check, and verify-specification helper tests). Pass --filter <expr> [target ...]
# to run filtered net10.0 tests on uncommitted work without re-typing the connection string or lock command.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
lock="$HOME/.cache/specification-spec-sql.lock"
mkdir -p "$(dirname "$lock")"

export SPECIFICATION_TEST_SQL_SERVER="${SPECIFICATION_TEST_SQL_SERVER:-Server=localhost,1433;User Id=sa;Password=P@ssW0rd!;TrustServerCertificate=True}"

usage() {
  cat <<'EOF'
Usage:
  tests/check.sh                             Run full Release build, net10.0 suite, package check, and verify tests
  tests/check.sh --filter <expr> [target...] Run filtered net10.0 tests under the spec-sql lock (default target: ci.slnf)
EOF
}

filter=""
targets=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    -f|--filter)
      if [[ $# -lt 2 || -z $2 ]]; then
        echo "error: --filter requires an expression" >&2
        exit 2
      fi
      filter="$2"
      shift 2
      ;;
    --)
      shift
      targets+=("$@")
      break
      ;;
    -*)
      echo "error: unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
    *)
      targets+=("$1")
      shift
      ;;
  esac
done

if [[ -n $filter ]]; then
  if [[ ${#targets[@]} -eq 0 ]]; then
    targets=("$root/ci.slnf")
  fi
  for target in "${targets[@]}"; do
    echo "=== dotnet test $target --framework net10.0 --filter $filter"
    flock "$lock" dotnet test "$target" --framework net10.0 --filter "$filter"
  done
  exit 0
fi

if [[ ${#targets[@]} -gt 0 ]]; then
  echo "error: unexpected positional arguments without --filter: ${targets[*]}" >&2
  usage >&2
  exit 2
fi

echo "=== dotnet build ci.slnf --configuration Release"
dotnet build "$root/ci.slnf" --configuration Release

echo "=== dotnet test ci.slnf --framework net10.0"
flock "$lock" dotnet test "$root/ci.slnf" --configuration Release --no-build --framework net10.0

echo "=== tests/package-contents.sh"
"$root/tests/package-contents.sh"

for test in "$root"/.agents/skills/verify-specification/tests/*.sh; do
  echo "=== ${test#"$root"/}"
  "$test"
done
