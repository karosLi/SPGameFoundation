#!/usr/bin/env python3
"""Reproduce the reviewed Lab source/asmdef closure and classify real Unity log evidence.

This does not compile C#, run Unity, infer absent response-file contents, or execute generators.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

import lab


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit(project, review_path, unity_log=None, artifact_root=None):
    review = json.loads(review_path.read_text())
    definitions = {json.loads(path.read_text())['name']: path for path in (project / 'Assets').rglob('*.asmdef')}
    lab.require(set(definitions) == set(review['assemblies']), 'Assembly set differs from the frozen review.')
    sources = sorted((project / 'Assets').rglob('*.cs'))
    observed = {path.relative_to(project).as_posix(): digest(path) for path in sources}
    lab.require(observed == review['source_sha256'], 'C# sources differ from the frozen review; review before reusing it.')
    log_lines = unity_log.read_text(errors='replace').splitlines() if unity_log else []
    launch_path = unity_log.parent / 'launch.json' if unity_log else None
    launch = json.loads(launch_path.read_text()) if launch_path and launch_path.is_file() else {}
    report = {'scope': 'Static/source review plus observed log events; not a complete native compilation pass.',
              'reviewed_source_commit': review['reviewed_source_commit'], 'source_count': len(sources),
              'source_sha256': observed, 'native_source_commit': launch.get('source_commit'),
              'unity_log_sha256': digest(unity_log) if unity_log else None, 'assemblies': {}}
    blocks = {}
    for index, line in enumerate(log_lines):
        match = re.fullmatch(r'##### Contents of (.+)', line)
        if not match:
            continue
        end = next((n for n in range(index + 1, len(log_lines)) if log_lines[n].startswith('##### ')), len(log_lines))
        text = '\n'.join(log_lines[index + 1:end]) + '\n'
        blocks[match[1]] = {'provenance': 'Contents printed by Unity in the original log, not recovered original file bytes.',
                           'header_line': index + 1, 'last_content_line': end,
                           'logged_text_sha256': hashlib.sha256(text.encode()).hexdigest(),
                           'references': re.findall(r'^-r:"([^"]+)"$', text, re.MULTILINE),
                           'defines': re.findall(r'^-define:(.+)$', text, re.MULTILINE),
                           'analyzers': re.findall(r'^-analyzer:"([^"]+)"$', text, re.MULTILINE),
                           'sources': re.findall(r'^"(Assets/[^\n]+\.cs)"$', text, re.MULTILINE),
                           'language_version': re.findall(r'^-langversion:(.+)$', text, re.MULTILINE)}
    for name, path in sorted(definitions.items()):
        data = json.loads(path.read_text())
        lab.require(digest(path) == review['assemblies'][name]['sha256'], 'Asmdef differs from the frozen review: ' + name)
        lab.check_entities_dependencies(data)
        if name in ('Latios2022Lab.Editor', 'Latios2022Lab.PlayMode'):
            lab.check_test_assembly(data)
        lab.require(data['references'] == review['assemblies'][name]['references'], 'Reviewed references changed: ' + name)
        own_sources = [p for p in sources if p.is_relative_to(path.parent)]
        events = {'Csc': [], 'ILPostProcess': [], 'CopyFiles': [], 'WriteText': []}
        errors = []
        for line_number, line in enumerate(log_lines, 1):
            match = re.match(r'^\[[^\]]+\]\s+(Csc|ILPostProcess|CopyFiles|WriteText) (.+)$', line)
            if match and re.search('/' + re.escape(name) + r'\.(?:dll(?: |$)|rsp2?(?: |$)|UnityAdditionalFile\.txt(?: |$))', match[2]):
                events[match[1]].append({'line': line_number, 'text': line})
            if re.search(r'\berror [A-Z]+\d+:', line) and any(p.relative_to(project).as_posix() in line for p in own_sources):
                errors.append({'line': line_number, 'text': line})
        if errors:
            status = 'FAILED'
        elif all(events[stage] for stage in ('Csc', 'ILPostProcess', 'CopyFiles')):
            status = 'COMPILED_AND_COPIED_IN_RECORDED_RUN'
        elif events['Csc']:
            status = 'CSC_OBSERVED_COMPLETION_UNPROVEN'
        else:
            status = 'NO_CSC_COMPLETION_EVIDENCE'
        report['assemblies'][name] = {
            'asmdef': path.relative_to(project).as_posix(), 'asmdef_sha256': digest(path),
            'references': data['references'], 'source_files': [p.relative_to(project).as_posix() for p in own_sources],
            'entities_collections_direct_closure': 'CHECKED', 'native_status': status,
            'source_hashes_match_native_launch': all(launch.get('inputs_sha256', {}).get(p.relative_to(project).as_posix()) == digest(p) for p in own_sources) if launch else None,
            'asmdef_hash_matches_native_launch': launch.get('inputs_sha256', {}).get(path.relative_to(project).as_posix()) == digest(path) if launch else None,
            'events': events, 'coded_errors': errors,
            'logged_response_contents': {p: block for p, block in blocks.items() if Path(p).name in (name + '.rsp', name + '.rsp2')}}
    if artifact_root:
        files = sorted(p for p in artifact_root.rglob('*') if p.is_file())
        report['retained_compilation_files'] = [p.relative_to(artifact_root).as_posix() for p in files
                                               if p.suffix in ('.rsp', '.rsp2', '.dll', '.cs') or p.name.endswith('.UnityAdditionalFile.txt')]
        manifest_path = artifact_root / 'evidence-sha256.json'
        if manifest_path.is_file() and unity_log:
            expected = json.loads(manifest_path.read_text())[unity_log.relative_to(artifact_root).as_posix()]
            lab.require(expected == digest(unity_log), 'Unity log differs from the evidence manifest.')
            report['unity_log_matches_retained_manifest'] = True
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=lab.PROJECT)
    parser.add_argument('--review', type=Path, default=lab.PROJECT / 'Docs/Validation/20261008-compilation-domain/source-review.json')
    parser.add_argument('--unity-log', type=Path)
    parser.add_argument('--artifact-root', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    report = audit(args.project, args.review, args.unity_log, args.artifact_root)
    text = json.dumps(report, indent=2) + '\n'
    if args.output:
        args.output.write_text(text)
    else:
        print(text, end='')


if __name__ == '__main__':
    main()
