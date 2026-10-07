# Stage A cold composition probe: reproduction

This is a documentation validation artifact for [the compatibility matrix](../FoundationCompatibilityMatrix.md) and [its compact inventory](FoundationCompatibilityInventory-20261007.json). It is not a runtime subsystem, Unity test assembly, performance benchmark, leak detector, or portable save-schema implementation.

## Pinned evidence

- Gameplay source: local `62c5b7f5765a879aaf8ec9fa69bc7601ae3fa969`, tree `04435658d8b1c2e95e286b093405141b5fd6040b`; the subsequent `de755e1` closure commit changes documentation only.
- Probe source: [FoundationCompatibilityProbe.cs](FoundationCompatibilityProbe.cs).
- Probe source SHA-256: `571d9c0cedd9a3d55871c5a74751933884c4238e875d01ba71dda81090fd47ed`.
- Original cold output SHA-256: `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`. The retained, formatted source was rebuilt and its output compared byte-for-byte with the original probe output. The semantic inventory comparison below is the intended guard; JSON whitespace/order are not a gameplay contract.
- Observed environment: Linux, .NET SDK 8.0.425, repository Unity stubs and pinned Unity.Mathematics 1.2.6 source (`f110c8c230d253654afed153569030a587cc7557`). Native Unity/Burst/GPU/physical mobile execution is outside this probe.

The source creates the 19 listed Modes at seed 123 through actual factories, then `SimSession.Create`, and disposes each Session. It reads initialized metadata after `OnCreate`, but does not call Start, Step, Update or OnTick. Config/Module/Mode assets are explicitly destroyed at the end of their diagnostic ownership. This is not a proof of production Unity object lifetime or zero leaks, because destruction is exercised against stubs.

Clock labels for Puzzle/Story are annotations from the actual Bootstrap source. Their Bootstraps are not executed. The probe intentionally uses cold reflection to inspect internal ordered storage and scope lists. It exports key names rather than process-local AccessKey IDs, and does not emit native struct sizes as a cross-backend ABI. A future private-field rename may require an explicit probe update; do not bypass a failed comparison by regenerating the baseline without review.

## Build and run from repository root

Use an isolated worktree containing this artifact and the intended source revision. Have .NET 8 and Python 3 on PATH. Dependency setup follows the existing [harness script](../../Tools/DotnetHarness/run.sh); if pinned dependencies are already present, the fetch step reuses them. No production files are modified. Generated harness projects stay in the ignored `.gen` directory, and the probe project/output stay in a fresh temporary directory.

```bash
set -euo pipefail
Tools/DotnetHarness/fetch-deps.sh
python3 Tools/DotnetHarness/generate.py
work="$(mktemp -d)"
export SPF_COMPAT_OUT="$work"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$work/nuget-http}"
python3 - "$PWD" "$work" <<'PY'
from pathlib import Path
from xml.sax.saxutils import escape
import sys
root, work = map(Path, sys.argv[1:])
assemblies = ["SPF.Runtime"] + [name + "Foundation.Runtime" for name in (
    "Snake", "Rpg", "Survivor", "Platformer", "Defense", "Puzzle", "Sling", "Brawler", "Story", "Shooter")]
references = "\n".join(
    '<ProjectReference Include="' + escape(str(root / "Tools/DotnetHarness/.gen" / name / (name + ".csproj")), {'"': '&quot;'}) + '" />'
    for name in assemblies)
source = escape(str(root / "Docs/validation/FoundationCompatibilityProbe.cs"), {'"': '&quot;'})
(work / "Probe.csproj").write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
    '<TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType>'
    '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
    '</PropertyGroup><ItemGroup><Compile Include="' + source + '" />\n' + references +
    '</ItemGroup></Project>\n')
PY
dotnet build "$work/Probe.csproj" -nologo -v minimal -m:1
dotnet "$work/bin/Debug/net8.0/Probe.dll" > "$work/compositions.json"
dotnet "$work/bin/Debug/net8.0/Probe.dll" > "$work/compositions-repeat.json"
cmp "$work/compositions.json" "$work/compositions-repeat.json"
printf 'Cold outputs are in %s\n' "$work"
```

The initial development attempt used a nonpublic `AccessDeclaration.IsBarrier` as if public and failed compilation. The retained source uses explicit diagnostic reflection and was rebuilt successfully. Dependency restore emitted NU1900 warnings for an unwritable vulnerability cache, and cached warnings remained in the verified reproduction despite selecting a writable HTTP cache. They did not prevent compilation or the inventory comparison; successful compilation does not constitute a dependency vulnerability audit. The build/run/compare commands were re-executed from a fresh temporary project with the already-generated assembly graph and pinned dependency cache; dependency acquisition was not repeated.

## Compare the observed composition with the checked-in inventory

Run this in the same shell and repository root as above. Each assertion compares an observed field, not a value inferred from a similar game. Source hashes also ensure a purported exact-baseline check has not silently run on changed code. For later stages, investigate a changed source hash separately from actual composition changes; do not erase the old inventory to obtain a pass.

```bash
python3 - <<'PY'
from pathlib import Path
import hashlib, json, os, re
root = Path.cwd()
out = Path(os.environ["SPF_COMPAT_OUT"])
record = json.loads((root / "Docs/validation/FoundationCompatibilityInventory-20261007.json").read_text())
observed_rows = json.loads((out / "compositions.json").read_text())
observed = {row["id"]: row for row in observed_rows}
assert len(observed_rows) == len(observed) == len(record["compositions"]) == 19
assert set(observed) == {row["audit_id"] for row in record["compositions"]}
for expected in record["compositions"]:
    actual = observed[expected["audit_id"]]
    assert expected["module_ids"] == actual["modules"]
    assert expected["tick_rate"] == actual["settings"]["TickRate"]
    assert expected["max_ticks_per_frame"] == actual["settings"]["MaxTicksPerFrame"]
    assert expected["settings_destroy_capacity"] == actual["settings"]["DestroyQueueCapacity"]
    assert expected["effective_destroy_capacity"] == actual["effectiveDestroyQueueCapacity"]
    assert expected["bootstrap_clock"] == actual["bootstrapClock"]
    assert expected["snapshot_gaps"] == actual["snapshotGaps"]
    tables = [[t["key"], t["capacity"], t["pooled"], t["levelScoped"], t["trackChangedRows"],
        [[c["key"], c["type"]] for c in t["columns"]]] for t in actual["tables"]]
    resources = [[r["key"], r["type"], r["levelScoped"], r["snapshotHook"], r["saved"],
        r["capacity"], r["publicArrayLengths"]] for r in actual["resources"]]
    systems = [[s["type"], s["phase"], s["order"], s["registrationIndex"],
        s["snapshotHook"], s["resetHook"], s["barrier"]] for s in actual["systems"]]
    assert expected["tables"] == tables, expected["audit_id"]
    assert expected["resources"] == resources, expected["audit_id"]
    assert [s[:7] for s in expected["systems"]] == systems, expected["audit_id"]
    assert sorted(s[3] for s in systems) == list(range(len(systems)))
for source in record["source_files"]:
    assert hashlib.sha256((root / source["path"]).read_bytes()).hexdigest() == source["sha256"], source["path"]
for test in record["fixtures"]:
    text = (root / test["path"]).read_text()
    assert re.search(r"\b" + re.escape(test["method"]) + r"\s*\(", text), test
print("PASS: 19 compositions, source hashes, and named test methods match the retained baseline.")
PY
```

This does not execute the named gameplay tests, assert full snapshot coverage from an empty SnapshotGaps result, validate custom authoring configurations, or measure memory/GPU/mobile budgets. After any runtime change, execute the applicable actual regression suites in addition to this cold comparison.
