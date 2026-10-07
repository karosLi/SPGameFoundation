#!/usr/bin/env bash
# Run the ordinary test adapters and export cold evidence. No external writes or device settings.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"
if [ "$#" -ne 1 ]; then echo "usage: $0 OUTPUT_DIRECTORY (new or empty directory)" >&2; exit 2; fi
out="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$1")"
if [ -e "$out" ] && [ -n "$(ls -A "$out")" ]; then echo 'Use an empty output directory; stale evidence must not mix with this run.' >&2; exit 2; fi
case "$out/" in "$root/Assets/"*|"$root/Tools/"*) echo 'Keep generated evidence outside Assets/Tools.' >&2; exit 2;; esac
# A commit label cannot describe changed or untracked executable/configuration inputs.
git diff --exit-code HEAD -- Assets Tools >/dev/null
if [ -n "$(git ls-files --others --exclude-standard -- Assets Tools)" ]; then echo 'Commit all Assets/Tools inputs before exporting exact evidence.' >&2; exit 2; fi
export SPF_ACCEPTANCE_COMMIT="$(git rev-parse HEAD)" SPF_ACCEPTANCE_TREE="$(git rev-parse HEAD^{tree})"
export SPF_ACCEPTANCE_OUT="$out/raw"
mkdir -p "$SPF_ACCEPTANCE_OUT"
python3 Tools/Acceptance/verify_inputs.py "$root" > "$out/toolchain.json"
python3 Tools/DotnetHarness/generate.py > "$out/generate.log"
dotnet build Tools/DotnetHarness/.gen/Harness.proj -nologo -v q -m:1 > "$out/build.log" 2>&1
for assembly in FoundationAcceptance.Tests.EditMode ShooterFoundation.Tests.EditMode PlatformerFoundation.Tests.EditMode; do
  dotnet test "Tools/DotnetHarness/.gen/$assembly/$assembly.csproj" --no-build -nologo -v q -m:1 --filter 'FullyQualifiedName~Acceptance' \
    --logger "trx;LogFileName=$assembly.trx" --results-directory "$out/tests" > "$out/$assembly.log" 2>&1
done
python3 -m unittest discover -s Tools/Acceptance -p 'test_*.py' -v > "$out/schema-tests.log" 2>&1
python3 Tools/Acceptance/verify_inputs.py "$root" > "$out/toolchain-after.json"
cmp "$out/toolchain.json" "$out/toolchain-after.json"
python3 Tools/Acceptance/mobile_acceptance_report.py --evidence "$SPF_ACCEPTANCE_OUT" --toolchain "$out/toolchain.json" --output "$out/report"
echo "Exact commit $SPF_ACCEPTANCE_COMMIT, tree $SPF_ACCEPTANCE_TREE. JSON/Markdown: $out/report.*"
