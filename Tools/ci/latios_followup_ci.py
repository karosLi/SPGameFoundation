#!/usr/bin/env python3
"""Request-only CI bridge for the separately gated frozen-source Latios follow-up."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / 'Latios2022Lab/Tools'))
import followup

ci = followup.ci
require = followup.require
SCHEMA = 'latios-followup-ci-v1'
REQUEST = 'Latios2022Lab/followup-request.json'
WORKFLOW = '.github/workflows/latios-followup.yml'
NATIVE_COMMIT = 'b6ef271fc8eded93c6e79c0dfcf90917b72b7945'
NATIVE_TREE = 'dbda0866da350caa91029ce2d1cae855c61ce06b'
HOSTED_HARNESS_SHA256 = 'a844f9e971a09577f782d1617f6fefc79835545943ea52b678722cf6c7093a9f'
NATIVE_WORKFLOWS = {WORKFLOW, ci.WORKFLOW, ci.ROOT_WORKFLOW,
                    '.github/workflows/unity-shooter-native-allocation-diagnostic.yml'}
EDITOR_BINDING = 'Latios2022Lab/Docs/Validation/20261008-patched-local-editor/editor-artifact-binding.json'
TOOL_FILES = {WORKFLOW, REQUEST, 'Tools/ci/latios_followup_ci.py', 'Tools/ci/test_latios_followup_ci.py',
              'Tools/ci/latios_followup_artifacts.py', 'Tools/ci/test_latios_followup_artifacts.py',
              'Latios2022Lab/Tools/followup.py', 'Latios2022Lab/Tools/test_followup.py', 'Latios2022Lab/README.md',
              'Docs/Latios2022LabCI.md', 'Docs/Latios2022LabPreparation.md'}
REQUEST_FIELDS = {'schema', 'repository', 'tool_commit', 'source_commit', 'source_tree', 'stage', 'allowed_phases',
                  'target', 'architecture', 'burst', 'p0_commit', 'p0_native_status', 'p0_native_evidence_reviewed',
                  'p0_evidence_url', 'runner_reserved', 'coordinator', 'expires_utc', 'experiment',
                  'editor_artifacts', 'editor_evidence_reviewed'}


def artifact_module():
    return followup.module('latios_followup_artifacts', Path(__file__).with_name('latios_followup_artifacts.py'))


def check_request(request, repo, repository, ref):
    require(isinstance(request, dict) and request.get('schema') == SCHEMA, 'Unsupported follow-up request schema.')
    require(ref == ci.BRANCH and request.get('repository') == repository, 'Follow-up must use the exact repository/Lab branch.')
    stage = request.get('stage')
    require(stage in followup.STAGES and request.get('allowed_phases') == followup.STAGES[stage], 'Unreleased stage/phase sequence.')
    expected = REQUEST_FIELDS | ({'repeat_artifacts', 'repeat_evidence_reviewed'} if stage == 'mac-il2cpp-smoke' else set())
    require(set(request) == expected, 'Unknown/missing request fields; repeat and player require independent requests.')
    require(request['source_commit'] == NATIVE_COMMIT and request['source_tree'] == NATIVE_TREE,
            'Only the verified b6ef271 frozen native source is selected.')
    require(re.fullmatch('[0-9a-f]{40}', request.get('tool_commit', '')), 'Missing approved preparation SHA.')
    require(request.get('target') == 'StandaloneOSX' and request.get('burst') == 'on'
            and request.get('architecture') in ('arm64', 'x86_64'), 'Wrong Mac target/Burst/explicit architecture.')
    require(request.get('editor_evidence_reviewed') is True, 'Original Editor source/package/native evidence has not been reviewed.')
    binding = json.loads((repo / EDITOR_BINDING).read_text())
    require(request['editor_artifacts'] == binding, 'Editor artifact binding differs from the verified original b6 evidence.')
    artifacts = artifact_module()
    artifacts.validate_spec(repository, request['editor_artifacts'])
    if stage == 'mac-il2cpp-smoke':
        require(request.get('repeat_evidence_reviewed') is True, 'Repeat import original evidence has not been reviewed.')
        require(request['repeat_artifacts'].get('workflow') == WORKFLOW, 'Player needs a completed separate repeat workflow.')
        artifacts.validate_spec(repository, request['repeat_artifacts'])
    p0 = dict(request, schema=1, approved_source_commit=request['source_commit'], allowed_phases=['import', 'editmode', 'playmode'])
    ci.check_request(p0, repository, ref)
    return request


def check_prepared_source(repo, request):
    prepared = request['tool_commit']
    require(ci.git(repo, 'rev-parse', request['source_commit'] + '^{tree}') == request['source_tree'], 'Frozen native tree differs.')
    subprocess.run(['git', 'merge-base', '--is-ancestor', request['source_commit'], prepared], cwd=repo, check=True)
    changed = ci.git(repo, 'diff', '--name-only', request['source_commit'], prepared, '--').splitlines()
    require(all(name in TOOL_FILES or name.startswith('Latios2022Lab/Docs/') for name in changed),
            'Approved preparation includes native fixture/product/original workflow changes.')
    original = json.loads(ci.git(repo, 'show', request['source_commit'] + ':' + ci.REQUEST))
    for name in ('p0_commit', 'p0_evidence_url'):
        require(request[name] == original[name], 'P0 trust anchor differs from the frozen native release.')
    for row in ci.git(repo, 'ls-tree', '-r', prepared, '--', 'Latios2022Lab', 'Tools/ci', '.github/workflows').splitlines():
        require(row.split()[0] not in ('120000', '160000'), 'Preparation cannot contain symlink/submodule inputs.')


def check_release_chain(repo, prepared, release):
    require(re.fullmatch('[0-9a-f]{40}', prepared) and re.fullmatch('[0-9a-f]{40}', release), 'Missing precise release/tool SHA.')
    subprocess.run(['git', 'merge-base', '--is-ancestor', prepared, release], cwd=repo, check=True)
    commits = ci.git(repo, 'rev-list', '--reverse', prepared + '..' + release).splitlines()
    require(0 < len(commits) <= 32, 'Release must be a bounded request-only child chain of reviewed preparation.')
    parent = prepared
    for commit in commits:
        require(ci.git(repo, 'show', '-s', '--format=%P', commit).split() == [parent], 'Release chain must be linear, without merge/rebase ambiguity.')
        require(ci.git(repo, 'diff', '--name-only', parent, commit, '--').splitlines() == [REQUEST],
                'Every release-chain commit must change only followup-request.json.')
        parent = commit
    require(parent == release, 'Release chain does not end at the triggering SHA.')


def validate_release(repo, repository, ref, sha, prepared_checkout=False):
    repo = followup.canonical(repo)
    require(ci.git(repo, 'rev-parse', 'HEAD') == sha and not ci.git(repo, 'status', '--porcelain', '--untracked-files=normal'),
            'Release checkout must be clean at the exact triggering commit.')
    require((repo / REQUEST).stat().st_size <= 256 * 1024, 'Conditional request exceeds 256 KiB.')
    raw = (repo / REQUEST).read_text()
    require(raw == subprocess.check_output(['git', 'show', sha + ':' + REQUEST], cwd=repo, text=True), 'Working request differs from the release object.')
    request = check_request(json.loads(raw), repo, repository, ref)
    check_prepared_source(repo, request)
    check_release_chain(repo, request['tool_commit'], sha)
    if prepared_checkout:
        require(ci.git(REPO, 'rev-parse', 'HEAD') == request['tool_commit']
                and not ci.git(REPO, 'status', '--porcelain', '--untracked-files=normal'),
                'Run follow-up from the exact approved preparation checkout, not the gate-only release commit.')
        # Both checkouts must see the same immutable request object.
        require(ci.git(REPO, 'show', sha + ':' + REQUEST) == raw.strip(), 'Preparation checkout lacks the exact release object.')
    return request


def check_own_queue(repository, run_id, fetch=ci.api_get):
    require(type(run_id) is int and run_id > 0, 'Missing current Actions run identity.')
    for status in ('queued', 'in_progress', 'waiting', 'pending', 'requested'):
        data = fetch(repository, 'workflows/latios-followup.yml/runs?status=' + status + '&per_page=100')
        runs = data.get('workflow_runs', [])
        require(data.get('total_count') == len(runs), 'Incomplete conditional queue inventory.')
        require(all(run.get('id') == run_id for run in runs), 'Another conditional workflow is queued/running; reservation is not exclusive.')


def check_native_queue(repository, run_id, repo=REPO, fetch=ci.api_get):
    """All active workflows, including diagnostics; exempt only proven hosted-only definitions."""
    require(type(run_id) is int and run_id > 0, 'Missing current Actions run identity.')
    seen = set()
    for status in ('queued', 'in_progress', 'waiting', 'pending', 'requested'):
        data = fetch(repository, 'runs?status=' + status + '&per_page=100')
        runs = data.get('workflow_runs', [])
        require(data.get('total_count') == len(runs), 'Incomplete repository queue inventory.')
        for run in runs:
            identifier, path = run.get('id'), run.get('path')
            require(type(identifier) is int and identifier > 0, 'Unknown queued run identity.')
            if identifier in seen:
                continue
            seen.add(identifier)
            if identifier == run_id:
                require(path == WORKFLOW, 'Current queue identity is not this conditional workflow.')
                continue
            require(path not in NATIVE_WORKFLOWS, 'Shared Mac queue is occupied by ' + str(path))
            # Job listings can omit downstream jobs that do not exist yet. Do not mistake a hosted
            # gate job for proof that the whole workflow is hosted-only. This exact known definition
            # has one ubuntu-latest job; unknown/missing definitions conservatively block admission.
            require(path == '.github/workflows/harness.yml' and re.fullmatch('[0-9a-f]{40}', run.get('head_sha', '')),
                    'Unknown workflow runner target; shared Mac cannot be proved free: ' + str(path))
            try:
                content = subprocess.check_output(['git', 'show', run['head_sha'] + ':' + path], cwd=repo, stderr=subprocess.DEVNULL)
            except subprocess.SubprocessError:
                raise ValueError('Queued hosted workflow source is unavailable; runner scope is unverified.') from None
            require(followup.hashlib.sha256(content).hexdigest() == HOSTED_HARNESS_SHA256,
                    'Queued harness definition changed; hosted-only runner scope is unverified.')


def check_repeat_identity(root, spec, request, release_repo, release_sha):
    context = json.loads((root / 'followup-ci.json').read_text())
    require(context.get('repository') == request['repository'] and context.get('ref') == ci.BRANCH
            and context.get('run_id') == spec['run_id'] and context.get('attempt') == spec['attempt']
            and context.get('release_commit') == spec['head_sha'] and context.get('stage') == 'repeat-clean-import',
            'Repeat artifact/run/release/stage identity differs.')
    previous = json.loads(ci.git(release_repo, 'show', spec['head_sha'] + ':' + REQUEST))
    require(previous.get('stage') == 'repeat-clean-import' and previous.get('source_commit') == request['source_commit']
            and previous.get('source_tree') == request['source_tree'] and previous.get('tool_commit') == context.get('tool_commit'),
            'Repeat was not a separately released same-source import.')
    require(context.get('request_sha256') == followup.hashlib.sha256(
        subprocess.check_output(['git', 'show', spec['head_sha'] + ':' + REQUEST], cwd=release_repo)).hexdigest(),
        'Repeat request bytes differ from the archived release binding.')
    check_release_chain(release_repo, previous['tool_commit'], spec['head_sha'])
    subprocess.run(['git', 'merge-base', '--is-ancestor', spec['head_sha'], release_sha], cwd=release_repo, check=True)


def prepare_gate(release_repo, repository, ref, sha, review, run_id, restore=None):
    request = validate_release(release_repo, repository, ref, sha, prepared_checkout=True)
    check_own_queue(repository, run_id)
    check_native_queue(repository, run_id, release_repo)
    review = followup.canonical(review)
    require(not review.exists() and review.parent.is_dir(), 'Use a fresh separate prerequisite review directory.')
    review.mkdir()
    restore = restore or artifact_module().restore_artifacts
    root = restore(repository, request['editor_artifacts'], review / 'editor')
    transport_fields = {'editor_artifacts', 'editor_evidence_reviewed', 'repeat_artifacts', 'repeat_evidence_reviewed'}
    gate = {key: value for key, value in request.items() if key not in transport_fields}
    gate['schema'] = followup.SCHEMA
    gate['editor_evidence'] = dict(root=str(root), manifest_sha256=request['editor_artifacts']['evidence_manifest_sha256'],
                                   reviewed=True, run_url=request['editor_artifacts']['run_url'])
    if request['stage'] == 'mac-il2cpp-smoke':
        repeated = restore(repository, request['repeat_artifacts'], review / 'repeat')
        check_repeat_identity(repeated, request['repeat_artifacts'], request, release_repo, sha)
        gate['repeat_evidence'] = dict(root=str(repeated), manifest_sha256=request['repeat_artifacts']['evidence_manifest_sha256'],
                                      reviewed=True, run_url=request['repeat_artifacts']['run_url'])
    # This reads every original hash and exact native phase/package/source record before the Mac job is admitted.
    followup.check_gate(gate)
    followup.check_remote(gate)
    check_native_queue(repository, run_id, release_repo)
    path = review / 'conditional-gate.json'
    ci.write_json(path, gate)
    ci.write_json(review / 'prerequisite-summary.json', dict(status='VERIFIED_PREREQUISITES_ONLY', native='NOT_RUN',
                  release_commit=sha, tool_commit=request['tool_commit'], source_commit=request['source_commit'],
                  source_tree=request['source_tree'], stage=request['stage'], run_id=run_id,
                  editor_run_url=request['editor_artifacts']['run_url'],
                  repeat_run_url=request.get('repeat_artifacts', {}).get('run_url'),
                  editor_evidence_manifest_sha256=request['editor_artifacts']['evidence_manifest_sha256'],
                  repeat_evidence_manifest_sha256=request.get('repeat_artifacts', {}).get('evidence_manifest_sha256')))
    return request, path


def execute(release_repo, repository, ref, sha, review, run_id, attempt, workspace, editor):
    require(sys.platform == 'darwin', 'Conditional native phase requires the existing Mac runner.')
    require(type(attempt) is int and attempt > 0, 'Missing exact Actions attempt.')
    # Refusal of an existing/foreign workspace must never enter the evidence-writing finally block.
    workspace = ci.validate_workspace(REPO, workspace)
    request, gate_path = prepare_gate(release_repo, repository, ref, sha, review, run_id)
    request_hash = ci.digest(release_repo / REQUEST)
    try:
        return followup.execute(gate_path, workspace, editor,
                                queue_check=lambda: check_native_queue(repository, run_id, release_repo))
    finally:
        evidence = workspace / 'Artifacts'
        if evidence.is_dir():
            followup.canonical(evidence)
            # Preserve CI provenance even when the original native invocation fails.
            ci.write_json(evidence / 'followup-ci.json', dict(repository=repository, ref=ref, release_commit=sha,
                          tool_commit=request['tool_commit'], source_commit=request['source_commit'], source_tree=request['source_tree'],
                          stage=request['stage'], run_id=run_id, attempt=attempt, request_sha256=request_hash))
            shutil.copyfile(release_repo / REQUEST, evidence / 'followup-request.json')
            shutil.copyfile(review / 'prerequisite-summary.json', evidence / 'prerequisite-summary.json')
            ci.write_evidence_hashes(evidence)


def package(workspace, output, run_id, attempt, sha):
    workspace = followup.canonical(workspace)
    evidence = followup.canonical(workspace / 'Artifacts')
    context = json.loads((evidence / 'followup-ci.json').read_text())
    require(context.get('run_id') == run_id and context.get('attempt') == attempt and context.get('release_commit') == sha,
            'Refusing to package stale evidence from another conditional run/attempt/release.')
    output = followup.canonical(output)
    require(not output.exists(), 'Evidence packaging must use a new output directory.')
    output.mkdir()
    ci.package(workspace, output)
    summary = workspace / 'Artifacts/followup-summary.json'
    require(summary.is_file() and summary.stat().st_size <= 1024 * 1024, 'Bounded conditional summary is missing.')
    shutil.copyfile(summary, output / 'early/followup-summary.json')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('plan', 'release', 'validate', 'execute', 'package'), nargs='?', default='plan')
    parser.add_argument('--release-repo', type=Path, default=REPO)
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY', ''))
    parser.add_argument('--ref', default=os.environ.get('GITHUB_REF', ''))
    parser.add_argument('--sha', default=os.environ.get('GITHUB_SHA', ''))
    parser.add_argument('--run-id', type=int, default=int(os.environ.get('GITHUB_RUN_ID', '0')))
    parser.add_argument('--attempt', type=int, default=int(os.environ.get('GITHUB_RUN_ATTEMPT', '0')))
    parser.add_argument('--review', type=Path)
    parser.add_argument('--workspace', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--editor', type=Path)
    parser.add_argument('--github-output', type=Path)
    args = parser.parse_args(argv)
    if args.command == 'plan':
        print(json.dumps(dict(status='PLAN_ONLY', native='NOT_RUN', request=REQUEST, workflow=WORKFLOW,
                              stages=followup.STAGES, source_commit=NATIVE_COMMIT), indent=2))
        return 0
    if args.command == 'release':
        request = validate_release(args.release_repo, args.repository, args.ref, args.sha)
        if args.github_output is not None:
            with args.github_output.open('a') as output:
                output.write('tool_commit=' + request['tool_commit'] + '\nstage=' + request['stage'] + '\n')
        print('REQUEST_ONLY_CHAIN_VERIFIED; native NOT_RUN')
    elif args.command == 'package':
        require(args.workspace is not None and args.output is not None, 'Explicit workspace/output required.')
        package(args.workspace, args.output, args.run_id, args.attempt, args.sha)
    else:
        require(args.review is not None, 'Explicit fresh prerequisite review directory required.')
        if args.command == 'validate':
            prepare_gate(args.release_repo, args.repository, args.ref, args.sha, args.review, args.run_id)
            print('ORIGINAL_PREREQUISITES_VERIFIED; native NOT_RUN')
        else:
            require(args.workspace is not None, 'Explicit fresh native workspace required.')
            editor = args.editor or Path('/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents/MacOS/Unity')
            execute(args.release_repo, args.repository, args.ref, args.sha, args.review, args.run_id, args.attempt, args.workspace, editor)
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, KeyError, OSError, subprocess.SubprocessError) as error:
        print('BLOCKED: ' + str(error), file=sys.stderr)
        sys.exit(2)
