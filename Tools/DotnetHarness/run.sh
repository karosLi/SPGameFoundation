#!/usr/bin/env bash
# Builds every assembly against Unity stubs with Unity's assembly boundaries and runs the EditMode tests.
set -euo pipefail
cd "$(dirname "$0")"
./fetch-deps.sh
python3 generate.py
dotnet build .gen/Harness.proj -nologo -v q -warnaserror:CS0246,CS0234
dotnet test .gen/Harness.proj -nologo -v q --no-build "$@"
