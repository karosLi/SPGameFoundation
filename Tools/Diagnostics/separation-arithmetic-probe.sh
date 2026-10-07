#!/usr/bin/env bash
# Usage: script /path/to/Editor/Data/MonoBleedingEdge /path/to/Unity.Mathematics.dll [output-dir]
# Uses the installed Unity Mono + Mathematics, with no Unity license or preferences changed.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
mono="${1:?Unity MonoBleedingEdge directory required}"
math="${2:?real Unity.Mathematics.dll required}"
out="${3:-$root/Artifacts/separation-managed-probe}"
mkdir -p "$out"
cp "$math" "$out/Unity.Mathematics.dll"
"$mono/bin/mono" "$mono/lib/mono/4.5/mcs.exe" -langversion:latest \
  -r:"$mono/lib/mono/4.5/Facades/netstandard.dll" -r:"$out/Unity.Mathematics.dll" \
  -out:"$out/SeparationArithmeticProbe.exe" \
  "$root/Tools/Diagnostics/SeparationArithmeticProbe.cs" \
  "$root/Assets/BrawlerFoundation/Tests/EditMode/BwBeltSeparationArithmetic.cs" \
  "$root/Assets/SinglePlayerFoundation/L2Gameplay/Combat/GroundCombatQueries.cs" \
  "$root/Assets/SinglePlayerFoundation/L2Gameplay/Combat/CombatSweep.cs"
"$mono/bin/mono" --optimize=float32 "$out/SeparationArithmeticProbe.exe" | tee "$out/float32.txt"
"$mono/bin/mono" --optimize=-float32 "$out/SeparationArithmeticProbe.exe" | tee "$out/extended-scalar.txt"
