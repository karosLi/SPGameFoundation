#!/usr/bin/env python3
"""
Generates one .NET project per Unity assembly definition (asmdef + asmref) so the harness enforces
exactly the same assembly boundaries and references as Unity. Unity engine/package APIs resolve to
UnityStubs; Unity.Mathematics compiles from its real sources (see fetch-deps.sh).
"""
import json, os, pathlib, re, sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent.parent
ASSETS = ROOT / "Assets"
GEN = HERE / ".gen"
MATH_SRC = HERE / ".deps/Unity.Mathematics/src/Unity.Mathematics"

STUB_REFS = {
    "Unity.Collections", "Unity.Burst", "Unity.Jobs", "UnityEngine.TestRunner", "UnityEditor.TestRunner",
    "Unity.RenderPipelines.Universal.Runtime", "Unity.RenderPipelines.Core.Runtime", "UnityEngine.UI",
    "Unity.PerformanceTesting", "Unity.Profiling.Core",
}
DEFINES = "SPF_DOTNET_HARNESS;UNITY_2022_3_OR_NEWER;ENABLE_LEGACY_INPUT_MANAGER;UNITY_INCLUDE_TESTS"
NOWARN = "CS0649;CS0660;CS0661;CS8632;CS8981;CS0414;CS1591;CS0618;CS0162;CS1587;CS0169"


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    owners = {}      # folder -> assembly name
    asmdefs = {}     # name -> (folder, data)
    for p in ASSETS.rglob("*.asmdef"):
        data = load_json(p)
        asmdefs[data["name"]] = (p.parent, data)
        owners[p.parent] = data["name"]
    for p in ASSETS.rglob("*.asmref"):
        owners[p.parent] = load_json(p)["reference"]

    files = {name: [] for name in asmdefs}
    for cs in ASSETS.rglob("*.cs"):
        folder = cs.parent
        while folder not in owners and folder != ASSETS.parent:
            folder = folder.parent
        name = owners.get(folder)
        if name is None:
            print(f"warning: {cs} belongs to no asmdef (Assembly-CSharp); skipped")
            continue
        files[name].append(cs)

    GEN.mkdir(exist_ok=True)
    projects = []

    def write(name, body):
        d = GEN / name
        d.mkdir(exist_ok=True)
        (d / f"{name}.csproj").write_text(body)
        projects.append(d / f"{name}.csproj")

    write("Unity.Mathematics", f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>{NOWARN}</NoWarn></PropertyGroup>
  <ItemGroup>
    <Compile Include="{MATH_SRC}/**/*.cs" />
    <ProjectReference Include="../UnityStubs/UnityStubs.csproj" />
  </ItemGroup>
</Project>
""")
    write("UnityStubs", f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>{NOWARN}</NoWarn>
  <DefineConstants>{DEFINES}</DefineConstants></PropertyGroup>
  <ItemGroup>
    <Compile Include="{HERE}/UnityStubs*.cs" />
    <PackageReference Include="NUnit" Version="3.13.3" />
  </ItemGroup>
</Project>
""")

    for name, (folder, data) in sorted(asmdefs.items()):
        refs = {"UnityStubs"}
        for r in data.get("references", []):
            if r.startswith("GUID:"):
                sys.exit(f"{name}: GUID references are not supported by the harness; use names")
            if r in STUB_REFS:
                continue
            if r == "Unity.Mathematics" or r in asmdefs:
                refs.add(r)
            else:
                sys.exit(f"{name}: unknown reference {r} (add to STUB_REFS if it is a Unity package)")
        is_test = "nunit.framework.dll" in data.get("precompiledReferences", [])
        editor_only = data.get("includePlatforms") == ["Editor"]
        defines = DEFINES + (";UNITY_EDITOR" if editor_only else "")
        items = "\n".join(f'    <Compile Include="{f}" />' for f in sorted(files[name]))
        prefs = "\n".join(f'    <ProjectReference Include="../{r}/{r}.csproj" />' for r in sorted(refs))
        test = """
    <PackageReference Include="NUnit" Version="3.13.3" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />""" if is_test else ""
        write(name, f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <AssemblyName>{name}</AssemblyName>
    <AllowUnsafeBlocks>{str(bool(data.get("allowUnsafeCode"))).lower()}</AllowUnsafeBlocks>
    <Nullable>disable</Nullable>
    <IsTestProject>{str(is_test).lower()}</IsTestProject>
    <!-- Unity asmdef references are not transitive: every assembly must name what it uses. -->
    <DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>
    <DefineConstants>{defines}</DefineConstants>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>{NOWARN}</NoWarn>
  </PropertyGroup>
  <ItemGroup>
{items}
{prefs}{test}
  </ItemGroup>
</Project>
""")

    sln = GEN / "Harness.proj"
    sln.write_text("""<Project Sdk="Microsoft.Build.Traversal/4.1.0">
  <ItemGroup>
""" + "\n".join(f'    <ProjectReference Include="{p.relative_to(GEN)}" />' for p in projects) + """
  </ItemGroup>
</Project>
""")
    print(f"generated {len(projects)} projects in {GEN}")


if __name__ == "__main__":
    main()
