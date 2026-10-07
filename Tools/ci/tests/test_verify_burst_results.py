import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('gate', Path(__file__).parents[1] / 'verify-approved-burst-results.py')
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class BurstEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for suite in ('editmode', 'playmode'):
            total = gate.MINIMUM_CASES[suite]
            skips = gate.ALLOWED_SKIPS[suite]
            xml = ET.Element('test-run', result='Skipped:Ignored' if suite == 'editmode' else 'Passed',
                             total=str(total), passed=str(total - len(skips)), failed='0', skipped=str(len(skips)))
            for index in range(total - len(skips)):
                ET.SubElement(xml, 'test-case', fullname='passing_' + str(index), result='Passed')
            for name, label in skips.items():
                ET.SubElement(xml, 'test-case', fullname=name, result='Skipped', label=label)
            ET.ElementTree(xml).write(self.root / (suite + '-results.xml'))
        (self.root / 'perf-physics-backend.txt').write_text(
            'EditorPrefs BurstCompilation: exists=True, value=True\n'
            'BurstCompiler.IsEnabled=True; Options.IsEnabled=True; EnableBurstCompilation=True\n'
            'JobsUtility.JobCompilerEnabled=True\n'
            'direct managed Execute=0; Run Burst=1; Schedule Burst=1\n'
            'actual physics first step Burst=True\n')
        (self.root / 'perf-physics.txt').write_text(
            '=== Physics2D: 600 bodies poured into a box ===\nstep ms mean 0.418 worst 0.618\n'
            'actual Burst execution: warmup 60/60 steps; measured 300/300 steps\n')

    def change_xml(self, action):
        path = self.root / 'editmode-results.xml'
        tree = ET.parse(path)
        action(tree.getroot())
        tree.write(path)

    def test_known_ignored_root_with_all_required_cases_passes(self):
        self.assertIn('passed=722', gate.verify(self.root))

    def test_hidden_failed_case_is_rejected_even_if_root_counters_claim_zero(self):
        self.change_xml(lambda root: root.find('test-case').set('result', 'Failed'))
        with self.assertRaises(AssertionError): gate.verify(self.root)

    def test_unexpected_skip_is_rejected(self):
        self.change_xml(lambda root: list(root)[-1].set('fullname', 'unexpected.skipped.test'))
        with self.assertRaises(AssertionError): gate.verify(self.root)

    def test_missing_cases_are_rejected(self):
        self.change_xml(lambda root: root.remove(root.find('test-case')))
        with self.assertRaises(AssertionError): gate.verify(self.root)

    def test_bad_counters_are_rejected(self):
        self.change_xml(lambda root: root.set('passed', '721'))
        with self.assertRaises(AssertionError): gate.verify(self.root)

    def test_fallback_and_missing_report_are_rejected(self):
        path = self.root / 'editmode-noburst-results.xml'
        path.write_text('')
        with self.assertRaises(AssertionError): gate.verify(self.root)
        path.unlink()
        (self.root / 'perf-physics-backend.txt').unlink()
        with self.assertRaises(FileNotFoundError): gate.verify(self.root)

    def test_managed_witness_is_rejected(self):
        path = self.root / 'perf-physics-backend.txt'
        path.write_text(path.read_text().replace('Run Burst=1', 'Run Burst=0'))
        with self.assertRaises(AssertionError): gate.verify(self.root)

    def test_managed_steps_are_rejected(self):
        path = self.root / 'perf-physics.txt'
        path.write_text(path.read_text().replace('warmup 60/60', 'warmup 0/60'))
        with self.assertRaises(AssertionError): gate.verify(self.root)

    def test_original_budget_and_finite_mean_are_required(self):
        path = self.root / 'perf-physics.txt'
        source = path.read_text()
        for mean in ('4.000', 'nan', 'inf', '-1'):
            path.write_text(source.replace('0.418', mean))
            with self.assertRaises(AssertionError): gate.verify(self.root)


if __name__ == '__main__':
    unittest.main()
