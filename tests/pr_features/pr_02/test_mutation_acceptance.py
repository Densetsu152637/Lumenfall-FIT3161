"""Additional PR02 gate mutations; run against an isolated PR02 checkout."""
import importlib.util
import os
from pathlib import Path
import sys
import tempfile
import unittest

REPO = Path(os.environ.get('LUMENFALL_TARGET_REPO', Path.cwd()))
sys.path.insert(0, str(REPO / 'scripts'))
spec = importlib.util.spec_from_file_location('pr02_fixture', REPO / 'scripts/tests/test_validate_authored_content.py')
fixture_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture_module)
ContentFixture = fixture_module.ContentFixture


class Pr02MutationAcceptanceTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.fixture = ContentFixture(Path(temporary.name))
        self.assertEqual([], self.fixture.save())

    def test_matching_source_and_baked_invalid_glyph_still_fails(self):
        self.fixture.rows = '....\n.?..\n....\n....\n'
        errors = self.fixture.save()
        self.assertTrue(any('unsupported tile glyph' in error for error in errors), errors)

    def test_combined_independent_faults_are_all_reported(self):
        self.fixture.rows = 'W...\n....\n....\n....\n'
        self.fixture.registration['tags'] = ['unexpected']
        self.fixture.puzzles[0]['completionEventId'] = 'absent'
        errors = self.fixture.save()
        for fragment in ('spawn: blocked tile', 'registration: source/baked tags mismatch', 'unknown completionEventId absent'):
            self.assertTrue(any(fragment in error for error in errors), (fragment, errors))

    def test_repairing_baked_parity_does_not_hide_invalid_spawn(self):
        self.fixture.map['spawn'] = dict(x=4, y=0)
        errors = self.fixture.save()
        self.assertTrue(any('source/baked spawn mismatch' in error for error in errors), errors)
        self.fixture.area['definition']['spawn'] = dict(x=4, y=0)
        errors = self.fixture.save()
        self.assertFalse(any('source/baked spawn mismatch' in error for error in errors), errors)
        self.assertTrue(any('spawn: cell/footprint outside' in error for error in errors), errors)


if __name__ == '__main__':
    unittest.main()
