#!/usr/bin/env bash
# Compares `dotnet pack` + `tests/package-contents.sh` across ordinary vs private-TMPDIR (sandbox) execution
# with MSBuild node reuse enabled vs disabled, preserving SDK details, pack.log, package locations, and exit codes.
#
# Usage:
#   tests/diagnose-package-contents.sh [--check] [commit] [output-dir]
#   --check exits non-zero if `tests/package-contents.sh` fails under any case (including sandbox-reuse-on,
#   which is the deterministic regression for cross-namespace MSBuild node reuse fixed in c9bf318).
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
check_mode=0
if [[ ${1:-} == "--check" ]]; then
  check_mode=1
  shift
fi
commit="${1:-HEAD}"
out_dir="${2:-$(mktemp -d "${TMPDIR:-/tmp}/diagnose-package-contents.XXXXXX")}"
mkdir -p "$out_dir"

sandbox="${SKILLFLOW_SANDBOX:-}"
if [[ -z $sandbox ]]; then
  for candidate in \
    "$HOME/Work/skillflow/skills/skillflow/bin/sandbox" \
    "$HOME/.claude/skills/skillflow/bin/sandbox"; do
    if [[ -x $candidate ]]; then
      sandbox="$candidate"
      break
    fi
  done
fi

sha=$(git -C "$root" rev-parse "$commit")
echo "Commit: $sha" | tee "$out_dir/summary.txt"
echo "Output: $out_dir" | tee -a "$out_dir/summary.txt"
echo "dotnet: $(command -v dotnet || echo missing) ($(dotnet --version 2>&1 || true))" | tee -a "$out_dir/summary.txt"
echo "SDKs:" | tee -a "$out_dir/summary.txt"
dotnet --list-sdks 2>&1 | sed 's/^/  /' | tee -a "$out_dir/summary.txt"

run_case() {
  local name="$1" mode="$2" reuse="$3"
  local case_dir="$out_dir/$name"
  local work clone feed pack_exit pkg_exit
  mkdir -p "$case_dir"
  work=$(mktemp -d "/tmp/diag-pkg-$name.XXXXXX")
  clone="$work/clone"
  git clone -q "$root" "$clone"
  git -C "$clone" checkout -q --detach "$sha"

  local -a env_args=() pack_args=()
  if [[ $reuse == "disabled" ]]; then
    env_args=(MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0)
    pack_args=(--disable-build-servers)
  else
    env_args=(MSBUILDDISABLENODEREUSE=0)
  fi

  local script
  script=$(cat <<EOF
set -uo pipefail
cd "$clone"
echo "TMPDIR=\${TMPDIR:-<unset>}"
echo "dotnet=\$(command -v dotnet) (\$(dotnet --version))"
dotnet build ci.slnf --configuration Release -nologo -tl:off > "$case_dir/build.log" 2>&1
build_rc=\$?
echo "build_exit=\$build_rc"
if (( build_rc != 0 )); then exit \$build_rc; fi
feed=\$(mktemp -d "\${TMPDIR:-/tmp}/diag-feed.XXXXXX")
echo "feed=\$feed"
dotnet pack "$clone/ci.slnf" --configuration Release --no-build ${pack_args[*]} --output "\$feed" -nologo -tl:off > "$case_dir/pack.log" 2>&1
pack_rc=\$?
echo "pack_exit=\$pack_rc"
ls -la "\$feed" > "$case_dir/feed-listing.txt" 2>&1
find "$clone/src" -name '*.nupkg' > "$case_dir/src-nupkgs.txt" 2>&1
rm -rf -- "\$feed"
"$clone/tests/package-contents.sh" > "$case_dir/package-contents.log" 2>&1
pkg_rc=\$?
echo "package_contents_exit=\$pkg_rc"
exit \$pkg_rc
EOF
)

  set +e
  if [[ $mode == "sandbox" && -n $sandbox ]]; then
    env -u TMPDIR "${env_args[@]}" "$sandbox" bash -c "$script" > "$case_dir/meta.txt" 2>&1
    pkg_exit=$?
  elif [[ $mode == "sandbox" ]]; then
    echo "SKIP: bin/sandbox not found" > "$case_dir/meta.txt"
    pkg_exit=0
  else
    env "${env_args[@]}" bash -c "$script" > "$case_dir/meta.txt" 2>&1
    pkg_exit=$?
  fi
  set -e

  rm -rf -- "$work"
  pack_exit=$(sed -n 's/^pack_exit=//p' "$case_dir/meta.txt" | tail -n 1)
  local nupkg_count
  nupkg_count=$(grep -c '\.nupkg$' "$case_dir/feed-listing.txt" 2>/dev/null || true)
  echo "case=$name mode=$mode reuse=$reuse pack_exit=${pack_exit:-?} feed_nupkgs=${nupkg_count:-0} package_contents_exit=$pkg_exit" | tee -a "$out_dir/summary.txt"
}

failed=0
run_case_checked() {
  run_case "$@"
  local rc
  rc=$(sed -n 's/^package_contents_exit=//p' "$out_dir/$1/meta.txt" | tail -n 1)
  if (( check_mode )) && [[ ${rc:-0} != "0" ]]; then
    failed=1
  fi
}

run_case_checked "ordinary-reuse-on" "ordinary" "enabled"
run_case_checked "ordinary-reuse-off" "ordinary" "disabled"
run_case_checked "sandbox-reuse-on" "sandbox" "enabled"
run_case_checked "sandbox-reuse-off" "sandbox" "disabled"

echo "Diagnostics saved under $out_dir"
exit $failed
