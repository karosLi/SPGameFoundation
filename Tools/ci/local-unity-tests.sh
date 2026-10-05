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

# On Apple Silicon an x64 Actions runner runs under Rosetta and every child inherits x86_64, so Unity
# would run translated and Burst's JIT code gets the editor SIGKILLed ("Code Signature Invalid").
# Launch the editor natively as arm64 whenever the hardware and the editor binary support it.
LAUNCH=()
if [ "$(uname)" = Darwin ] && [ "$(sysctl -n hw.optional.arm64 2>/dev/null)" = 1 ]; then
  echo "Shell arch: $(uname -m) (runner under Rosetta if x86_64); editor archs: $(lipo -archs "$EDITOR" 2>/dev/null)"
  if lipo -archs "$EDITOR" 2>/dev/null | grep -qw arm64; then
    LAUNCH=(arch -arm64)
  else
    echo "::warning::Unity at $EDITOR has no arm64 slice; install the Apple Silicon editor for Burst to work"
  fi
fi
A=Artifacts; rm -rf "$A"; mkdir -p "$A"
PROJECT="$(pwd)"

# Prints why the OS killed the editor (memory pressure, code signing, ...) on macOS.
diagnose_kill() {
  [ "$(uname)" = Darwin ] || return 0
  echo "----- macOS kernel/system log mentioning Unity (last 3 min) -----"
  log show --last 3m --style compact --predicate \
    '(eventMessage CONTAINS[c] "unity") AND (eventMessage CONTAINS[c] "kill" OR eventMessage CONTAINS[c] "jetsam" OR eventMessage CONTAINS[c] "memorystatus" OR eventMessage CONTAINS[c] "codesign" OR eventMessage CONTAINS[c] "code sign" OR eventMessage CONTAINS[c] "signature")' \
    2>/dev/null | tail -n 40 || true
  # The crash report is written asynchronously; find the one for this editor's PID.
  local ips="" f i
  for i in 1 2 3 4 5 6 7 8 9 10; do
    for f in $(ls -t "$HOME"/Library/Logs/DiagnosticReports/Unity*.ips 2>/dev/null | head -n 5); do
      if grep -qE "\"pid\" ?: ?$1[,}]" "$f"; then ips=$f; break 2; fi
    done
    sleep 2
  done
  if [ -n "$ips" ]; then
    echo "----- $ips (termination info) -----"
    grep -iE '"(termination|exception)"' "$ips" | cut -c1-400 | head -n 10 || true
    # Which binary did code signing reject? Map the faulting address to a loaded image, then codesign it.
    local img
    img=$(python3 - "$ips" <<'PY' 2>/dev/null
import json, re, sys
text = open(sys.argv[1]).read()
body = json.loads(text[text.index('\n') + 1:])
m = re.search(r'at (0x[0-9a-fA-F]+)', body.get('exception', {}).get('subtype', ''))
addr = int(m.group(1), 16) if m else None
found = None
for im in body.get('usedImages', []):
    base, size = im.get('base', 0), im.get('size', 0)
    if addr is not None and base <= addr < base + size:
        found = im.get('path') or im.get('name')
print(found or '')
for im in body.get('usedImages', []):
    p = im.get('path', '')
    if 'burst' in p.lower():
        print('burst image loaded: ' + p, file=sys.stderr)
PY
)
    echo "faulting image: ${img:-<address not inside any loaded image (JIT/anonymous memory)>}"
    if [ -n "$img" ] && [ -f "$img" ]; then
      codesign -dvvv "$img" 2>&1 | head -n 12 || true
      codesign --verify --verbose=2 "$img" 2>&1 | head -n 5 || true
    fi
  fi
  echo "macOS: $(sw_vers -productVersion 2>/dev/null) ($(uname -m))"
}

# Prints name, message and stack trace of every failed test case in an NUnit results file.
print_failures() {
  [ -f "$1" ] || return 0
  perl -0777 -ne '
    while (/<test-case\b([^>]*result="Failed"[^>]*)>(.*?)<\/test-case>/gs) {
      my ($attrs, $body) = ($1, $2);
      my ($name) = $attrs =~ /fullname="([^"]*)"/;
      my ($msg) = $body =~ /<message><!\[CDATA\[(.*?)\]\]><\/message>/s;
      my ($st) = $body =~ /<stack-trace><!\[CDATA\[(.*?)\]\]><\/stack-trace>/s;
      my ($out) = $body =~ /<output><!\[CDATA\[(.*?)\]\]><\/output>/s;
      $st = join("\n", grep { defined } (split /\n/, ($st // ""))[0..11]);
      $out = join("\n", grep { defined } (split /\n/, ($out // ""))[-15..-1]);
      print "::error title=Test failed::$name\n--- FAILED: $name\n", ($msg // ""), "\n", $st, "\n";
      print "--- output (tail):\n$out\n" if $out;
    }' "$1"
}

run() {
  local platform=$1 tag=$2; shift 2
  echo "::group::${platform} tests${tag:+ ($tag)}"
  ${LAUNCH[@]+"${LAUNCH[@]}"} "$EDITOR" -batchmode "$@" -projectPath "$PROJECT" -runTests -testPlatform "$platform" \
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
    [ $code -ge 128 ] && diagnose_kill $pid
  fi
  print_failures "$PROJECT/$A/${platform}${tag:+-$tag}-results.xml"
  return $code
}

# A SIGKILLed editor can leave child processes (asset import workers, shader compilers) that keep
# the project lock; the next editor would then abort with "another Unity instance is running".
kill_leftover_editors() {
  local pids
  pids=$(pgrep -f -- "-projectPath $PROJECT" 2>/dev/null || true)
  if [ -n "$pids" ]; then
    echo "Stopping leftover Unity processes for this project: $pids"
    kill $pids 2>/dev/null || true; sleep 3
    kill -9 $pids 2>/dev/null || true; sleep 1
  fi
  rm -f Temp/UnityLockfile 2>/dev/null || true
}

# Burst's editor cache persists in Library/ between runs (checkout uses clean: false); a stale or
# badly signed cached library gets the editor SIGKILLed by macOS code signing at startup.
clear_burst_cache() {
  rm -rf Library/BurstCache Temp/Burst* 2>/dev/null || true
  local v
  v=$(grep -A1 '"com.unity.burst"' Packages/packages-lock.json 2>/dev/null | sed -n 's/.*"version": *"\([^"]*\)".*/\1/p')
  echo "Burst package: ${v:-unknown (not resolved yet)}"
}

# A run killed by a signal (137 = SIGKILL) is retried once with Burst disabled. The tests themselves
# are then judged on that run, and the kill is reported as a warning: on some macOS setups the
# editor's Burst JIT libraries fail code signing, which is an editor/OS problem, not a test failure.
run_platform() {
  local platform=$1; shift
  kill_leftover_editors
  clear_burst_cache
  run "$platform" "" "$@"; local code=$?
  if [ $code -ge 128 ]; then
    # Intermittent editor-side crashes (e.g. inside the Burst compiler itself) get one more try as-is.
    echo "::warning title=Editor crashed::${platform}: editor killed by signal $((code - 128)); retrying once with Burst"
    kill_leftover_editors
    run "$platform" "" "$@"; code=$?
  fi
  if [ $code -ge 128 ]; then
    echo "::warning title=Burst editor killed::${platform}: editor killed by signal $((code - 128)) with Burst enabled (see termination info); results below are from a Burst-disabled run"
    kill_leftover_editors
    run "$platform" noburst "$@" --burst-disable-compilation; code=$?
  fi
  return $code
}

run_platform editmode -nographics; EDIT=$?
for f in "$A"/perf-*.txt; do [ -f "$f" ] && { echo "::group::performance report $(basename "$f")"; cat "$f"; echo "::endgroup::"; }; done
run_platform playmode; PLAY=$?
for f in "$A"/perf-render-*.txt; do [ -f "$f" ] && { echo "::group::performance report $(basename "$f")"; cat "$f"; echo "::endgroup::"; }; done
[ $EDIT -eq 0 ] && [ $PLAY -eq 0 ]
