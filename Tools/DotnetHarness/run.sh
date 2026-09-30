#!/usr/bin/env bash
# Builds every assembly against Unity stubs with Unity's assembly boundaries and runs the EditMode tests.
set -euo pipefail
cd "$(dirname "$0")"
./fetch-deps.sh
python3 generate.py
dotnet build .gen/Harness.proj -nologo -v q -warnaserror:CS0246,CS0234
dotnet test .gen/Harness.proj -nologo -v q --no-build "$@"

# Optional headless preview: PREVIEW=1 Tools/DotnetHarness/run.sh  → Docs/preview/frame*.png (needs Pillow)
if [ "${PREVIEW:-0}" = "1" ]; then
  out="$(mktemp -d)"
  SPF_PREVIEW_OUT="$out" dotnet test .gen/SnakeFoundation.Tests.EditMode/SnakeFoundation.Tests.EditMode.csproj --no-build -nologo -v q --filter "Name=ExportFrames"
  python3 preview.py "$out" ../../Docs/preview
fi
