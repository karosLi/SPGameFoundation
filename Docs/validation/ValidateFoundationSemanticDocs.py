#!/usr/bin/env python3
"""Check the two Stage A docs and extract their C# examples for a harness-only build.

Run from any directory after Tools/DotnetHarness/generate.py. This script does not
modify Assets, call Unity, install dependencies, or claim native/device coverage.
--integration-root can resolve links added by a parallel documentation commit.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOCS = [ROOT / 'Docs/Architecture.md', ROOT / 'Docs/NewGameplayIntegrationRecipe.md']


def normalize(text):
    text = re.sub(r'//[^\n]*', '', text)
    return re.sub(r'\s+', '', text)


def heading_slug(text):
    return re.sub(r'[^\w\- ]', '', text.lower()).replace(' ', '-')


def extract_interface(source, name):
    start = source.index('public interface ' + name)
    opening = source.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def prepare_snippets(output, architecture, recipe):
    output.mkdir(parents=True, exist_ok=True)
    (output / 'Architecture.ExampleKeys.cs').write_text(architecture[2])
    imports = '\n'.join('using ' + name + ';' for name in [
        'SPF.Contracts', 'SPF.Runtime.World', 'SPF.Runtime.Scheduling',
        'SPF.Runtime.Composition', 'Unity.Jobs'])
    # A separate namespace avoids redefining production interfaces. Their signatures
    # are also compared against the actual source by the checks below.
    (output / 'Architecture.Interfaces.cs').write_text(imports + '\nnamespace DocumentationDeclarations {\n' + '\n'.join(architecture[:2]) + '\n}')
    imports = '\n'.join('using ' + name + ';' for name in [
        'SPF.Contracts', 'SPF.Runtime.Composition', 'SPF.Runtime.Session',
        'SPF.Samples.DriftSmoke', 'SPF.L2.Skills', 'SurvivorFoundation',
        'SurvivorFoundation.Game', 'UnityEngine'])
    methods = '\n'.join('public static void ' + name + '() {\n' + code + '\n}'
                        for name, code in [('DriftSmoke', recipe[0]), ('Skills', recipe[2]),
                                           ('FlyingSwordCompileOnly', recipe[4])])
    (output / 'Recipe.Snippets.cs').write_text(imports + '\npublic static class RecipeExamples {\n' + methods + '\n}')
    (output / 'Program.cs').write_text('''using System;
using SPF.Runtime.World;
public static class Program {
 public static void Main() {
   RecipeExamples.DriftSmoke();
   RecipeExamples.Skills();
   var layout = new WorldLayout();
   ExampleKeys.Declare(layout);
   using var world = new SimWorld(layout, 1);
   if (world.Table(ExampleKeys.Actor).Capacity != 128 || !world.Table(ExampleKeys.Actor).HasColumn(ExampleKeys.Streak))
       throw new Exception("Extension columns not composed.");
   Console.WriteLine("PASS: DriftSmoke 60 ticks, SkillSlots construction, ExampleKeys table/extension composition");
 }
}
''')
    projects = ['SPF.Contracts', 'SPF.Runtime.Core', 'SPF.Runtime', 'SPF.L2Gameplay',
                'SPF.Samples.DriftSmoke', 'SurvivorFoundation.Runtime',
                'SurvivorFoundation.Game', 'UnityStubs', 'Unity.Mathematics']
    refs = '\n'.join(f'<ProjectReference Include="{ROOT}/Tools/DotnetHarness/.gen/{name}/{name}.csproj" />' for name in projects)
    (output / 'DocExamples.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>9.0</LangVersion>
<Nullable>disable</Nullable><DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>
<ConcurrentGarbageCollection>false</ConcurrentGarbageCollection></PropertyGroup><ItemGroup>
''' + refs + '\n</ItemGroup></Project>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--snippet-output', type=Path, required=True)
    parser.add_argument('--report-output', type=Path, required=True)
    parser.add_argument('--integration-root', type=Path, action='append', default=[])
    args = parser.parse_args()
    errors, links, integrated = [], [], []
    table_rows = 0
    docs = [doc.read_text() for doc in DOCS]
    for doc, text in zip(DOCS, docs):
        if text.count('```') % 2:
            errors.append('Unbalanced code fences: ' + str(doc.relative_to(ROOT)))
        width = None
        for line in text.splitlines():
            if line.startswith('|'):
                cells = line.count('|')
                if width is not None and cells != width:
                    errors.append('Unequal Markdown table columns: ' + str(doc.relative_to(ROOT)) + ': ' + line)
                width = cells
                table_rows += 1
            else:
                width = None
        for raw in re.findall(r'\[[^\]]*\]\(([^)]+)\)', text):
            if re.match(r'^[a-z]+:', raw):
                continue
            path, _, anchor = raw.partition('#')
            target = (doc.parent / path).resolve() if path else doc
            relative = target.relative_to(ROOT)
            if not target.exists():
                for integration in args.integration_root:
                    alternative = integration.resolve() / relative
                    if alternative.exists():
                        target = alternative
                        integrated.append(str(relative))
                        break
            links.append({'document': str(doc.relative_to(ROOT)), 'target': raw})
            if not target.exists():
                errors.append('Missing relative link: ' + str(relative))
            elif anchor and target.suffix == '.md':
                headings = re.findall(r'^#+\s+(.+)$', target.read_text(), re.M)
                if anchor not in {heading_slug(heading) for heading in headings}:
                    errors.append('Missing heading: ' + raw)
    architecture, recipe = [re.findall(r'```csharp\n(.*?)\n```', text, re.S) for text in docs]
    assert (len(architecture), len(recipe)) == (3, 5)
    for snippet, path, name in [
        (architecture[0], 'Assets/SinglePlayerFoundation/Runtime/Scheduling/ISimSystem.cs', 'ISimSystem'),
        (architecture[1], 'Assets/SinglePlayerFoundation/Runtime/Composition/IGameplayModule.cs', 'IGameplayModule')]:
        if normalize(snippet) != normalize(extract_interface((ROOT / path).read_text(), name)):
            errors.append('Interface signature drift: ' + name)
    drift = (ROOT / 'Assets/SinglePlayerFoundation/Samples/DriftSmoke/DriftSmokeModule.cs').read_text()
    if normalize(recipe[1]) not in normalize(drift):
        errors.append('DriftSmoke contextual excerpt differs from source')
    hud = (ROOT / 'Assets/BrawlerFoundation/Game/BwGameBootstrap.cs').read_text()
    hud = hud.replace('Joystick = MobileHud.Joystick;', '')  # Deliberately omitted local assignment.
    if normalize(recipe[3]) not in normalize(hud):
        errors.append('Brawler HUD contextual excerpt differs from source')
    sources = [(path, path.read_text()) for path in (ROOT / 'Assets').rglob('*.cs')]
    menus = {}
    for path, source in sources:
        for menu in re.findall(r'MenuItem\("([^"]+)"', source):
            menus[menu] = str(path.relative_to(ROOT))
        for menu in re.findall(r'CreateAssetMenu\(menuName\s*=\s*"([^"]+)"', source):
            menus['Assets/Create/' + menu] = str(path.relative_to(ROOT))
    # Every full arrow menu shown in the recipe is checked against an attribute.
    documented_menus = []
    for bold in re.findall(r'\*\*([^*]+)\*\*', docs[1]):
        if '→' not in bold:
            continue
        menu = '/'.join(part.strip() for part in bold.split('→'))
        if menu.startswith(('SPF/', 'Assets/Create/')):
            documented_menus.append(menu)
            if menu not in menus:
                errors.append('Missing menu: ' + menu)
    prepare_snippets(args.snippet_output.resolve(), architecture, recipe)
    report = {
        'documents': {str(doc.relative_to(ROOT)): hashlib.sha256(doc.read_bytes()).hexdigest() for doc in DOCS},
        'relative_link_occurrences': len(links),
        'markdown_table_rows_checked': table_rows,
        'unique_relative_targets': len({item['target'] for item in links}),
        'integration_targets': sorted(set(integrated)),
        'menu_paths': {menu: menus.get(menu) for menu in documented_menus},
        'source_signature_and_contextual_excerpt_checks': 4,
        'csharp_blocks': {'architecture': len(architecture), 'recipe': len(recipe)},
        'generated_project': str(args.snippet_output / 'DocExamples.csproj'),
        'errors': errors,
        'scope': 'Static source/link/menu checks and snippet generation only. Build and execution results are recorded separately.'
    }
    args.report_output.parent.mkdir(parents=True, exist_ok=True)
    args.report_output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
