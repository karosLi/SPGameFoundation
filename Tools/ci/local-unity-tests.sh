#!/usr/bin/env bash
# Runs EditMode + PlayMode tests with a locally installed Unity (self-hosted runner or a developer machine).
# The machine's own Unity Hub sign-in provides the licence (works with Personal).
# Editor path: $UNITY_EDITOR_PATH, else the default Unity Hub install location for ProjectVersion.txt.
set -uo pipefail
cd "$(dirname "$0")/../.."
VERSION=$(sed -n 's/^m_EditorVersion: *//p' ProjectSettings/ProjectVersion.txt | tr -d '\r')
EDITOR="${UNITY_EDITOR_PATH:-}"
if [ -z "$EDITOR" ]; then
  for c in \
    "/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity" \
    "/c/Program Files/Unity/Hub/Editor/$VERSION/Editor/Unity.exe" \
    "C:/Program Files/Unity/Hub/Editor/$VERSION/Editor/Unity.exe" \
    "$HOME/Unity/Hub/Editor/$VERSION/Editor/Unity"; do
    [ -x "$c" ] || [ -f "$c" ] && { EDITOR="$c"; break; }
  done
fi
if [ -z "$EDITOR" ]; then
  echo "::error::Unity $VERSION not found. Install it with Unity Hub or set UNITY_EDITOR_PATH."; exit 1
fi
echo "Unity: $EDITOR"
A=Artifacts; mkdir -p "$A"
PROJECT="$(pwd)"

run() {
  local platform=$1; shift
  echo "::group::${platform} tests"
  "$EDITOR" -batchmode "$@" -projectPath "$PROJECT" -runTests -testPlatform "$platform" \
    -testResults "$PROJECT/$A/${platform}-results.xml" -logFile "$PROJECT/$A/${platform}.log"
  local code=$?
  echo "exit code ${code} (0 = passed, 2 = test failures, other = editor error)"
  echo "::endgroup::"
  [ $code -ne 0 ] && { echo "----- last 150 lines of ${platform}.log -----"; tail -n 150 "$A/${platform}.log" || true; }
  return $code
}
run editmode -nographics; EDIT=$?
run playmode; PLAY=$?
[ $EDIT -eq 0 ] && [ $PLAY -eq 0 ]
