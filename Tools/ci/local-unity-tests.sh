#!/usr/bin/env bash
# Runs EditMode + PlayMode tests with a locally installed Unity (self-hosted runner or a developer machine).
# The machine's own Unity Hub sign-in provides the licence (works with Personal).
# Editor path: $UNITY_EDITOR_PATH, else the default Unity Hub install location for ProjectVersion.txt.
set -uo pipefail
cd "$(dirname "$0")/../.."
# Keep intrusive capture out of the full gate, even if the runner inherited this variable.
# This is an exact, closed mode rather than a general-purpose test-filter override.
MODE=full
if [ "$#" -gt 1 ]; then
  echo "::error::Usage: $0 [--shooter-gc-diagnostic|--shooter-platform-control]"; exit 2
fi
case "$#:${1:-}" in
  0:) export SPF_SHOOTER_GC_CAPTURE=0 SPF_SHOOTER_PLATFORM_CONTROL=0 ;;
  1:--shooter-gc-diagnostic)
    MODE=shooter-gc-diagnostic
    export SPF_SHOOTER_GC_CAPTURE=1
    export SPF_SHOOTER_PLATFORM_CONTROL=0
    export SPF_STORY_GC_CAPTURE=0
    export SPF_WEAPON_GAMEPLAY_SEQUENCE=0 SPF_ABILITY_GAMEPLAY_SEQUENCE=0
    ;;
  1:--shooter-platform-control)
    MODE=shooter-platform-control
    export SPF_SHOOTER_PLATFORM_CONTROL=1 SPF_SHOOTER_GC_CAPTURE=0 SPF_SHOOTER_GC_INCLUDE_EDITOR=0
    export SPF_STORY_GC_CAPTURE=0 SPF_WEAPON_GAMEPLAY_SEQUENCE=0 SPF_ABILITY_GAMEPLAY_SEQUENCE=0
    ;;
  *) echo "::error::Usage: $0 [--shooter-gc-diagnostic|--shooter-platform-control]"; exit 2 ;;
esac
A=Artifacts; rm -rf "$A"; mkdir -p "$A"
if [ "$MODE" = shooter-gc-diagnostic ]; then
  {
    printf 'purpose=intrusive allocation attribution; NOT full native gate\n'
    printf 'head=%s\n' "$(git rev-parse HEAD 2>/dev/null || printf unknown)"
    printf 'github_sha=%s\nref=%s\nrun_id=%s\nrun_attempt=%s\n' "${GITHUB_SHA:-local}" "${GITHUB_REF:-local}" "${GITHUB_RUN_ID:-local}" "${GITHUB_RUN_ATTEMPT:-local}"
    printf 'started_utc=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } > "$A/shooter-diagnostic-run.txt"
fi
if [ "$MODE" = shooter-platform-control ]; then
  {
    printf 'purpose=ordinary-counter A/B calibration/platform control; NOT product gate\n'
    printf 'head=%s\ngithub_sha=%s\nref=%s\nrun_id=%s\nrun_attempt=%s\n' "$(git rev-parse HEAD 2>/dev/null || printf unknown)" "${GITHUB_SHA:-local}" "${GITHUB_REF:-local}" "${GITHUB_RUN_ID:-local}" "${GITHUB_RUN_ATTEMPT:-local}"
    printf 'started_utc=%s\neditor_invocations=1\nretry=false\nburst_disabled_fallback=false\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf 'conditions=A-raw-recorder,B-frame-governor\nwarmup_per_condition=150\nobservations_per_condition=180\ncontrol_frames_per_condition=24\nwarmup_observation_control_frames=708\nteardown_yields=2\n'
    printf 'rss_stop_mb=4096\nrss_sampling_seconds=1\nprocess_timeout_seconds=600\nprofiler_settings_changed=false\n'
  } > "$A/shooter-platform-control-run.txt"
fi
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
    if [ "$MODE" != full ]; then
      echo "::error::Shooter diagnostic requires the arm64 Unity editor on Apple Silicon for native Burst execution"; exit 1
    fi
    echo "::warning::Unity at $EDITOR has no arm64 slice; install the Apple Silicon editor for Burst to work"
  fi
fi
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
  local pid=$! peak=0 rss started=$SECONDS stop_reason=""
  if [ "$MODE" = shooter-platform-control ]; then
    printf 'elapsedSeconds,rssKiB\n' > "$A/platform-control-resident-samples.csv"
  fi
  # Sample the editor's resident memory so a runaway allocation is visible even if the OS kills it.
  while kill -0 $pid 2>/dev/null; do
    rss=$(ps -o rss= -p $pid 2>/dev/null | tr -d ' ')
    case "$rss" in ''|*[!0-9]*) rss="" ;; esac
    if [ -n "$rss" ] && [ "$rss" -gt "$peak" ]; then peak=$rss; fi
    if [ "$MODE" = shooter-platform-control ]; then
      if ! kill -0 "$pid" 2>/dev/null; then break; fi
      printf '%s,%s\n' "$((SECONDS - started))" "${rss:--1}" >> "$A/platform-control-resident-samples.csv"
      if [ -z "$rss" ]; then stop_reason="RSS unavailable";
      elif [ "$rss" -gt 4194304 ]; then stop_reason="RSS exceeded 4096 MiB stop threshold";
      elif [ "$((SECONDS - started))" -ge 600 ]; then stop_reason="600 second process timeout"; fi
      if [ -n "$stop_reason" ]; then
        printf 'monitor_stop=%s\n' "$stop_reason" >> "$A/shooter-platform-control-run.txt"
        echo "::error::Platform control stopped: $stop_reason; no retry and no valid measurement claim."
        kill "$pid" 2>/dev/null || true
        sleep 2
        kill -9 "$pid" 2>/dev/null || true
        break
      fi
    fi
    sleep 1
  done
  wait $pid; local code=$?
  if [ -n "$stop_reason" ]; then code=124; fi
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

if [ "$MODE" = shooter-platform-control ]; then
  FILTER=ShooterFoundation.Tests.PlayMode.ShooterPlatformControlTests.MatchedEmptyPlatformWindows
  printf 'unity_version=%s\neditor=%s\nhost=%s\ntest_filter=%s\n' "$VERSION" "$EDITOR" "$(uname -sm)" "$FILTER" >> "$A/shooter-platform-control-run.txt"
  kill_leftover_editors
  clear_burst_cache
  run playmode shooter-platform-control -testFilter "$FILTER"; DIAGNOSTIC=$?
  printf 'editor_exit_code=%s\n' "$DIAGNOSTIC" >> "$A/shooter-platform-control-run.txt"
  python3 - "$A/playmode-shooter-platform-control-results.xml" "$FILTER" "$A/GC/platform-control" <<'PY'
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
try:
    cases = ET.parse(sys.argv[1]).getroot().findall(".//test-case")
    if len(cases) != 1 or cases[0].get("fullname") != sys.argv[2] or cases[0].get("result") not in ("Passed", "Failed"):
        raise ValueError("expected precisely the one executed A/B platform-control test")
    for name in ("A-raw-recorder", "B-frame-governor"):
        report = json.loads((Path(sys.argv[3]) / name / "summary.json").read_text())
        if report.get("condition") != name or report.get("observationCount") != 204 or report.get("windowFrames") != 180:
            raise ValueError("incomplete or wrong condition: " + name)
        if report.get("establishesProductGate") is not False or report.get("measurementValid") is not True:
            raise ValueError("invalid calibration/sampling: " + name)
    print("Both fixed platform conditions complete and calibrated; this does not establish the Shooter product gate.")
    if cases[0].get("result") == "Failed": sys.exit(2)
except (OSError, ValueError, ET.ParseError) as error:
    print("::error::Incomplete platform control: " + str(error)); sys.exit(1)
PY
  RESULTS=$?
  printf 'result_scope_validation_exit_code=%s\n' "$RESULTS" >> "$A/shooter-platform-control-run.txt"
  [ "$DIAGNOSTIC" -ne 0 ] || DIAGNOSTIC=$RESULTS
  exit "$DIAGNOSTIC"
fi

if [ "$MODE" = shooter-gc-diagnostic ]; then
  FILTER=ShooterFoundation.Tests.PlayMode.ShooterPlayTests.WarmSteadyFrameAndPresentationOnlyQuality
  echo "::notice::INTRUSIVE SHOOTER DIAGNOSTIC ONLY; NOT THE FULL NATIVE GATE"
  {
    printf 'unity_version=%s\neditor=%s\nhost=%s\n' "$VERSION" "$EDITOR" "$(uname -sm)"
    printf 'test_filter=%s\ntiers=GpuDriven,DataTexture\n' "$FILTER"
    printf 'SPF_SHOOTER_GC_CAPTURE=1\nmeasured_frames=180\nmax_allocating_frames=2\n'
    printf 'editor_invocations=1\nburst_disabled_fallback=false\nretry=false\n'
    printf 'burst_actual_state=see per-tier capture settings and precondition\n'
  } >> "$A/shooter-diagnostic-run.txt"
  kill_leftover_editors
  clear_burst_cache
  # Deliberately bypass run_platform: no retry and no Burst-disabled fallback.
  # The two parameterized tiers retain the unchanged window and append marker controls.
  run playmode shooter-gc-diagnostic -testFilter "$FILTER"; DIAGNOSTIC=$?
  printf 'editor_exit_code=%s\n' "$DIAGNOSTIC" >> "$A/shooter-diagnostic-run.txt"
  # A successful editor exit without both requested, executed cases is incomplete evidence.
  python3 - "$A/playmode-shooter-gc-diagnostic-results.xml" "$FILTER" <<'PY'
import sys
import xml.etree.ElementTree as ET

try:
    cases = ET.parse(sys.argv[1]).getroot().findall(".//test-case")
    expected = {sys.argv[2] + "(" + tier + ")" for tier in ("GpuDriven", "DataTexture")}
    if len(cases) != 2 or {case.get("fullname") for case in cases} != expected:
        raise ValueError("expected exactly the GpuDriven and DataTexture steady-window cases")
    if any(case.get("result") not in ("Passed", "Failed") for case in cases):
        raise ValueError("both diagnostic tiers must execute; skipped/inconclusive cases are incomplete")
    print("Both requested Shooter diagnostic tiers executed; this is not the full native gate.")
    if any(case.get("result") == "Failed" for case in cases):
        sys.exit(2)
except (OSError, ValueError, ET.ParseError) as error:
    print("::error::Incomplete Shooter diagnostic results: " + str(error))
    sys.exit(1)
PY
  RESULTS=$?
  printf 'result_scope_validation_exit_code=%s\n' "$RESULTS" >> "$A/shooter-diagnostic-run.txt"
  [ "$DIAGNOSTIC" -ne 0 ] || DIAGNOSTIC=$RESULTS
  exit "$DIAGNOSTIC"
fi

run_platform editmode -nographics; EDIT=$?
for f in "$A"/perf-*.txt; do [ -f "$f" ] && { echo "::group::performance report $(basename "$f")"; cat "$f"; echo "::endgroup::"; }; done
run_platform playmode; PLAY=$?
for f in "$A"/perf-render-*.txt; do [ -f "$f" ] && { echo "::group::performance report $(basename "$f")"; cat "$f"; echo "::endgroup::"; }; done
[ $EDIT -eq 0 ] && [ $PLAY -eq 0 ]
