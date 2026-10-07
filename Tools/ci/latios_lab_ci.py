#!/usr/bin/env python3
"""Deliberately released, isolated S1a Editor control. No native default or player build."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time
import urllib.request
import zipfile
import xml.etree.ElementTree as ET

BRANCH = 'refs/heads/dot/latios-lab-validate'
REQUEST = 'Latios2022Lab/ci-request.json'
WORKFLOW = '.github/workflows/latios-lab.yml'
ROOT_WORKFLOW = '.github/workflows/unity-self-hosted.yml'
EXCLUSION = '    branches-ignore: [dot/latios-lab-validate]\n'
PHASES = (('import', 'on'), ('editmode', 'on'), ('editmode', 'off'),
          ('playmode', 'on'), ('playmode', 'off'))
ALLOWED_FILES = {WORKFLOW, ROOT_WORKFLOW, 'Tools/ci/latios_lab_ci.py',
                 'Tools/ci/test_latios_lab_ci.py', 'Docs/Latios2022LabPreparation.md',
                 'Docs/Latios2022LabCI.md'}
MAX_SECONDS = 1800
EARLY_BYTES = 16 * 1024 * 1024


def require(value, message):
    if not value:
        raise ValueError(message)


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def git(repo, *arguments):
    return subprocess.check_output(['git', *arguments], cwd=repo, text=True).strip()


def check_request(request, repository, ref, now=None):
    require(ref == BRANCH, 'Only the deliberate dot/latios-lab-validate branch may run.')
    require(re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository), 'Invalid repository.')
    require(request.get('schema') == 1, 'Unsupported CI request schema.')
    require(request.get('repository') == repository, 'CI request belongs to another repository.')
    for key in ('p0_commit', 'approved_source_commit'):
        require(re.fullmatch('[0-9a-f]{40}', request.get(key, '')), 'Missing exact ' + key + '.')
    require(request.get('p0_native_status') == 'passed', 'P0 native acceptance has not passed.')
    require(request.get('p0_native_evidence_reviewed') is True,
            'Coordinator must inspect original P0 XML/logs, including Burst and fallback status.')
    require(request.get('runner_reserved') is True and request.get('coordinator', '').strip(),
            'Coordinator has not released the shared runner.')
    require(request.get('allowed_phases') == ['import', 'editmode', 'playmode'],
            'CI release must contain exactly the three Editor phases; player is a later gate.')
    expiry = datetime.fromisoformat(request['expires_utc'].replace('Z', '+00:00'))
    require(expiry.tzinfo is not None and expiry > (now or datetime.now(timezone.utc)), 'CI release expired.')
    match = re.fullmatch(r'https://github\.com/' + re.escape(repository) +
                         r'/actions/runs/([1-9][0-9]*)/attempts/([1-9][0-9]*)',
                         request.get('p0_evidence_url', ''))
    require(match, 'Evidence must identify the exact GitHub P0 run and attempt in this repository.')
    return int(match[1]), int(match[2])


def check_source(repo, request, sha):
    require(re.fullmatch('[0-9a-f]{40}', sha), 'Missing exact source revision.')
    require(git(repo, 'rev-parse', 'HEAD') == sha, 'Checkout is not the triggering revision.')
    require(not git(repo, 'status', '--porcelain', '--untracked-files=normal'), 'Source checkout must be clean.')
    baseline = request['p0_commit']
    prepared = request['approved_source_commit']
    # Tree/ancestor checks use actual Git objects, never a caller-supplied file list.
    for commit in (baseline, prepared):
        subprocess.run(['git', 'merge-base', '--is-ancestor', commit, sha], cwd=repo, check=True)
    release_delta = git(repo, 'diff', '--name-only', prepared, sha, '--').splitlines()
    require(release_delta == [REQUEST], 'Release must change only ci-request.json from the reviewed source.')
    changed = git(repo, 'diff', '--name-only', baseline, sha, '--').splitlines()
    unexpected = [name for name in changed if not (name.startswith('Latios2022Lab/') or name in ALLOWED_FILES)]
    require(not unexpected, 'Non-lab changes differ from the exact P0 source: ' + ', '.join(unexpected))
    if ROOT_WORKFLOW in changed:
        old = git(repo, 'show', baseline + ':' + ROOT_WORKFLOW)
        new = git(repo, 'show', sha + ':' + ROOT_WORKFLOW)
        require(old.count('  push:\n') == 1 and EXCLUSION not in old,
                'P0 root workflow is not the expected unmodified push workflow.')
        require(new == old.replace('  push:\n', '  push:\n' + EXCLUSION, 1),
                'The root workflow may only exclude this exact lab branch from push events.')
    # Symlinks and submodules cannot smuggle a project/cache outside its owned directory.
    for row in git(repo, 'ls-tree', '-r', sha, '--', 'Latios2022Lab', 'Tools/ci', '.github/workflows').splitlines():
        require(row.split()[0] not in ('120000', '160000'), 'Lab/CI source contains a symlink or submodule.')
    return {'p0_commit': baseline, 'source_commit': sha,
            'source_tree': git(repo, 'rev-parse', sha + '^{tree}'),
            'approved_source_commit': prepared, 'changed_from_p0': changed}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, newurl):
        raise ValueError('Actions API redirected; stop rather than forward the job token.')


def api_get(repository, suffix):
    # Standard job-scoped read token only. Never print/store it or expose it to Unity.
    token = os.environ.get('GITHUB_TOKEN')
    require(token, 'The standard read-only Actions token is unavailable.')
    request = urllib.request.Request('https://api.github.com/repos/' + repository + '/actions/' + suffix,
                                     headers={'Accept': 'application/vnd.github+json',
                                              'X-GitHub-Api-Version': '2022-11-28',
                                              'Authorization': 'Bearer ' + token})
    with urllib.request.build_opener(NoRedirect).open(request, timeout=30) as response:
        require(response.geturl().startswith('https://api.github.com/'), 'Unexpected API redirect.')
        return json.load(response)


def check_p0(request, repository, ref, fetch=api_get):
    run_id, attempt = check_request(request, repository, ref)
    run = fetch(repository, 'runs/{}/attempts/{}'.format(run_id, attempt))
    require(run.get('id') == run_id and run.get('run_attempt') == attempt, 'P0 run/attempt mismatch.')
    require(run.get('repository', {}).get('full_name') == repository, 'P0 run repository mismatch.')
    require(run.get('head_sha') == request['p0_commit'], 'P0 run is for a different commit.')
    require(run.get('path') == ROOT_WORKFLOW, 'P0 evidence is not the SPF self-hosted workflow.')
    require(run.get('status') == 'completed' and run.get('conclusion') == 'success',
            'P0 workflow is pending, failed, skipped, cancelled, or otherwise unsuccessful.')
    # A skipped job can coexist with a successful workflow; require the actual native step.
    data = fetch(repository, 'runs/{}/attempts/{}/jobs?per_page=100'.format(run_id, attempt))
    require(data.get('total_count') == len(data.get('jobs', [])), 'Incomplete P0 job inventory.')
    jobs = [job for job in data['jobs'] if job.get('name') == 'unity-tests']
    require(len(jobs) == 1 and jobs[0].get('conclusion') == 'success', 'P0 native job did not succeed.')
    steps = [step for step in jobs[0].get('steps', []) if step.get('name') == 'Run EditMode and PlayMode tests']
    require(len(steps) == 1 and steps[0].get('conclusion') == 'success', 'P0 native test step did not succeed.')
    return {'run_id': run_id, 'attempt': attempt, 'head_sha': run['head_sha'],
            'workflow': run['path'], 'conclusion': run['conclusion'], 'job_id': jobs[0]['id'],
            'evidence_url': request['p0_evidence_url'],
            'scope': 'API confirms workflow/job/step only; coordinator reviewed native artifacts separately.'}


def check_queue(repository, fetch=api_get):
    for status in ('queued', 'in_progress', 'waiting', 'pending', 'requested'):
        runs = fetch(repository, 'workflows/unity-self-hosted.yml/runs?status=' + status + '&per_page=1')
        require(runs.get('total_count') == 0, 'SPF native queue is not released: ' + status + '.')


def load_request(repo):
    return json.loads((repo / REQUEST).read_text())


def validate(repo, repository, ref, sha):
    request = load_request(repo)
    check_request(request, repository, ref)
    source = check_source(repo, request, sha)
    source['p0_verified'] = check_p0(request, repository, ref)
    return request, source


def product_fingerprint(repo):
    return {str(path.relative_to(repo)): digest(path)
            for directory in ('Packages', 'ProjectSettings')
            for path in (repo / directory).rglob('*') if path.is_file()}


def terminate_owned_group(process, grace_seconds=20):
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        process.wait()
        return
    deadline = time.monotonic() + grace_seconds
    while True:
        process.poll()  # Reap the launcher; surviving Unity/compiler children still own this group.
        try:
            os.killpg(process.pid, 0)
        except ProcessLookupError:
            break
        if time.monotonic() >= deadline:
            # Escalate for surviving descendants even if the Python launcher already exited on TERM.
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            break
        time.sleep(0.1)
    process.wait()


def run_owned_process(command, cwd, log, seconds=MAX_SECONDS):
    # Only this newly created process group may be stopped on timeout. Never pgrep/kill other editors.
    with log.open('wb') as stream:
        process = subprocess.Popen(command, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT,
                                   start_new_session=True,
                                   env={key: value for key, value in os.environ.items() if key != 'GITHUB_TOKEN'})
        try:
            return process.wait(timeout=seconds)
        except subprocess.TimeoutExpired:
            terminate_owned_group(process)
            return 124


def collect(project, artifacts):
    native = project / 'Artifacts'
    if native.exists():
        shutil.copytree(native, artifacts / 'native', dirs_exist_ok=True)
    inputs = artifacts / 'lab-inputs'
    for folder in ('Packages', 'ProjectSettings'):
        source = project / folder
        if source.exists():
            shutil.copytree(source, inputs / folder, dirs_exist_ok=True)
    xml = []
    for path in sorted(artifacts.rglob('*.xml')):
        record = {'path': path.relative_to(artifacts).as_posix(), 'sha256': digest(path)}
        try:
            root = ET.parse(path).getroot()
            cases = list(root.iter('test-case'))
            results = {}
            for case in cases:
                name = case.get('result', 'UNKNOWN')
                results[name] = results.get(name, 0) + 1
            record.update(run_attributes=root.attrib, discovered_cases=len(cases), case_results=results)
        except ET.ParseError as error:
            record['parse_error'] = str(error)
        xml.append(record)
    write_json(artifacts / 'xml-summary.json', xml)
    write_json(artifacts / 'evidence-sha256.json', {
        str(path.relative_to(artifacts)): digest(path) for path in artifacts.rglob('*')
        if path.is_file() and path.name != 'evidence-sha256.json'})


def validate_workspace(repo, workspace):
    require(workspace.is_absolute() and not workspace.exists(), 'Use a new absolute run-specific workspace.')
    require(not any(path.is_symlink() for path in (workspace, *workspace.parents)),
            'Workspace ancestry must not contain symlinks.')
    workspace = workspace.resolve()
    repo = repo.resolve()
    require(repo not in workspace.parents and workspace not in repo.parents,
            'Isolated workspace must be outside the checkout.')
    require(workspace.parent.is_dir(), 'Invalid workspace parent.')
    return workspace


def execute(repo, workspace, repository, ref, sha, editor):
    require(sys.platform == 'darwin', 'This release targets only the approved existing Mac runner.')
    request, source = validate(repo, repository, ref, sha)
    check_queue(repository)
    workspace = validate_workspace(repo, workspace)
    workspace.mkdir()
    artifacts = workspace / 'Artifacts'
    artifacts.mkdir()
    source['phases'] = [{'phase': phase, 'burst': burst, 'status': 'NOT_RUN'} for phase, burst in PHASES]
    source.update(player_build='NOT_RUN', player_execution='NOT_RUN', repeat_clean_import='NOT_RUN',
                  mobile='NOT_RUN', s1a='INCOMPLETE')
    write_json(artifacts / 'ci-summary.json', source)
    write_json(artifacts / 'release-request.json', request)
    working = workspace / 'source'
    project = working / 'Latios2022Lab'
    baseline = product_fingerprint(repo)
    try:
        subprocess.run(['git', 'worktree', 'add', '--detach', str(working), sha], cwd=repo, check=True)
        require(product_fingerprint(working) == baseline, 'Isolated product inputs differ from source.')
        require(not any((project / path).exists() for path in ('Library', '.upm-cache', 'Temp', '.launch-lock')),
                'Native control must start with a fresh lab Library/cache and no lock.')
        selected = editor or Path('/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents/MacOS/Unity')
        require(selected.is_absolute() and selected.is_file() and os.access(selected, os.X_OK),
                'Installed Unity 2022.3.62f2 executable is unavailable; no installation attempted.')
        gate = dict(request, project_path=str(project))
        gate_path = workspace / 'runtime-gate.json'
        write_json(gate_path, gate)
        for index, (phase, burst) in enumerate(PHASES):
            record = source['phases'][index]
            source['p0_verified'] = check_p0(request, repository, ref)
            check_queue(repository)
            record['status'] = 'RUNNING'
            write_json(artifacts / 'ci-summary.json', source)
            command = [sys.executable, str(project / 'Tools/lab.py'), phase, '--burst', burst,
                       '--editor', str(selected), '--target', 'StandaloneOSX', '--gate', str(gate_path), '--execute']
            record['command'] = command
            code = run_owned_process(command, project, artifacts / ('%02d-%s-%s-console.log' % (index, phase, burst)))
            record.update(exit_code=code, status='PASSED' if code == 0 else 'FAILED')
            require(product_fingerprint(repo) == baseline and product_fingerprint(working) == baseline,
                    'Root Packages/ProjectSettings changed; no later phase may start.')
            collect(project, artifacts)
            write_json(artifacts / 'ci-summary.json', source)
            require(code == 0, 'First control-group failure: {} Burst {}; remaining phases NOT_RUN.'.format(phase, burst))
        source['editor_control'] = 'PASSED'
        return 0
    except Exception as exc:
        source['error'] = str(exc)
        raise
    finally:
        # Preserve failing output, even if the original launcher raised before its success manifest.
        write_json(artifacts / 'ci-summary.json', source)
        if project.exists():
            collect(project, artifacts)
        # Keep this run-specific project/worktree for diagnosis. Never clean the SPF workspace/cache.


def package(workspace, output):
    source = workspace / 'Artifacts'
    require(source.is_dir(), 'No run evidence was produced; source validation may have blocked launch.')
    spec = importlib.util.spec_from_file_location('evidence_parts', Path(__file__).with_name('evidence_parts.py'))
    evidence = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(evidence)
    evidence.package_evidence(source, output / 'parts')
    early = output / 'early'
    candidate = output / 'early-review.candidate.zip'
    # Small XML, locks, source pins and summaries are immediately reviewable. Full logs stay in parts.
    # Log excerpts are explicitly labelled and hash-linked to the complete retained original.
    with zipfile.ZipFile(candidate, 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(source.rglob('*')):
            if not path.is_file():
                continue
            relative = path.relative_to(source).as_posix()
            if path.suffix in ('.xml', '.json', '.txt'):
                archive.write(path, relative)
            elif path.suffix == '.log':
                with path.open('rb') as stream:
                    head = stream.read(64 * 1024)
                    stream.seek(max(0, path.stat().st_size - 64 * 1024))
                    tail = stream.read()
                archive.writestr(relative + '.excerpt.txt',
                                 ('EXCERPT ONLY; complete file in bounded parts. SHA256=' + digest(path) + '\nHEAD\n').encode()
                                 + head + b'\nTAIL\n' + tail)
    require(candidate.stat().st_size <= EARLY_BYTES,
            'Early evidence exceeds 16 MiB; full bounded parts remain available, no silent truncation.')
    early.mkdir()
    candidate.rename(early / 'early.zip')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('validate', 'execute', 'package'))
    parser.add_argument('--repo', type=Path, default=Path.cwd())
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY', ''))
    parser.add_argument('--ref', default=os.environ.get('GITHUB_REF', ''))
    parser.add_argument('--sha', default=os.environ.get('GITHUB_SHA', ''))
    parser.add_argument('--workspace', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--editor', type=Path)
    args = parser.parse_args(argv)
    if args.command == 'validate':
        _, source = validate(args.repo.resolve(), args.repository, args.ref, args.sha)
        print(json.dumps(source, indent=2))
    elif args.command == 'execute':
        require(args.workspace is not None, '--workspace is required.')
        execute(args.repo.resolve(), args.workspace, args.repository, args.ref, args.sha, args.editor)
    else:
        require(args.workspace is not None and args.output is not None, '--workspace and --output are required.')
        args.output.mkdir(exist_ok=False)
        package(args.workspace, args.output)
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, KeyError, OSError, subprocess.SubprocessError) as error:
        print('BLOCKED: ' + str(error), file=sys.stderr)
        sys.exit(2)
