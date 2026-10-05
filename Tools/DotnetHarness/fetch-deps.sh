#!/usr/bin/env bash
# Fetches the pure-C# sources the harness compiles alongside the stubs (not vendored in the repo).
set -euo pipefail
cd "$(dirname "$0")"
MATH_COMMIT=f110c8c230d253654afed153569030a587cc7557   # Unity.Mathematics 1.2.6 (the version in Packages/manifest.json)
if [ ! -d .deps/Unity.Mathematics ]; then
  mkdir -p .deps
  git clone -q https://github.com/Unity-Technologies/Unity.Mathematics.git .deps/Unity.Mathematics
fi
git -C .deps/Unity.Mathematics checkout -q "$MATH_COMMIT"
