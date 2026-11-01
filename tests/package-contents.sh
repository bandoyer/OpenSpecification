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

# Each project under src/ packs a package whose lib/<framework>/ folders hold only <id>.dll and <id>.xml.
lib_folders_hold_only_assembly_and_docs() {
    local project id nupkg folders folder actual expected
    for project in "$root"/src/*/*.csproj; do
        id=$(basename "$project" .csproj)
        nupkg=$(find "$feed" -maxdepth 1 -name "$id.[0-9]*.nupkg" -print -quit)
        if [[ -z $nupkg ]]; then
            echo "FAIL $id: no package"; failed=1; continue
        fi
        folders=$(unzip -Z1 "$nupkg" | sed -n 's|^\(lib/[^/]*\)/.*|\1|p' | sort -u)
        if [[ -z $folders ]]; then
            echo "FAIL $id: no lib/ folder"; failed=1; continue
        fi
        for folder in $folders; do
            actual=$(unzip -Z1 "$nupkg" | grep "^$folder/" | sort)
            expected=$(printf '%s\n' "$folder/$id.dll" "$folder/$id.xml")
            if [[ $actual == "$expected" ]]; then
                echo "ok $id $folder"
            else
                echo "FAIL $id $folder holds:" $actual; failed=1
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
