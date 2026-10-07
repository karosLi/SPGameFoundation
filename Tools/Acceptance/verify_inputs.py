#!/usr/bin/env python3
"""Read-only verification of the ignored, pinned source dependency used by the existing harness."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

MATH_COMMIT = 'f110c8c230d253654afed153569030a587cc7557'


def require(condition, message):
    if not condition: raise ValueError(message)


def command(args, cwd):
    return subprocess.check_output(args, cwd=cwd, text=True).strip()


def capture(root):
    root = Path(root).resolve()
    require(not command(['git','diff','HEAD','--','Assets','Tools'],root), 'changed tracked Assets/Tools inputs')
    require(not command(['git','ls-files','--others','--exclude-standard','--','Assets','Tools'],root), 'untracked Assets/Tools inputs')
    math = root/'Tools/DotnetHarness/.deps/Unity.Mathematics'
    require(command(['git','rev-parse','HEAD'],math) == MATH_COMMIT, 'Unity.Mathematics dependency differs from harness pin')
    require(not command(['git','status','--porcelain','--untracked-files=all'],math), 'dirty Unity.Mathematics dependency')
    source = Path('src/Unity.Mathematics')
    tracked = command(['git','ls-files','--',str(source)],math).splitlines()
    expected = {p for p in tracked if p.endswith('.cs')}
    actual = {p.relative_to(math).as_posix() for p in (math/source).rglob('*.cs')}
    require(expected and expected == actual, 'ignored/untracked/missing C# dependency source would change the harness input')
    digest = hashlib.sha256()
    for name in sorted(expected):
        digest.update(name.encode('utf-8')+b'\0'); digest.update((math/name).read_bytes()); digest.update(b'\0')
    info = command(['dotnet','--info'],root)
    require(info and len(info) <= 131072, 'SDK/runtime info unavailable or unbounded')
    return validate_context({'schema_version':1,'code_commit':command(['git','rev-parse','HEAD'],root),
                            'code_tree':command(['git','rev-parse','HEAD^{tree}'],root),
                            'unity_mathematics_commit':MATH_COMMIT,'unity_mathematics_source_sha256':digest.hexdigest(),
                            'unity_mathematics_clean':True,'dotnet_info':info})


def validate_context(context):
    require(isinstance(context,dict) and set(context)=={'schema_version','code_commit','code_tree','unity_mathematics_commit',
            'unity_mathematics_source_sha256','unity_mathematics_clean','dotnet_info'}, 'toolchain context schema mismatch')
    require(type(context['schema_version']) is int and context['schema_version']==1, 'toolchain version')
    for key in ['code_commit','code_tree']: require(isinstance(context[key],str) and re.fullmatch('[0-9a-f]{40}',context[key]), 'source identity')
    require(context['unity_mathematics_commit']==MATH_COMMIT and context['unity_mathematics_clean'] is True, 'dependency not pinned/clean')
    require(isinstance(context['unity_mathematics_source_sha256'],str) and re.fullmatch('[0-9a-f]{64}',context['unity_mathematics_source_sha256']), 'dependency hash')
    require(isinstance(context['dotnet_info'],str) and 0<len(context['dotnet_info'])<=131072, 'SDK/runtime context')
    return context


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('repo',type=Path)
    print(json.dumps(capture(parser.parse_args().repo),indent=2))
