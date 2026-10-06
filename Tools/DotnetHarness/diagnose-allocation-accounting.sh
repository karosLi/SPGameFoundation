#!/usr/bin/env bash
# Linux-only controlled reproducer. Does not modify any production runtime settings.
set -euo pipefail
cd "$(dirname "$0")"
if [ "$(uname -s)" != Linux ]; then
  echo 'This diagnostic uses Linux gettid for allocation-event attribution.' >&2
  exit 2
fi
./fetch-deps.sh
python3 generate.py
configuration="${CONFIGURATION:-Debug}"
dotnet build .gen/SPF.Tests.EditMode/SPF.Tests.EditMode.csproj -c "$configuration" -m:1 -p:BuildInParallel=false -p:NuGetAudit=false -nologo -v q
mkdir -p .gen/AllocationDiagnostics
project="$PWD/.gen/AllocationDiagnostics/AllocationDiagnostics.csproj"
{
  echo '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>'
  echo "<Compile Include=\"$PWD/AllocationDiagnostics.cs\" />"
  for assembly in SPF.Contracts SPF.L1Simulation SPF.Presentation SPF.Testing Unity.Mathematics UnityStubs nunit.framework; do
    echo "<Reference Include=\"$assembly\"><HintPath>$PWD/.gen/SPF.Tests.EditMode/bin/$configuration/net8.0/$assembly.dll</HintPath></Reference>"
  done
  echo '</ItemGroup></Project>'
} > "$project"
dotnet build "$project" -c "$configuration" -m:1 -p:NuGetAudit=false -nologo -v q
out="${1:-$PWD/.gen/allocation-diagnostics-$configuration}"
mkdir -p "$out"
for workload in empty pose; do
  index=0
  # ABBA: background, blocking testhost, blocking testhost, background.
  for concurrent in 1 0 0 1; do
    index=$((index + 1))
    log="$out/$workload-$index-concurrent$concurrent.log"
    DOTNET_gcConcurrent="$concurrent" dotnet ".gen/AllocationDiagnostics/bin/$configuration/net8.0/AllocationDiagnostics.dll" "$workload" background > "$log"
    echo "$log: $(tail -1 "$log")"
  done
  log="$out/$workload-explicit-blocking.log"
  DOTNET_gcConcurrent=1 dotnet ".gen/AllocationDiagnostics/bin/$configuration/net8.0/AllocationDiagnostics.dll" "$workload" blocking > "$log"
  echo "$log: $(tail -1 "$log")"
done
