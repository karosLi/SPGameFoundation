#!/usr/bin/env python3
"""Generate a compile-only project against installed real Unity 2022.3/package DLLs.

Run Tools/DotnetHarness/generate.py first. The normal harness separately enforces
asmdef boundaries; this project checks all production sources plus rollback tests
against actual APIs. It does not execute Unity or replace native CI.
"""
from pathlib import Path
import argparse
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--repo', type=Path, required=True)
parser.add_argument('--unity-data', type=Path, required=True)
parser.add_argument('--nunit', type=Path, required=True)
parser.add_argument('--out', type=Path, required=True)
parser.add_argument('--all-assets', action='store_true',
                    help='Compile every Assets C# file, including all native test fixtures.')
parser.add_argument('--target-framework', choices=('net8.0', 'netstandard2.1'), default='net8.0',
                    help='Use netstandard2.1 to additionally check the Unity-era BCL surface.')
args = parser.parse_args()
repo, unity, out = args.repo.resolve(), args.unity_data.resolve(), args.out.resolve()
out.mkdir(parents=True, exist_ok=True)
sources = set()
for project in (repo / 'Tools/DotnetHarness/.gen').glob('*/*.csproj'):
    if '.Tests.' in project.stem or project.stem in ('UnityStubs', 'Unity.Mathematics'):
        continue
    sources.update(x.attrib['Include'] for x in ET.parse(project).findall('.//Compile'))
for test in (
    'Assets/SinglePlayerFoundation/Tests/EditMode/CompositionRollbackTests.cs',
    'Assets/SnakeFoundation/Tests/EditMode/SystemCreationRollbackTests.cs',
):
    sources.add(str(repo / test))
if args.all_assets:
    sources = {str(path) for path in (repo / 'Assets').rglob('*.cs')}
refs = list((unity / 'Managed/UnityEngine').glob('*.dll'))
# Use modular Editor assemblies; adding the monolithic UnityEditor.dll alongside
# these duplicates EditorWindow/MenuItem definitions in this compile-only setup.
refs += list((unity / 'Managed').glob('UnityEditor.*.dll'))
cache = unity / 'Resources/PackageManager/ProjectTemplates/libcache/com.unity.template.2d-7.0.4/ScriptAssemblies'
for name in ('Unity.Mathematics', 'Unity.Collections', 'Unity.Collections.LowLevel.ILSupport',
             'Unity.Burst', 'Unity.Jobs', 'UnityEngine.UI', 'UnityEngine.TestRunner', 'UnityEditor.TestRunner'):
    dll = cache / (name + '.dll')
    if dll.exists():
        refs.append(dll)
refs.append(args.nunit.resolve())
for path in list(map(Path, sources)) + refs:
    if not path.is_file():
        raise FileNotFoundError(path)
project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
props = ET.SubElement(project, 'PropertyGroup')
for key, value in {
    'TargetFramework': args.target_framework, 'LangVersion': '9.0', 'EnableDefaultCompileItems': 'false',
    'AllowUnsafeBlocks': 'true', 'NuGetAudit': 'false',
    'DefineConstants': 'UNITY_2022_3_OR_NEWER;ENABLE_LEGACY_INPUT_MANAGER;UNITY_INCLUDE_TESTS;UNITY_EDITOR',
}.items():
    ET.SubElement(props, key).text = value
items = ET.SubElement(project, 'ItemGroup')
for source in sorted(sources):
    ET.SubElement(items, 'Compile', Include=source)
for dll in refs:
    ET.SubElement(ET.SubElement(items, 'Reference', Include=dll.stem), 'HintPath').text = str(dll)
ET.ElementTree(project).write(out / 'Compile.csproj')
print(f'{len(sources)} sources / {len(refs)} real API references: {out / "Compile.csproj"}')
