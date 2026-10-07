import copy
import json
from pathlib import Path
import tempfile
import unittest
import mobile_acceptance_report as report


def probe():
    # Synthetic validator input only. Never delivered as a real benchmark or phone measurement.
    return {'schema_version':1,'kind':'simulation-probe','mode':'external.courier-test-only','configuration':'schema-test',
            'orientation_intent':'none','session':'synthetic-test','backend':'simulation-only/no-renderer','runtime':'dotnet-unity-stubs',
            'platform':'synthetic schema test, not measured','started_utc':'2026-10-07T00:00:00Z','code_commit':'a'*40,'code_tree':'b'*40,
            'seed':123,'tick_rate':30,'warmup_ticks':16,'first_tick':16,'end_tick_exclusive':80,
            'timing_source':'Synthetic unit test only','tick_ms':[1.,2.,3.,4.]*16,
            'pipeline':{'source':'Synthetic EMA schema test','completed_ticks':80,'schedule_ema_ms':1.,'sync_wait_ema_ms':0.,'tick_wall_ema_ms':1.},
            'allocation':{'source':'Synthetic calibration schema test','metric':'ManagedBytes','thread_id':1,'first_tick':80,'end_tick_exclusive':144,
                          'value':0,'gen0_process_collections':0,'before_retained':32768,'before_empty':0,'after_retained':32768,'after_empty':0},
            'tables':[{'name':'Parcel','capacity':8,'peak_after_tick':1,'final_count':1}],
            'world_create_failures':0,'destroy_queue_overflow':0,'counter_window':'Synthetic bounded window',
            'presentation_scope':'Unsupported: no renderer','unsupported_views':'Unsupported: no views'}


class ReportTests(unittest.TestCase):
    def setUp(self):
        self.profiles = report.load(Path(__file__).with_name('mobile_profiles.json'))

    def reject(self, change):
        p=probe(); change(p)
        with self.assertRaises(ValueError): report.validate_probe(p)

    def test_all_nineteen_actual_profiles_and_four_tiers(self):
        report.validate_profiles(self.profiles)
        self.assertEqual(19,len(self.profiles['modes']))
        self.assertEqual([0,1,2,3],[t['level'] for t in self.profiles['quality_tiers']])

    def test_profile_capacities_are_exact_stage_a_inventory(self):
        inventory=report.load(Path(__file__).resolve().parents[2]/'Docs/validation/FoundationCompatibilityInventory-20261007.json')
        expected={c['audit_id']:{t[0]:t[1] for t in c['tables']} for c in inventory['compositions']}
        self.assertEqual(expected,{m['mode']:m['table_capacities'] for m in self.profiles['modes']})

    def test_unknown_is_null_not_zero_and_mobile_is_pending(self):
        r=report.report(self.profiles,[probe()])
        for m in r['measured_software_probes'][0]['unknown_measurements'].values(): self.assertIsNone(m['value'])
        self.assertEqual({'Android':'Pending','iOS':'Pending'},r['device_acceptance'])
        self.assertIn('Unknowns (not zeros)',report.markdown(r))
        self.assertEqual(19,len(r['unmeasured_modes']))

    def test_percentiles_are_nearest_rank_raw_samples_not_ema(self):
        s=report.percentiles([9,2,1,4]); self.assertEqual(2,s['p50_ms']); self.assertEqual(9,s['p95_ms']); self.assertEqual(9,s['worst_ms'])

    def test_samples_cannot_be_missing_or_unbounded(self):
        for values in [[],[0]*63,[0]*65,[0]*4097,None]: self.reject(lambda p: p.update(tick_ms=values))

    def test_samples_cannot_be_negative_nonfinite_bool_or_unknown_zero(self):
        for v in [-1,float('nan'),float('inf'),True,None]: self.reject(lambda p: p['tick_ms'].__setitem__(0,v))

    def test_actual_backend_and_scope_are_required(self):
        self.reject(lambda p:p.update(backend='Metal'))
        self.reject(lambda p:p.update(runtime='Android-physical'))
        self.reject(lambda p:p.update(platform=''))

    def test_allocation_controls_before_and_after_are_mandatory(self):
        for key,val in [('before_retained',0),('after_retained',31),('before_empty',1),('after_empty',1),('value',-1)]:
            self.reject(lambda p:p['allocation'].__setitem__(key,val))

    def test_allocation_units_cannot_convert_samples_to_bytes(self):
        self.reject(lambda p:p['allocation'].update(metric='AllocationSamples'))
        p=probe();p['runtime']='unity-native-editmode';p['allocation'].update(metric='AllocationSamples',before_retained=32,after_retained=32)
        report.validate_probe(p)
        p['allocation']['after_retained']=31
        with self.assertRaises(ValueError): report.validate_probe(p)

    def test_allocation_and_timing_windows_must_be_exact(self):
        self.reject(lambda p:p.update(end_tick_exclusive=21))
        self.reject(lambda p:p['allocation'].update(first_tick=19))
        self.reject(lambda p:p['allocation'].update(end_tick_exclusive=81))
        self.reject(lambda p:p.update(first_tick=0,end_tick_exclusive=64))
        self.reject(lambda p:p.update(warmup_ticks=1))
        self.reject(lambda p:p['pipeline'].update(completed_ticks=80.5))

    def test_capacity_bounds_and_unique_names(self):
        self.reject(lambda p:p['tables'][0].update(peak_after_tick=9))
        self.reject(lambda p:p['tables'][0].update(final_count=9))
        self.reject(lambda p:p['tables'].append(copy.deepcopy(p['tables'][0])))

    def test_exact_commit_tree_and_single_checkpoint(self):
        self.reject(lambda p:p.update(code_commit='HEAD'))
        a=probe();b=probe();b.update(session='different',code_tree='c'*40)
        with self.assertRaises(ValueError): report.report(self.profiles,[a,b])

    def test_unsupported_fields_and_device_claims_are_rejected(self):
        self.reject(lambda p:p.update(device_acceptance='Passed'))
        self.reject(lambda p:p['pipeline'].update(worker_cpu_ms=0))
        self.reject(lambda p:p.update(unsupported_views=''))

    def test_duplicate_and_unknown_probe_are_rejected(self):
        with self.assertRaises(ValueError): report.report(self.profiles,[probe(),probe()])
        p=probe();p['mode']='invented-game'
        with self.assertRaises(ValueError): report.report(self.profiles,[p])

    def test_json_loader_rejects_duplicate_keys_nonfinite_and_oversize(self):
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)/'bad.json'
            for s in ['{"a":1,"a":2}','{"a":NaN}',' '* (report.MAX_BYTES+1)]:
                p.write_text(s)
                with self.assertRaises(ValueError): report.load(p)

    def test_profiles_cannot_sign_mobile_or_silently_remove_axes(self):
        p=copy.deepcopy(self.profiles);p['platform_targets']['Android']['status']='Passed'
        with self.assertRaises(ValueError): report.validate_profiles(p)
        p=copy.deepcopy(self.profiles);del p['modes'][0]['targets']['gpu_ms']
        with self.assertRaises(ValueError): report.validate_profiles(p)

    def test_targets_are_configurable_but_remain_goals(self):
        p=copy.deepcopy(self.profiles);p['modes'][0]['targets']['native_live_bytes']['value']=32*1024*1024
        p['platform_targets']['Android']['device_model']='Future lab target'
        report.validate_profiles(p)
        self.assertEqual('Pending',report.report(p,[probe()])['device_acceptance']['Android'])

    def test_custom_capacity_cannot_impersonate_default_load(self):
        p=probe();p.update(mode='shooter.default',orientation_intent='portrait')
        r=report.report(self.profiles,[p]);self.assertFalse(r['measured_software_probes'][0]['matches_default_factory_capacities'])

    def test_payload_api_and_physical_traffic_remain_separate(self):
        r=report.report(self.profiles,[probe()]);u=r['measured_software_probes'][0]['unknown_measurements']
        for key in ['upload_payload_bytes','upload_api_bytes','physical_gpu_traffic_bytes']: self.assertIn(key,u)


if __name__=='__main__': unittest.main()
