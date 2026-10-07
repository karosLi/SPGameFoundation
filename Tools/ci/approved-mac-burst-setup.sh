#!/usr/bin/env bash
# One authorized Mac CI setup invocation. This is never called by ordinary test workflows.
set -euo pipefail
cd "$(dirname "$0")/../.."
if [ "${GITHUB_EVENT_NAME:-}" != push ] ||
   [ "${GITHUB_REF:-}" != refs/heads/dot/ci-burst-setup-approved-20261007 ] ||
   [ "${SPF_APPROVED_BURST_SETUP:-}" != 2026-10-07 ] || [ "$(uname)" != Darwin ]; then
  echo '::error::This setup is restricted to the explicitly approved Mac CI branch.'; exit 1
fi
if [ "$(git rev-parse HEAD)" != "${GITHUB_SHA:-}" ]; then
  echo '::error::Checkout does not match the triggering commit.'; exit 1
fi
python3 - <<'PY'
import json, os
event = json.load(open(os.environ['GITHUB_EVENT_PATH']))
assert event.get('created') is True, 'Only the dedicated branch-creation event may activate Burst'
PY
version=$(sed -n 's/^m_EditorVersion: *//p' ProjectSettings/ProjectVersion.txt | tr -d '\r')
editor="${UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity}"
if [ ! -x "$editor" ]; then echo "::error::Unity executable not found: $editor"; exit 1; fi
launch=()
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null)" = 1 ]; then
  if ! lipo -archs "$editor" | grep -qw arm64; then
    echo '::error::Apple Silicon CI requires an arm64 Unity editor for this native Burst verification.'; exit 1
  fi
  launch=(arch -arm64)
fi
rm -rf BurstSetupArtifacts
mkdir -p BurstSetupArtifacts
git rev-parse HEAD HEAD^{tree} > BurstSetupArtifacts/revision.txt
run_editor() {
  local stage=$1 method=$2; shift 2
  echo "::group::Fresh Editor: $stage"
  if ! ${launch[@]+"${launch[@]}"} "$editor" -batchmode -nographics -quit -projectPath "$PWD" \
    -executeMethod "SPF.Editor.CiBurstSetup.$method" "$@" -logFile "$PWD/BurstSetupArtifacts/$stage.log"; then
    tail -n 150 "BurstSetupArtifacts/$stage.log" || true
    echo '::endgroup::'; return 1
  fi
  cat "BurstSetupArtifacts/$stage.txt"
  echo '::endgroup::'
}
run_editor before Inspect
# Verify the resolved package's persistence surface before allowing the option setter to run.
python3 - <<'PY'
import json, pathlib, re, shutil
root = pathlib.Path('Library/PackageCache')
paths = list(root.glob('com.unity.burst@*/Runtime/Editor/BurstEditorOptions.cs'))
assert len(paths) == 1, f'Expected exactly one resolved Burst source, found {len(paths)}'
source = paths[0]
assert json.loads((source.parents[2] / 'package.json').read_text())['version'] == '1.8.27'
text = source.read_text()
constants = dict(re.findall(r'const string (\w+) = "([^"]+)"', text))
editor_keys = {constants[name] for name in re.findall(r'EditorPrefs.SetBool\((\w+),', text)}
assert editor_keys == {'BurstCompilation', 'BurstCompileSynchronously', 'BurstShowTimings',
                       'BurstDebug', 'BurstForceSafetyChecks'}, editor_keys
session_keys = {constants[name] for name in re.findall(r'SessionState.SetBool\((\w+),', text)}
assert session_keys == {'BurstSafetyChecks'}, session_keys
shutil.copyfile(source, 'BurstSetupArtifacts/resolved-BurstEditorOptions.cs')
PY
run_editor activation EnableApproved --spf-enable-approved-ci-burst-20261007
run_editor verified VerifyFreshProcess
python3 - <<'PY'
from pathlib import Path
def values(name):
    return dict(line.split(': ', 1) for line in Path('BurstSetupArtifacts/' + name).read_text().splitlines() if ': ' in line)
before, after = values('before.txt'), values('verified.txt')
for key in ('BurstCompileSynchronously', 'BurstShowTimings', 'BurstDebug', 'BurstForceSafetyChecks'):
    assert before[key] == after[key], f'{key} changed across fresh Editor processes'
assert after['BurstCompilation'] == 'exists=True, value=True'
print('Fresh process readback preserved all four non-target persisted preferences.')
PY
