#!/usr/bin/env bash
# Locks the verify helper's guards. Cleanup, and each command that writes into a folder, refuses a decoy outside the
# scratch root, a path that climbs out with .., a folder inside a scratch item, and a link out of the scratch root,
# while cleanup of an item the helper made still succeeds. The [pr] suite in skillflow.toml runs it.
# Usage: guards.sh [<helper>], where the helper defaults to ../bin/verify; prints one line per case, exits 1 on a failure.
set -uo pipefail

HERE=$(cd "$(dirname "$(realpath "${BASH_SOURCE[0]}")")" && pwd)
V=${1:-$HERE/../bin/verify}
# mktemp names, since $$ is the same in every sandboxed run.
SCRATCH=$(mktemp -d /tmp/verify-specification-guards.XXXXXX)
export VERIFY_SPECIFICATION_SCRATCH=$SCRATCH
ESCAPE=$SCRATCH-escape
DECOYS=()
trap 'rm -rf -- "$SCRATCH" "$ESCAPE" "${DECOYS[@]}"' EXIT

failed=0
pass() { echo "ok: $1"; }
fail() { echo "FAIL: $1"; failed=1; }

# A decoy carries the helper's marker and a project file, so only the path checks stand between it and the helper.
new_decoy() {
    local decoy
    decoy=$(mktemp -d /tmp/verify-specification-decoy.XXXXXX)
    echo keep > "$decoy/keep"
    echo decoy > "$decoy/.verify-made"
    echo "<Project" > "$decoy/Consumer.csproj"
    DECOYS+=("$decoy")
}
snapshot() { (cd "$1" 2>/dev/null && find . -printf '%p %s\n' | sort && cat keep); }

# refuses <case> <helper arguments>: expects the helper to refuse, and the case's own decoy to stay as it was. In the
# arguments, @DECOY@ names that decoy, @DECOYNAME@ its name in /tmp, and @LINK@ a link to it in the scratch root.
refuses() {
    local case=$1 decoy link before out status arg
    local -a args=()
    shift
    new_decoy
    decoy=${DECOYS[-1]}
    link=$SCRATCH/consumers/link-${#DECOYS[@]}
    ln -s "$decoy" "$link"
    before=$(snapshot "$decoy")
    for arg; do
        arg=${arg//@DECOYNAME@/$(basename "$decoy")}
        arg=${arg//@DECOY@/$decoy}
        args+=("${arg//@LINK@/$link}")
    done
    out=$("$V" "${args[@]}" 2>&1) && status=0 || status=$?
    rm -f -- "$link"
    if [[ $status -eq 0 || $out != *refused* ]]; then
        fail "$case: exit $status, output: ${out//$'\n'/ | }"
    elif [[ $(snapshot "$decoy") != "$before" ]]; then
        fail "$case: the decoy changed"
    else
        pass "$case"
    fi
}

TREE=$("$V" tree HEAD)
if [[ -n $TREE && -d $TREE/src ]]; then pass "tree exports HEAD to $TREE"; else echo "FAIL: verify tree HEAD made no tree"; exit 1; fi

refuses "cleanup of a decoy outside the scratch root" cleanup @DECOY@
refuses "cleanup of a path that climbs out with .." cleanup "$SCRATCH/trees/../../@DECOYNAME@"
refuses "cleanup of a link out of the scratch root" cleanup @LINK@
refuses "cleanup of a folder inside a scratch item" cleanup "$TREE/src"
[[ -d $TREE/src ]] || fail "cleanup removed $TREE/src"
refuses "consumer from a decoy tree" consumer @DECOY@ decoy
refuses "consumer from a folder inside a tree" consumer "$TREE/src" inside
refuses "consumer with a name that climbs out" consumer "$TREE" "../../$(basename "$ESCAPE")"
[[ ! -e $ESCAPE ]] || fail "consumer wrote $ESCAPE"
refuses "efcore-consumer from a decoy tree" efcore-consumer @DECOY@ decoy
refuses "efcore-consumer from a folder inside a tree" efcore-consumer "$TREE/src" inside
refuses "efcore-consumer with a name that climbs out" efcore-consumer "$TREE" "../../$(basename "$ESCAPE")"
[[ ! -e $ESCAPE ]] || fail "efcore-consumer wrote $ESCAPE"
refuses "pack of a decoy tree" pack @DECOY@
refuses "pack of a folder inside a tree" pack "$TREE/src"
mkdir "$TREE/feed"
refuses "package-consumer from a decoy tree" package-consumer @DECOY@ migration decoy
refuses "package-consumer with a name that climbs out" package-consumer "$TREE" migration "../../$(basename "$ESCAPE")"
[[ ! -e $ESCAPE ]] || fail "package-consumer wrote $ESCAPE"
refuses "package-consumer with a template that climbs out" package-consumer "$TREE" "../../@DECOYNAME@" decoy-template
PACKAGES=$("$V" package-consumer "$TREE" migration guards-packages)
if [[ -n $PACKAGES && -f $PACKAGES/NuGet.config && -f $PACKAGES/Consumer.csproj ]] && grep -qF "value=\"$TREE/feed\"" "$PACKAGES/NuGet.config"; then
    pass "package-consumer makes $PACKAGES from its template, restoring from the tree's feed"
else
    fail "package-consumer made no NuGet.config naming $TREE/feed in '$PACKAGES'"
fi
[[ -z $PACKAGES ]] || "$V" cleanup "$PACKAGES" >/dev/null
refuses "efcore-consumer --package with a name that climbs out" efcore-consumer --package "$TREE" "../../$(basename "$ESCAPE")"
[[ ! -e $ESCAPE ]] || fail "efcore-consumer --package wrote $ESCAPE"
refuses "consumer --package from a decoy tree" consumer --package @DECOY@ decoy
refuses "consumer --framework with a path for a framework" consumer --framework "../../@DECOYNAME@" "$TREE" decoy-framework
# --package names the provider package at its project's <Version>, and --framework sets the framework and the SQL Server
# provider's version, which is the EF Core version Directory.Packages.props gives that framework.
L=$("$V" library "$TREE")
PROVIDER_VERSION=$(sed -nE 's/.*<Version>([^<]+)<\/Version>.*/\1/p' "$TREE/src/$L.EntityFrameworkCore/$L.EntityFrameworkCore.csproj" | head -1)
NET9_EFCORE=$(sed -n "/== 'net9.0'/,/<\/ItemGroup>/p" "$TREE/Directory.Packages.props" \
    | sed -nE 's/.*Include="Microsoft\.EntityFrameworkCore" Version="([^"]+)".*/\1/p' | head -1)
OPTIONS=$("$V" efcore-consumer --package --framework net9.0 "$TREE" guards-options)
if [[ -n $OPTIONS && -n $PROVIDER_VERSION && -n $NET9_EFCORE ]] \
    && grep -qF "<TargetFramework>net9.0</TargetFramework>" "$OPTIONS/Consumer.csproj" \
    && grep -qF "<PackageReference Include=\"$L.EntityFrameworkCore\" Version=\"$PROVIDER_VERSION\" />" "$OPTIONS/Consumer.csproj" \
    && grep -qF "Include=\"Microsoft.EntityFrameworkCore.SqlServer\" Version=\"$NET9_EFCORE\"" "$OPTIONS/Consumer.csproj" \
    && ! grep -q '<ProjectReference\|@[A-Z]*@' "$OPTIONS/Consumer.csproj" \
    && grep -qF "value=\"$TREE/feed\"" "$OPTIONS/NuGet.config"; then
    pass "efcore-consumer --package --framework net9.0 references $L.EntityFrameworkCore $PROVIDER_VERSION from the feed, with SQL Server provider $NET9_EFCORE"
else
    fail "efcore-consumer --package --framework net9.0 made '$OPTIONS', whose project is: $(tr '\n' ' ' < "$OPTIONS/Consumer.csproj" 2>/dev/null)"
fi
[[ -z $OPTIONS ]] || "$V" cleanup "$OPTIONS" >/dev/null
rmdir "$TREE/feed"
out=$("$V" consumer --package "$TREE" guards-no-feed 2>&1) && status=0 || status=$?
if [[ $status -ne 0 && $out == *"has no feed"* && ! -e $SCRATCH/consumers/guards-no-feed ]]; then
    pass "consumer --package from a tree without a feed stops before it writes"
else
    fail "consumer --package without a feed: exit $status, output: ${out//$'\n'/ | }"
fi
# A commit can hold a log's path as a link, and git archive keeps it in the tree. pack and warnings must unlink it rather
# than write through it. A stand-in dotnet prints one line and succeeds, so no real build runs.
STANDIN=$(mktemp -d /tmp/verify-specification-standin.XXXXXX)
DECOYS+=("$STANDIN")
printf '#!/usr/bin/env bash\necho "stand-in dotnet $1"\n' > "$STANDIN/dotnet"
chmod +x "$STANDIN/dotnet"
WARNINGS_PROJECT=$(cd "$TREE" && ls src/*.EntityFramework6/*.csproj | head -1)
WARNINGS_LOG=$TREE/warnings-$(basename "$WARNINGS_PROJECT" .csproj).log
for planted in "pack:$TREE/pack.log" "warnings:$WARNINGS_LOG"; do
    command=${planted%%:*} link=${planted#*:}
    new_decoy
    sentinel=${DECOYS[-1]}/keep
    ln -s "$sentinel" "$link"
    if [[ $command == pack ]]; then
        out=$(PATH=$STANDIN:$PATH "$V" pack "$TREE" 2>&1) && status=0 || status=$?
    else
        out=$(PATH=$STANDIN:$PATH "$V" warnings "$TREE/$WARNINGS_PROJECT" 2>&1) && status=0 || status=$?
    fi
    if [[ $(cat "$sentinel") == keep && ! -L $link ]]; then
        pass "$command unlinks a planted log link rather than write through it"
    else
        fail "$command with a planted log link: exit $status, sentinel now '$(cat "$sentinel")', output: ${out//$'\n'/ | }"
    fi
    rm -rf -- "$link" "$TREE/feed"
done
refuses "build of a decoy consumer" build @DECOY@
refuses "build through a link out of the scratch root" build @LINK@
refuses "build of a folder inside a scratch item" build "$TREE/src"
refuses "run of a decoy consumer" run @DECOY@
refuses "warnings of a decoy project" warnings @DECOY@/Consumer.csproj
refuses "warnings of a project that climbs out with .." warnings "$TREE/../../../@DECOYNAME@/Consumer.csproj"
refuses "benchmark of a decoy tree" benchmark @DECOY@ "$SCRATCH" IncludeStep
refuses "compare with a decoy base tree" benchmark compare @DECOY@ "$TREE" "$SCRATCH" IncludeStep
refuses "compare with a decoy branch tree" benchmark compare "$TREE" @DECOY@ "$SCRATCH" IncludeStep

CONSUMER=$("$V" consumer "$TREE" guards)
if [[ -n $CONSUMER && -f $CONSUMER/Consumer.csproj ]]; then pass "consumer makes $CONSUMER"; else fail "consumer made no project from $TREE"; fi
if [[ -n $CONSUMER ]] && "$V" cleanup "$CONSUMER" >/dev/null && [[ ! -e $CONSUMER ]]; then
    pass "cleanup removes the consumer it made"
else
    fail "cleanup didn't remove the consumer '$CONSUMER'"
fi
EFCORE=$("$V" efcore-consumer "$TREE" guards-efcore)
if [[ -n $EFCORE && -f $EFCORE/Consumer.csproj ]] && ! grep -q '@[A-Z]*@' "$EFCORE/Consumer.csproj" \
    && grep -q 'Include="Microsoft.EntityFrameworkCore.SqlServer" Version="[0-9]' "$EFCORE/Consumer.csproj" \
    && grep -qF '<TargetFramework>net10.0</TargetFramework>' "$EFCORE/Consumer.csproj" \
    && grep -q '<ProjectReference Include="[^"]*EntityFrameworkCore.csproj" />' "$EFCORE/Consumer.csproj" \
    && [[ ! -e $EFCORE/NuGet.config ]]; then
    pass "efcore-consumer makes $EFCORE for net10.0, referencing the provider project, with the tree's SQL Server provider version"
else
    fail "efcore-consumer made no project from $TREE, or left a placeholder in '$EFCORE/Consumer.csproj'"
fi
[[ -z $EFCORE ]] || "$V" cleanup "$EFCORE" >/dev/null

new_decoy
ln -s "${DECOYS[-1]}" "$SCRATCH/consumers/link"
before=$(snapshot "${DECOYS[-1]}")
out=$("$V" cleanup 2>&1) && status=0 || status=$?
if [[ $status -ne 0 && $out == *refused* && ! -e $TREE && $(snapshot "${DECOYS[-1]}") == "$before" ]]; then
    pass "cleanup of everything removes the tree and refuses the link"
else
    fail "cleanup of everything: exit $status, tree left: $([[ -e $TREE ]] && echo yes || echo no), output: ${out//$'\n'/ | }"
fi

# A tree older than a day goes when the next folder is made, so a consumer from it is refused rather than left
# pointing at nothing.
TREE=$("$V" tree HEAD)
touch -d '2 days ago' "$TREE/.verify-made"
out=$("$V" consumer "$TREE" aged 2>&1) && status=0 || status=$?
if [[ $status -ne 0 && $out == *refused* && ! -e $TREE && ! -e $SCRATCH/consumers/aged ]]; then
    pass "consumer from a tree the day-old cleanup removed"
else
    fail "consumer from a day-old tree: exit $status, consumer made: $([[ -e $SCRATCH/consumers/aged ]] && echo yes || echo no), output: ${out//$'\n'/ | }"
fi

exit $failed
