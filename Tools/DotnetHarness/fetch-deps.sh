#!/usr/bin/env bash
# Fetches the pure-C# sources the harness compiles alongside the stubs (not vendored in the repo).
set -euo pipefail
cd "$(dirname "$0")"
MATH_COMMIT=f53664de3dad88869a91ef8ebdbecf356e1312d5   # Unity.Mathematics 1.3.2
if [ ! -d .deps/Unity.Mathematics ]; then
  mkdir -p .deps
  git clone -q https://github.com/Unity-Technologies/Unity.Mathematics.git .deps/Unity.Mathematics
fi
git -C .deps/Unity.Mathematics checkout -q "$MATH_COMMIT"
