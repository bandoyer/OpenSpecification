#!/usr/bin/env bash
# Checks that `verify benchmark` passes only when every expected method has an allocation result, and its compare mode
# only when every workload also has the branch's Ratio and RatioSD. A stand-in dotnet on PATH plays BenchmarkDotNet
# back from saved runs in benchmark-reports/: #6's report with allocations, #4's `SingleSearch | NA | NA` report, #6's
# first run, which BenchmarkDotNet rejected for an IQueryable return and which wrote only a log, and #7's same-process
# comparison. Both failures exited 0. The [pr] suite in skillflow.toml runs it.
# Usage: benchmark.sh [<helper>], where the helper defaults to ../bin/verify; prints one line per case, exits 1 on a
# failure.
set -uo pipefail

HERE=$(cd "$(dirname "$(realpath "${BASH_SOURCE[0]}")")" && pwd)
V=${1:-$HERE/../bin/verify}
SAVED=$HERE/benchmark-reports
SCRATCH=$(mktemp -d /tmp/verify-specification-benchmark.XXXXXX)
export VERIFY_SPECIFICATION_SCRATCH=$SCRATCH
STANDIN=$(mktemp -d /tmp/verify-specification-standin.XXXXXX)
WORKLOAD=$(mktemp -d /tmp/verify-specification-workload.XXXXXX)
trap 'rm -rf -- "$SCRATCH" "$STANDIN" "$WORKLOAD"' EXIT

failed=0
pass() { echo "ok: $1"; }
fail() { echo "FAIL: $1"; failed=1; }

TREE=$("$V" tree HEAD) || { echo "FAIL: verify tree HEAD"; exit 1; }
RESULTS=$WORKLOAD/results-$(basename "$TREE")
echo '// The workload; the stand-in dotnet never builds it.' > "$WORKLOAD/Program.cs"

# `dotnet run ... --artifacts <folder>` copies $REPORT into the folder's results/ and $LOG into the folder, as
# BenchmarkDotNet would write them, and exits 0. It exits 3 when the helper didn't set up the project beside it, for
# the helper's DEFAULT_FRAMEWORK, which the consumers also target.
FRAMEWORK=$(sed -n 's/^DEFAULT_FRAMEWORK=//p' "$V")
[[ -n $FRAMEWORK ]] || { echo "FAIL: $V has no DEFAULT_FRAMEWORK= line"; exit 1; }
export FRAMEWORK
cat > "$STANDIN/dotnet" <<'EOF'
#!/usr/bin/env bash
[[ -f Program.cs && -f Benchmark.csproj ]] && ! grep -q '@[A-Z]*@' Benchmark.csproj \
    && grep -q "<TargetFramework>$FRAMEWORK</TargetFramework>" Benchmark.csproj || exit 3
while [[ $# -gt 0 && $1 != --artifacts ]]; do shift; done
artifacts=$2
mkdir -p "$artifacts/results"
[[ -z ${REPORT:-} ]] || cp "$REPORT" "$artifacts/results/Saved-report-github.md"
[[ -z ${LOG:-} ]] || cp "$LOG" "$artifacts/"
exit 0
EOF
chmod +x "$STANDIN/dotnet"

# bench <case> <expected exit: 0 or nonzero> <a line the output must hold> <methods>...
bench() {
    local case=$1 want=$2 line=$3 out status
    shift 3
    out=$(PATH=$STANDIN:$PATH "$V" benchmark "$TREE" "$WORKLOAD" "$@" 2>&1) && status=0 || status=$?
    if [[ $want == 0 && $status -ne 0 ]] || [[ $want != 0 && $status -eq 0 ]]; then
        fail "$case: exit $status, output: ${out//$'\n'/ | }"
    elif ! grep -qxF -- "$line" <<<"$out"; then
        fail "$case: no line \"$line\" in: ${out//$'\n'/ | }"
    else
        pass "$case"
    fi
}

export REPORT LOG
REPORT=$SAVED/include-strings-a07fc11-report-github.md LOG= \
    bench "a report with an allocation in every row passes" 0 "allocated IncludeStep: -, 656 B, 1248 B, 1480 B" IncludeStep
REPORT=$SAVED/include-strings-a07fc11-report-github.md LOG= \
    bench "a method missing from the report fails" nonzero "no allocation NoSuchMethod: no row in the report" IncludeStep NoSuchMethod
REPORT=$SAVED/search-parameters-na-report-github.md LOG= \
    bench "#4's NA report fails" nonzero "no allocation SingleSearch: no Allocated column" SingleSearch
REPORT= LOG=$SAVED/include-strings-iqueryable-rejected.log \
    bench "#6's rejected run, with no report, fails" nonzero \
    "no allocation IncludeStep: BenchmarkDotNet wrote no report in $RESULTS/results" IncludeStep
if cmp -s "$SAVED/include-strings-iqueryable-rejected.log" "$RESULTS/include-strings-iqueryable-rejected.log"; then
    pass "the run's raw log stays in the workload's results folder"
else
    fail "the raw log isn't in $RESULTS"
fi

rm -rf -- "$RESULTS"
mkdir "$RESULTS"
echo keep > "$RESULTS/keep"
out=$(PATH=$STANDIN:$PATH REPORT= LOG= "$V" benchmark "$TREE" "$WORKLOAD" IncludeStep 2>&1) && status=0 || status=$?
if [[ $status -ne 0 && $out == *refused* && $(cat "$RESULTS/keep") == keep ]]; then
    pass "a results folder the helper didn't make is refused and kept"
else
    fail "a results folder the helper didn't make: exit $status, output: ${out//$'\n'/ | }"
fi

# Compare mode, with the tree's OrderEvaluator.cs as the base's file to compile in.
echo "Evaluators/OrderEvaluator.cs" > "$WORKLOAD/base-files"
COMPARED=$WORKLOAD/compare-$(basename "$TREE")-$(basename "$TREE")
SAME_PROCESS=$SAVED/order-evaluator-same-process-report-github.md
compare() {
    local case=$1 want=$2 line=$3 out status
    shift 3
    out=$(PATH=$STANDIN:$PATH "$V" benchmark compare "$TREE" "$TREE" "$WORKLOAD" "$@" 2>&1) && status=0 || status=$?
    if [[ $want == 0 && $status -ne 0 ]] || [[ $want != 0 && $status -eq 0 ]]; then
        fail "$case: exit $status, output: ${out//$'\n'/ | }"
    elif ! grep -qxF -- "$line" <<<"$out"; then
        fail "$case: no line \"$line\" in: ${out//$'\n'/ | }"
    else
        pass "$case"
    fi
}

REPORT=$SAME_PROCESS LOG= \
    compare "#7's same-process report passes" 0 "compare OneKey: allocated 1616 B, then 1488 B; Ratio 1.00, RatioSD 0.02" \
    NoOrder OneKey TwoKeys FourKeys
# The stand-in builds nothing, so this checks what the copy holds for the real build: each of the provider's global
# usings as a plain using, the branch's library namespace, no byte order mark (the tree's GlobalUsings.cs and
# OrderEvaluator.cs each start with one), the namespace MergeBase, and no other name for the library. The second case
# takes the base's file from main before #47 (3e2e749), whose library is Ardalis.Specification, so the copy must name
# the branch's library in its place.
BRANCH_LIBRARY=$("$V" library "$TREE")
# check_copy <case> <base tree>
check_copy() {
    local case=$1 base=$2 base_library copy problems= names name
    base_library=$("$V" library "$base")
    copy=$SCRATCH/consumers/compare-$(basename "$base")-$(basename "$TREE")/MergeBase/Evaluators/OrderEvaluator.cs
    grep -q $'\xEF\xBB\xBF' "$copy" && problems+=" a byte order mark;"
    grep -q '^global using ' "$copy" && problems+=" a global using;"
    names=$(grep -o 'global using [A-Za-z0-9_.]*;' "$base/src/$base_library.EntityFramework6/GlobalUsings.cs" | sed 's/^global using //; s/;$//')
    [[ -n $names ]] || problems+=" no global using in the base's GlobalUsings.cs to check;"
    for name in $names; do
        grep -qx "using ${name//$base_library/$BRANCH_LIBRARY};" "$copy" || problems+=" no using ${name//$base_library/$BRANCH_LIBRARY};"
    done
    grep -qx "using $BRANCH_LIBRARY;" "$copy" || problems+=" no using $BRANCH_LIBRARY;"
    grep -qx 'namespace MergeBase;' "$copy" && ! grep -q '^namespace [^M]' "$copy" || problems+=" $(grep -m1 '^namespace' "$copy" 2>&1);"
    [[ $BRANCH_LIBRARY == Ardalis.Specification ]] || ! grep -q 'Ardalis' "$copy" || problems+=" $(grep -m1 Ardalis "$copy");"
    if [[ -z $problems ]]; then
        pass "$case"
    else
        fail "$case: the copy of the base's OrderEvaluator.cs has$problems"
    fi
}
check_copy "compare copies the base's file in with the provider's usings, in the namespace MergeBase" "$TREE"
BEFORE_RENAME=$("$V" tree 3e2e749) || fail "verify tree 3e2e749"
if [[ -n $BEFORE_RENAME ]]; then
    out=$(PATH=$STANDIN:$PATH REPORT=$SAME_PROCESS LOG= "$V" benchmark compare "$BEFORE_RENAME" "$TREE" "$WORKLOAD" OneKey 2>&1) \
        || fail "compare from a base before #47: ${out//$'\n'/ | }"
    check_copy "compare from a base before #47 names the branch's library, $BRANCH_LIBRARY, in the copy" "$BEFORE_RENAME"
fi

# The same report without its Ratio and RatioSD columns, as BenchmarkDotNet writes it when no method is the baseline.
awk -F'|' '
    /^\|/ {
        if (!seen) { for (i = 1; i <= NF; i++) { h = $i; gsub(/ /, "", h); if (h == "Ratio" || h == "RatioSD") drop[i] = 1 }; seen = 1 }
        out = $1
        for (i = 2; i <= NF; i++) if (!(i in drop)) out = out "|" $i
        print out
        next
    }
    { print }' "$SAME_PROCESS" > "$STANDIN/no-ratio-report-github.md"
if grep -qE '\| (Ratio|RatioSD) +\|' "$STANDIN/no-ratio-report-github.md" || ! grep -q 'Alloc Ratio' "$STANDIN/no-ratio-report-github.md"; then
    fail "the test's cut of the Ratio and RatioSD columns went wrong"
fi
REPORT=$STANDIN/no-ratio-report-github.md LOG= \
    compare "a report without Ratio or RatioSD fails" nonzero \
    "no comparison OneKey: allocated 1616 B, then 1488 B; Ratio no Ratio column, RatioSD no RatioSD column" OneKey
REPORT=$SAVED/search-parameters-na-report-github.md LOG= \
    compare "#4's NA report fails" nonzero \
    "no comparison SingleSearch: MergeBase_SingleSearch and Branch_SingleSearch need one row each in the report" SingleSearch

echo "../../../../etc/passwd.cs" > "$WORKLOAD/base-files"
out=$(PATH=$STANDIN:$PATH REPORT= LOG= "$V" benchmark compare "$TREE" "$TREE" "$WORKLOAD" OneKey 2>&1) && status=0 || status=$?
if [[ $status -ne 0 && $out == *refused* ]]; then
    pass "a base file that climbs out of the provider is refused"
else
    fail "a base file that climbs out: exit $status, output: ${out//$'\n'/ | }"
fi

exit $failed
