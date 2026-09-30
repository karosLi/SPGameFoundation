#!/usr/bin/env bash
# Executed inside unityci/editor. Activates the licence, runs both test platforms, returns a serial licence.
set -uo pipefail
cd /project
A=Artifacts
mkdir -p "$A"

mkdir -p ~/.cache/unity3d ~/.local/share/unity3d/Unity
if [ -n "${UNITY_LICENSE:-}" ]; then
  LIC=~/.local/share/unity3d/Unity/Unity_lic.ulf
  # Secrets pasted in a browser get CRLF line endings; the signed .ulf must be byte-exact (GameCI does the same).
  echo "$UNITY_LICENSE" | tr -d '\r' > "$LIC"
  echo "Licence file: $(wc -c < "$LIC") bytes, $(grep -c . "$LIC") lines"
  # The licensing client only accepts a .ulf that is imported explicitly.
  unity-editor -batchmode -nographics -quit -manualLicenseFile "$LIC" -logFile "$A/activation.log"
  CODE=$?
  echo "Licence: .ulf import exit $CODE"
  grep -iE "licen[cs]e|entitlement|error" "$A/activation.log" | tail -n 25 || true
  if [ $CODE -ne 0 ] && [ -n "${UNITY_EMAIL:-}" ]; then
    echo "Retrying with account sign-in"
    unity-editor -batchmode -nographics -quit -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -logFile "$A/activation-login.log"
    echo "Licence: sign-in exit $?"
    grep -iE "licen[cs]e|entitlement|error" "$A/activation-login.log" | tail -n 25 || true
  fi
elif [ -n "${UNITY_SERIAL:-}" ]; then
  unity-editor -batchmode -nographics -quit -serial "$UNITY_SERIAL" -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -logFile "$A/activation.log"
  echo "Licence: serial activation exit $?"
else
  echo "::error::No UNITY_LICENSE or UNITY_SERIAL provided"; exit 1
fi

run() {
  local platform=$1; shift
  echo "::group::${platform} tests"
  unity-editor -batchmode "$@" -projectPath /project -runTests -testPlatform "$platform" \
    -testResults "$A/${platform}-results.xml" -logFile "$A/${platform}.log"
  local code=$?
  echo "exit code ${code} (0 = passed, 2 = test failures, other = editor error)"
  echo "::endgroup::"
  if [ $code -ne 0 ]; then
    echo "----- last 150 lines of ${platform}.log -----"
    tail -n 150 "$A/${platform}.log" || true
  fi
  return $code
}

run editmode -nographics; EDIT=$?
# PlayMode with a (virtual) display so rendering and screenshots work; the image's wrapper provides xvfb.
run playmode; PLAY=$?

if [ -n "${UNITY_SERIAL:-}" ]; then
  unity-editor -batchmode -nographics -quit -returnlicense -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -logFile "$A/return.log" || true
fi
chown -R "${HOST_UID:-0}:${HOST_GID:-0}" /project/Artifacts /project/Library 2>/dev/null || true
[ $EDIT -eq 0 ] && [ $PLAY -eq 0 ]
