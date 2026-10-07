#!/usr/bin/env python3
"""Validate bounded, cold test evidence and render JSON/Markdown. Python standard library only.
Never creates measurements, changes runtime budgets, or signs physical-device acceptance.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re

MAX_BYTES = 2 * 1024 * 1024
WARMUP_TICKS = 16
MEASURED_TICKS = 64
UNKNOWN = {
    'cpu_worker_ms': 'PipelineStats main-thread schedule/wait is not worker CPU time.',
    'gpu_ms': 'No renderer/GPU profiler is active in this simulation-only fixture.',
    'managed_live_bytes': 'Allocation deltas are not live managed memory.',
    'native_live_bytes': 'No native-memory counter sampled.',
    'gpu_live_bytes': 'No GPU memory counter sampled.',
    'upload_payload_bytes': 'No presentation submission in this fixture. PackedPayloadBytes may be supplied by separate graphics evidence.',
    'upload_api_bytes': 'No draw/upload occurs in this fixture. SpriteBatch/ParticleRenderer/BatCharacterBatch.BytesUploaded are separate API accounting.',
    'physical_gpu_traffic_bytes': 'API payload/upload counts never measure physical bus/GPU traffic.',
    'pool_subtick_high_water': 'Completed-tick table maxima do not capture transient sub-tick peaks.',
    'input_rejection_count': 'No common input rejection counter; game-specific input regressions are separate.',
    'allocation_frame_count': 'Synchronous tick allocation probe has no rendered-frame sample.',
    'overdraw': 'VfxBudget.ScreenArea is a transparent-quad coverage budget, not measured GPU overdraw.',
    'thermal': 'No physical Android/iOS device capture.',
    'battery': 'No physical Android/iOS battery measurement.',
}
TARGETS = {'cpu_main_ms', 'cpu_worker_ms', 'gpu_ms', 'managed_live_bytes', 'native_live_bytes', 'gpu_live_bytes', 'upload_api_bytes', 'overdraw'}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def load(path):
    path = Path(path)
    require(path.stat().st_size <= MAX_BYTES, f'{path}: exceeds 2 MiB bound')
    def pairs(items):
        result = {}
        for k, v in items:
            require(k not in result, f'duplicate JSON key: {k}')
            result[k] = v
        return result
    return json.loads(path.read_text(encoding='utf-8'), object_pairs_hook=pairs,
                      parse_constant=lambda x: (_ for _ in ()).throw(ValueError(f'non-finite JSON: {x}')))


def number(value, label, integer=False):
    require(type(value) in ((int,) if integer else (int, float)) and math.isfinite(value) and value >= 0,
            f'{label}: expected finite nonnegative {"integer" if integer else "number"}')


def nonempty(value, label):
    require(isinstance(value, str) and bool(value.strip()) and len(value) <= 4096, f'{label}: text required')


def fields(obj, expected, label):
    require(isinstance(obj, dict) and set(obj) == set(expected), f'{label}: missing/unknown fields')


def validate_probe(p):
    fields(p, ['schema_version','kind','mode','configuration','orientation_intent','session','backend','runtime','platform',
               'started_utc','code_commit','code_tree','seed','tick_rate','warmup_ticks','first_tick','end_tick_exclusive',
               'timing_source','tick_ms','pipeline','allocation','tables','world_create_failures','destroy_queue_overflow',
               'counter_window','presentation_scope','unsupported_views'], 'probe')
    require(type(p['schema_version']) is int and p['schema_version'] == 1 and p['kind'] == 'simulation-probe', 'unsupported probe schema')
    for key in ['mode','configuration','session','platform','started_utc','timing_source','counter_window','presentation_scope','unsupported_views']:
        nonempty(p[key], key)
    require(p['backend'] == 'simulation-only/no-renderer', 'fixture cannot claim a rendered backend')
    require(p['runtime'] in ['dotnet-unity-stubs','unity-native-editmode'], 'unknown runtime scope')
    require(p['orientation_intent'] in ['portrait','landscape','none'], 'orientation_intent')
    for key in ['code_commit','code_tree']:
        require(isinstance(p[key], str) and re.fullmatch('[0-9a-f]{40}', p[key]), f'{key}: exact git identity required')
    for key in ['seed','tick_rate','warmup_ticks','first_tick','end_tick_exclusive','world_create_failures','destroy_queue_overflow']:
        number(p[key], key, True)
    require(p['tick_rate'] > 0 and p['warmup_ticks'] == WARMUP_TICKS and p['first_tick'] >= WARMUP_TICKS, 'v1 needs the declared 16-tick warmup before timing')
    samples = p['tick_ms']
    require(isinstance(samples, list) and len(samples) == MEASURED_TICKS, 'bounded timing window required')
    require(p['end_tick_exclusive'] - p['first_tick'] == len(samples), 'timing samples/window mismatch')
    for n in samples: number(n, 'tick_ms')
    fields(p['pipeline'], ['source','completed_ticks','schedule_ema_ms','sync_wait_ema_ms','tick_wall_ema_ms'], 'pipeline')
    nonempty(p['pipeline']['source'], 'pipeline source')
    for k, v in p['pipeline'].items():
        if k != 'source': number(v, k, k == 'completed_ticks')
    require(p['pipeline']['completed_ticks'] >= len(samples), 'pipeline has no measured tick history')
    a = p['allocation']
    fields(a, ['source','metric','thread_id','first_tick','end_tick_exclusive','value','gen0_process_collections',
               'before_retained','before_empty','after_retained','after_empty'], 'allocation')
    nonempty(a['source'], 'allocation source')
    require(a['metric'] in ['ManagedBytes','AllocationSamples'], 'allocation units must be explicit')
    require((p['runtime'] == 'dotnet-unity-stubs') == (a['metric'] == 'ManagedBytes'), 'probe/runtime units mismatch')
    for k, v in a.items():
        if k not in ['source', 'metric']: number(v, k, True)
    require(a['thread_id'] > 0 and a['end_tick_exclusive'] - a['first_tick'] == MEASURED_TICKS, 'v1 requires a 64-tick allocation window')
    require(a['first_tick'] == p['end_tick_exclusive'], 'allocation window must follow timing window')
    minimum = 32768 if a['metric'] == 'ManagedBytes' else 32
    require(a['before_retained'] >= minimum and a['after_retained'] >= minimum and a['before_empty'] == a['after_empty'] == 0,
            'invalid allocation calibration; zero is not verified')
    require(isinstance(p['tables'], list) and len(p['tables']) <= 128, 'table bound')
    names = set()
    for t in p['tables']:
        fields(t, ['name','capacity','peak_after_tick','final_count'], 'table'); nonempty(t['name'], 'table name')
        require(t['name'] not in names, 'duplicate table'); names.add(t['name'])
        for k in ['capacity','peak_after_tick','final_count']: number(t[k], k, True)
        require(t['capacity'] > 0 and max(t['peak_after_tick'], t['final_count']) <= t['capacity'], 'count exceeds capacity')
    return p


def validate_profiles(p):
    fields(p, ['schema_version','status','inventory_source','platform_targets','quality_tiers','modes'], 'profiles')
    require(type(p['schema_version']) is int and p['schema_version'] == 1 and p['status'] == 'provisional-goals-not-measurements', 'profiles are goals only')
    nonempty(p['inventory_source'], 'inventory source')
    require(set(p['platform_targets']) == {'Android','iOS'}, 'both physical platform gates required')
    for platform, target in p['platform_targets'].items():
        fields(target, ['device_model','os_version','graphics_api_candidates','status'], platform)
        require(target['status'] == 'Pending', 'target selection is not device acceptance')
        for key in ['device_model','os_version']:
            if target[key] is not None: nonempty(target[key], key)
        require(target['graphics_api_candidates'] == (['Vulkan','OpenGLES3'] if platform == 'Android' else ['Metal']), 'API targets are candidates, not actual backends')
    require(len(p['quality_tiers']) == 4, 'four existing FrameGovernor levels required')
    for i, tier in enumerate(p['quality_tiers']):
        fields(tier, ['level','name','render_scale_goal','core_feedback','source'], 'quality tier')
        require(type(tier['level']) is int and tier['level'] == i, 'quality levels must be 0..3')
        number(tier['render_scale_goal'], 'render scale'); require(0 < tier['render_scale_goal'] <= 1, 'render scale range')
        for k in ['name','core_feedback','source']: nonempty(tier[k], k)
    require(isinstance(p['modes'], list) and 1 <= len(p['modes']) <= 64, 'mode bound')
    names = set()
    for m in p['modes']:
        fields(m, ['mode','factory','factory_source','orientation_goal','safe_area_goal','active_fps_goal','fps_source',
                   'table_capacities','capacity_source','targets','quality_scope','simulation_quality_exception'], 'mode profile')
        require(m['mode'] not in names, 'duplicate mode'); names.add(m['mode'])
        for k in ['mode','factory','factory_source','safe_area_goal','fps_source','capacity_source','quality_scope']: nonempty(m[k], k)
        require(m['orientation_goal'] in ['portrait','landscape'], 'orientation goal')
        number(m['active_fps_goal'], 'fps'); require(m['active_fps_goal'] > 0, 'fps must be positive')
        require(isinstance(m['table_capacities'], dict), 'capacity map')
        for k,v in m['table_capacities'].items(): nonempty(k,'table'); number(v,'capacity',True); require(v>0,'capacity')
        require(set(m['targets']) == TARGETS, 'all budget axes required')
        for key,target in m['targets'].items():
            fields(target, ['value','unit','basis'], 'budget target'); nonempty(target['unit'],'unit'); nonempty(target['basis'],'basis')
            if target['value'] is not None: number(target['value'],key)
        require(m['simulation_quality_exception'] is None or isinstance(m['simulation_quality_exception'],str), 'exception')
    return p


def percentiles(values):
    ordered = sorted(values)
    return {'samples': len(values), 'p50_ms': ordered[math.ceil(.5*len(values))-1],
            'p95_ms': ordered[math.ceil(.95*len(values))-1], 'worst_ms': max(values), 'method':'nearest-rank'}


def report(profiles, probes):
    validate_profiles(profiles)
    require(1 <= len(probes) <= 256, 'need 1..256 measured probes')
    ids = set(); identities = set(); known_modes = {m['mode']:m for m in profiles['modes']}
    for p in probes:
        validate_probe(p); key = (p['mode'],p['configuration'],p['runtime'],p['session'])
        require(key not in ids, 'duplicate evidence identity'); ids.add(key); identities.add((p['code_commit'],p['code_tree']))
        require(p['mode'] in known_modes or p['mode']=='external.courier-test-only', 'unknown mode')
    require(len(identities) == 1, 'mixed commit/tree evidence is not one acceptance checkpoint')
    measured = []
    for p in probes:
        q = dict(p); q['tick_statistics'] = percentiles(p['tick_ms'])
        q['unknown_measurements'] = {k:{'value':None,'status':'Unknown','reason':reason} for k,reason in UNKNOWN.items()}
        q['device_acceptance'] = {'Android':'Pending','iOS':'Pending'}
        q['performance_verdict'] = 'Not evaluated: simulation-only software evidence cannot sign a device budget.'
        q['matches_default_factory_capacities'] = (dict((t['name'],t['capacity']) for t in p['tables']) == known_modes[p['mode']]['table_capacities']) if p['mode'] in known_modes else None
        measured.append(q)
    return {'schema_version':1,'kind':'mobile-acceptance-report','code_commit':probes[0]['code_commit'],'code_tree':probes[0]['code_tree'],
            'device_acceptance':{'Android':'Pending','iOS':'Pending'},'profile_goals':profiles,'measured_software_probes':measured,
            'unmeasured_modes':[m['mode'] for m in profiles['modes'] if m['mode'] not in {p['mode'] for p in probes}],
            'limits':['Not a full game regression summary. Existing NUnit/Unity results remain separately required.',
                      'No Android/iOS touch, IL2CPP/Burst build, fallback, thermal, battery or sustained frame acceptance.',
                      'No physical GPU traffic can be inferred from payload, API upload or allocation counts.',
                      'Goals do not override runtime capacities, GC gates, fixture windows or existing thresholds.']}


def markdown(r):
    lines = ['# Stage G bounded mobile-budget evidence', '', f"Commit `{r['code_commit']}`, tree `{r['code_tree']}`.",
             '', 'Android: **Pending**. iOS: **Pending**. Measurements below are simulation-only software probes.',
             'Profiles are configurable goals. No result below demonstrates a physical-device budget.', '']
    for p in r['measured_software_probes']:
        t=p['tick_statistics']; a=p['allocation']; s=p['pipeline']
        lines += [f"## {p['mode']} / {p['configuration']}",f"- Session: {p['session']}; seed {p['seed']}; {p['tick_rate']} Hz; orientation intent {p['orientation_intent']} (no rendered viewport)",
                  f"- Actual backend: {p['backend']}; runtime: {p['runtime']}; platform: {p['platform']}",
                  f"- Timing: {p['timing_source']}; ticks [{p['first_tick']}, {p['end_tick_exclusive']}); warmup {p['warmup_ticks']}",
                  f"- Fixed-step timing p50/p95/worst: {t['p50_ms']:.6f}/{t['p95_ms']:.6f}/{t['worst_ms']:.6f} ms ({t['samples']} samples, nearest-rank; not frames)",
                  f"- PipelineStats end EMA: schedule {s['schedule_ema_ms']:.6f}, sync wait {s['sync_wait_ema_ms']:.6f}, wall {s['tick_wall_ema_ms']:.6f} ms. {s['source']}",
                  f"- Allocation: {a['value']} {a['metric']}, thread {a['thread_id']}, ticks [{a['first_tick']}, {a['end_tick_exclusive']}); process gen0 collections {a['gen0_process_collections']}",
                  f"- Retained/empty controls before: {a['before_retained']}/{a['before_empty']}; after: {a['after_retained']}/{a['after_empty']}; controls excluded from measured window",
                  f"- Create failures {p['world_create_failures']}; destroy overflow {p['destroy_queue_overflow']}. {p['counter_window']}",
                  f"- Default factory capacities match: {p['matches_default_factory_capacities']}; custom fixture capacity must not be presented as default-load evidence."]
        lines += [f"  - {x['name']}: capacity {x['capacity']}, completed-tick peak {x['peak_after_tick']}, final {x['final_count']}" for x in p['tables']]
        lines += [f"- Quality scope: {p['presentation_scope']}", f"- Views: {p['unsupported_views']}", '']
    lines += ['## Unknowns (not zeros)']+[f'- {k}: {v}' for k,v in UNKNOWN.items()]
    lines += ['', '## Per-mode profile goals', 'Each mode has the four declared quality levels and Android/iOS candidate APIs. All physical gates remain Pending.']
    for m in r['profile_goals']['modes']:
        lines += [f"- {m['mode']}: {m['orientation_goal']}, {m['active_fps_goal']} FPS goal; capacities {json.dumps(m['table_capacities'],sort_keys=True)}. {m['quality_scope']}"]
        if m['simulation_quality_exception']: lines += [f"  - Compatibility exception: {m['simulation_quality_exception']}"]
    lines += ['', 'Unmeasured modes: '+', '.join(r['unmeasured_modes']), '', '## Limits']+['- '+s for s in r['limits']]
    return '\n'.join(lines)+'\n'


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profiles',type=Path,default=Path(__file__).with_name('mobile_profiles.json'))
    parser.add_argument('--evidence',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True,help='Output prefix for .json and .md')
    parser.add_argument('--toolchain',type=Path,required=True,help='Retained verify_inputs.py context')
    args=parser.parse_args(); files=sorted(args.evidence.glob('*.json')); require(1<=len(files)<=256,'evidence file bound')
    probes=[load(p) for p in files]; result=report(load(args.profiles),probes)
    from verify_inputs import validate_context
    result['toolchain'] = validate_context(load(args.toolchain))
    require(result['toolchain']['code_commit'] == result['code_commit'] and result['toolchain']['code_tree'] == result['code_tree'], 'toolchain/probe source identity mismatch')
    result['evidence_files']=[{'name':p.name,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in files]
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.with_suffix('.json').write_text(json.dumps(result,indent=2,allow_nan=False)+'\n')
    args.output.with_suffix('.md').write_text(markdown(result))
    print(f'Validated {len(files)} software probes. Android Pending; iOS Pending.')

if __name__=='__main__':
    main()
