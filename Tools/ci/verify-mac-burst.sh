#!/usr/bin/env bash
# Read-only preference/native verification after the separately approved setup completed.
set -euo pipefail
cd "$(dirname "$0")/../.."
if [ "${GITHUB_EVENT_NAME:-}" != push ] || [ "${GITHUB_REF:-}" != refs/heads/dot/ci-burst-verify-20261007 ] ||
   [ "$(uname)" != Darwin ] || [ "$(git rev-parse HEAD)" != "${GITHUB_SHA:-}" ]; then
  echo '::error::This verification is restricted to the exact Mac CI verification revision.'; exit 1
fi
version=$(sed -n 's/^m_EditorVersion: *//p' ProjectSettings/ProjectVersion.txt | tr -d '\r')
editor="${UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity}"
if [ ! -x "$editor" ]; then echo "::error::Unity executable not found: $editor"; exit 1; fi
launch=()
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null)" = 1 ]; then
  lipo -archs "$editor" | grep -qw arm64
  launch=(arch -arm64)
fi
rm -rf BurstSetupArtifacts Artifacts
mkdir -p BurstSetupArtifacts
git rev-parse HEAD HEAD^{tree} > BurstSetupArtifacts/revision.txt
read_preferences() {
  local key
  for key in BurstCompilation BurstCompileSynchronously BurstShowTimings BurstDebug BurstForceSafetyChecks; do
    printf '%s: ' "$key"
    defaults read com.unity3d.UnityEditor5.x "$key" 2>/dev/null || echo '<absent>'
  done | tee "BurstSetupArtifacts/$1-storage.txt"
}
read_preferences before
# This entry point reads preferences and runs native witnesses. It has no setter or approval flag.
if ! ${launch[@]+"${launch[@]}"} "$editor" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod SPF.Editor.CiBurstSetup.VerifyFreshProcess -logFile "$PWD/BurstSetupArtifacts/verified.log"; then
  tail -n 150 BurstSetupArtifacts/verified.log || true; exit 1
fi
cat BurstSetupArtifacts/verified.txt
read_preferences after-verification
cmp BurstSetupArtifacts/before-storage.txt BurstSetupArtifacts/after-verification-storage.txt
