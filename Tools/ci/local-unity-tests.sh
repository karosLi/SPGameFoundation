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
A=Artifacts; rm -rf "$A"; mkdir -p "$A"
PROJECT="$(pwd)"

# Prints why the OS killed the editor (memory pressure, code signing, ...) on macOS.
diagnose_kill() {
  [ "$(uname)" = Darwin ] || return 0
  echo "----- macOS kernel/system log mentioning Unity (last 3 min) -----"
  log show --last 3m --style compact --predicate \
    '(eventMessage CONTAINS[c] "unity") AND (eventMessage CONTAINS[c] "kill" OR eventMessage CONTAINS[c] "jetsam" OR eventMessage CONTAINS[c] "memorystatus" OR eventMessage CONTAINS[c] "codesign" OR eventMessage CONTAINS[c] "code sign" OR eventMessage CONTAINS[c] "signature")' \
    2>/dev/null | tail -n 40 || true
  local ips
  ips=$(ls -t "$HOME"/Library/Logs/DiagnosticReports/Unity*.ips 2>/dev/null | head -n 1)
  if [ -n "$ips" ] && [ $(( $(date +%s) - $(stat -f %m "$ips") )) -lt 600 ]; then
    echo "----- $ips (termination info) -----"
    grep -iE '"(termination|exception|signal|indicator|codes|namespace|type)"|reason' "$ips" | head -n 30 || true
  fi
}

run() {
  local platform=$1 tag=$2; shift 2
  echo "::group::${platform} tests${tag:+ ($tag)}"
  "$EDITOR" -batchmode "$@" -projectPath "$PROJECT" -runTests -testPlatform "$platform" \
    -testResults "$PROJECT/$A/${platform}${tag:+-$tag}-results.xml" -logFile "$PROJECT/$A/${platform}${tag:+-$tag}.log" &
  local pid=$! peak=0 rss
  # Sample the editor's resident memory so a runaway allocation is visible even if the OS kills it.
  while kill -0 $pid 2>/dev/null; do
    rss=$(ps -o rss= -p $pid 2>/dev/null | tr -d ' ')
    if [ -n "$rss" ] && [ "$rss" -gt "$peak" ]; then peak=$rss; fi
    sleep 1
  done
  wait $pid; local code=$?
  echo "exit code ${code} (0 = passed, 2 = test failures, other = editor error); peak RSS $((peak / 1024)) MB"
  echo "::endgroup::"
  if [ $code -ne 0 ]; then
    local log="$A/${platform}${tag:+-$tag}.log"
    echo "----- test progress in ${log} -----"; grep -F '[SPF-TEST]' "$log" | tail -n 15 || true
    echo "----- errors/exceptions in ${log} -----"
    grep -nE 'error CS|Exception|Crash|Received signal|Stacktrace|Assertion failed|Failed' "$log" | grep -v 'Failed to read NSDictionary' | head -n 60 || true
    echo "----- last 150 lines of ${log} -----"; tail -n 150 "$log" || true
    [ $code -ge 128 ] && diagnose_kill
  fi
  return $code
}

# A run killed by a signal (137 = SIGKILL) is retried once with Burst disabled to tell a Burst/native
# code problem apart from a managed-code one.
run_platform() {
  local platform=$1; shift
  run "$platform" "" "$@"; local code=$?
  if [ $code -ge 128 ]; then
    echo "::warning::${platform} editor was killed (exit ${code}); retrying with Burst disabled for diagnosis"
    run "$platform" noburst "$@" --burst-disable-compilation || true
  fi
  return $code
}

run_platform editmode -nographics; EDIT=$?
run_platform playmode; PLAY=$?
[ $EDIT -eq 0 ] && [ $PLAY -eq 0 ]
