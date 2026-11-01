#!/usr/bin/env bash
# Checks what the packages ship. Run it after `dotnet build ci.slnf --configuration Release`, since it packs with
# --no-build. Each project under src/ must pack a package whose lib/<framework>/ folders hold only the assembly and
# its .xml docs, and no project's Release build may write a runtimeconfig.json, which only an app's host reads.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
feed=$(mktemp -d "${TMPDIR:-/tmp}/package-contents.XXXXXX")
trap 'rm -rf -- "$feed"' EXIT

dotnet pack "$root/ci.slnf" --configuration Release --no-build --output "$feed" -nologo -tl:off > "$feed/pack.log" 2>&1 \
  || { cat "$feed/pack.log"; echo "FAIL: dotnet pack failed; build ci.slnf in Release first"; exit 1; }

failed=0

# Each project under src/ packs a package with a lib/<framework>/ folder for each framework its TargetFrameworks
# names, and no other, and each folder holds only <id>.dll and <id>.xml.
lib_folders_hold_only_assembly_and_docs() {
  local project id nupkg frameworks expected_folders folders folder actual expected
  for project in "$root"/src/*/*.csproj; do
    id=$(basename "$project" .csproj)
    nupkg=$(find "$feed" -maxdepth 1 -name "$id.[0-9]*.nupkg" -print -quit)
    if [[ -z $nupkg ]]; then
      echo "FAIL $id: no package"; failed=1; continue
    fi
    frameworks=$(dotnet msbuild "$project" -getProperty:TargetFrameworks -nologo)
    expected_folders=$(tr ';' '\n' <<< "$frameworks" | sed -n 's|^ *\([^ ]\{1,\}\) *$|lib/\1|p' | sort -u)
    if [[ -z $expected_folders ]]; then
      echo "FAIL $id: its project names no TargetFrameworks"; failed=1; continue
    fi
    folders=$(unzip -Z1 "$nupkg" | sed -n 's|^\(lib/[^/]*\)/.*|\1|p' | sort -u)
    for folder in $(comm -13 <(echo "$expected_folders") <(echo "$folders")); do
      echo "FAIL $id has $folder, which its TargetFrameworks don't name"; failed=1
    done
    for folder in $expected_folders; do
      actual=$(unzip -Z1 "$nupkg" | grep "^$folder/" | sort || true)
      expected=$(printf '%s\n' "$folder/$id.dll" "$folder/$id.xml")
      if [[ $actual == "$expected" ]]; then
        echo "ok $id $folder"
      else
        echo "FAIL $id $folder holds:" ${actual:-nothing}; failed=1
      fi
    done
  done
}

# No project's Release build writes a runtimeconfig.json.
release_build_writes_no_runtimeconfig() {
  local file
  while IFS= read -r file; do
    echo "FAIL the Release build wrote ${file#"$root"/}"; failed=1
  done < <(find "$root"/src/*/bin/Release -name '*.runtimeconfig.json')
}

lib_folders_hold_only_assembly_and_docs
release_build_writes_no_runtimeconfig
exit $failed
