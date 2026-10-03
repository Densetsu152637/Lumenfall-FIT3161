import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'release_acceptance.py'
spec = importlib.util.spec_from_file_location('release_acceptance', SCRIPT)
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseAcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.matrix = release.load_matrix()
        self.evidence = release.template(self.matrix, 'Windows x64', 'a'*40, 'test-build', 'test-slot')

    def complete(self):
        self.evidence['environment'].update(tester='Fixture tester', tested_at_utc='2026-10-03T07:00:00Z', device_os='Fixture device/OS')
        for c in self.evidence['checks']:
            c.update(status='pass', evidence='fixture capture: observed expected result')
        self.evidence['performance'] = {
            'hardware': 'Fixture CPU/GPU/RAM', 'scenario': 'Fixture cold/warm route',
            'captures': ['fixtures/disabled.capture', 'fixtures/enabled.capture'],
            'metrics': [{'name': name, 'diagnostics': mode, 'samples': 10, 'mean_ms': 2,
                         'max_ms': 4, 'budget_mean_ms': 3, 'budget_max_ms': 5,
                         'budget_approval': 'Fixture-approved-budget'}
                        for name in self.matrix['required_metrics'] for mode in ('disabled','enabled')]}
        return self.evidence

    def test_template_is_incomplete_and_preserves_identifiers(self):
        errors = release.validate(self.evidence, self.matrix)
        self.assertIn('first-light: pending', errors)
        self.assertIn('environment.tester is required', errors)
        self.assertEqual('test-slot', self.evidence['environment']['slot_id'])
        self.assertEqual(len(self.matrix['checks']), len(self.evidence['checks']))

    def test_complete_recorded_evidence_passes(self):
        self.assertEqual([], release.validate(self.complete(), self.matrix))

    def test_all_six_quest_checks_are_required(self):
        self.complete()
        required = ('first-light', 'string-them-together', 'out-of-alignment', 'cold-feet', 'patch-notes', 'something-borrowed')
        for check_id in required:
            evidence = copy.deepcopy(self.evidence)
            evidence['checks'] = [c for c in evidence['checks'] if c['id'] != check_id]
            self.assertIn(check_id+': missing check', release.validate(evidence, self.matrix))

    def test_missing_duplicate_unknown_and_failed_checks_rejected(self):
        self.complete()
        self.evidence['checks'][0]['status'] = 'blocked'
        self.evidence['checks'].append(copy.deepcopy(self.evidence['checks'][1]))
        self.evidence['checks'].append({'id':'invented', 'status':'pass', 'evidence':'fixture'})
        errors = release.validate(self.evidence, self.matrix)
        self.assertIn('startup-route: blocked', errors)
        self.assertIn('Duplicate check chapter-portals', errors)
        self.assertIn("Unknown check 'invented'", errors)

    def test_pass_without_evidence_rejected(self):
        self.complete()['checks'][0]['evidence'] = ' '
        self.assertTrue(any('pass requires evidence' in e for e in release.validate(self.evidence, self.matrix)))

    def test_short_sha_wrong_unity_and_local_timestamp_rejected(self):
        self.complete()['environment'].update(commit='abcdef', unity_version='6000.3', tested_at_utc='2026-10-03T07:00:00')
        errors = release.validate(self.evidence, self.matrix)
        self.assertTrue(any('40-character' in e for e in errors))
        self.assertTrue(any('Unity must' in e for e in errors))
        self.assertTrue(any('UTC timezone' in e for e in errors))

    def test_missing_metrics_and_budget_exceedance_rejected(self):
        self.complete()
        metric = self.evidence['performance']['metrics'].pop()
        self.evidence['performance']['metrics'][0].update(mean_ms=6, max_ms=8)
        errors = release.validate(self.evidence, self.matrix)
        self.assertTrue(any('missing performance metric' in e for e in errors))
        self.assertTrue(any('exceeds approved budget' in e for e in errors))
        self.assertEqual('enabled', metric['diagnostics'])

    def test_invalid_performance_numbers_and_unapproved_budget_rejected(self):
        self.complete()
        self.evidence['performance']['metrics'][0].update(samples=True, mean_ms=float('nan'), max_ms=-1, budget_mean_ms=0, budget_approval='')
        errors = release.validate(self.evidence, self.matrix)
        self.assertTrue(any('positive sample count' in e for e in errors))
        self.assertTrue(any('invalid mean_ms' in e for e in errors))
        self.assertTrue(any('budget_approval' in e for e in errors))

    def test_cli_template_nonzero_check_and_no_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            evidence = Path(temp)/'evidence.json'
            result = subprocess.run([sys.executable, str(SCRIPT), 'init', '--platform', 'Windows x64', '--commit', 'a'*40, '--build-id', 'fixture', '--slot-id', 'fixture-slot', '--output', str(evidence)], capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stderr)
            original = evidence.read_text()
            result = subprocess.run([sys.executable, str(SCRIPT), 'check', str(evidence), '--report', str(Path(temp)/'report.md')], capture_output=True, text=True)
            self.assertEqual(1, result.returncode)
            self.assertIn('INCOMPLETE / FAILED', (Path(temp)/'report.md').read_text())
            result = subprocess.run([sys.executable, str(SCRIPT), 'init', '--platform', 'Windows x64', '--commit', 'a'*40, '--build-id', 'fixture', '--slot-id', 'fixture-slot', '--output', str(evidence)], capture_output=True, text=True)
            self.assertEqual(2, result.returncode)
            self.assertEqual(original, evidence.read_text())

    def test_cli_malformed_json_and_complete_evidence_exit_codes(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)/'evidence.json'
            path.write_text('{invalid')
            result = subprocess.run([sys.executable, str(SCRIPT), 'check', str(path)], capture_output=True, text=True)
            self.assertEqual(2, result.returncode)
            path.write_text(json.dumps(self.complete()))
            result = subprocess.run([sys.executable, str(SCRIPT), 'check', str(path)], capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stderr)

    def test_wrong_schema_and_malformed_collections_rejected(self):
        self.assertEqual(['Evidence schema_version must be 1'], release.validate([], self.matrix))
        self.evidence['checks'] = {}
        self.assertIn('checks must be an array of objects', release.validate(self.evidence, self.matrix))
        self.evidence = release.template(self.matrix, 'Windows x64', 'a'*40, 'test-build', 'test-slot')
        self.complete()['performance']['metrics'] = {}
        self.assertIn('performance.metrics must be an array of objects', release.validate(self.evidence, self.matrix))


if __name__ == '__main__':
    unittest.main()
