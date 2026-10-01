from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from validate_unity_results import validate_results


class UnityResultsValidatorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        self.assembly = "Lumenfall.GameEvents.Tests"

    def tearDown(self):
        self.temp.cleanup()

    def report(self, cases='<test-case name="quest" result="Passed"/>',
               result="Passed", assembly=None):
        (self.directory / "EditMode-results.xml").write_text(
            f'<test-run result="{result}"><test-suite type="Assembly" '
            f'name="{assembly or self.assembly}.dll">{cases}</test-suite></test-run>',
            encoding="utf-8",
        )

    def test_passing_first_party_tests(self):
        self.report()
        self.assertEqual((1, []), validate_results(self.directory, self.assembly))

    def test_missing_results_fail(self):
        self.assertTrue(validate_results(self.directory, self.assembly)[1])

    def test_assembly_name_without_dll_suffix(self):
        self.report()
        path = self.directory / "EditMode-results.xml"
        path.write_text(path.read_text().replace(".dll", ""), encoding="utf-8")
        self.assertEqual((1, []), validate_results(self.directory, self.assembly))

    def test_empty_run_fails(self):
        self.report(cases="")
        self.assertTrue(validate_results(self.directory, self.assembly)[1])

    def test_unrelated_assembly_cannot_satisfy_suite(self):
        self.report(assembly="ThirdParty.Tests")
        self.assertTrue(validate_results(self.directory, self.assembly)[1])

    def test_skipped_suite_fails(self):
        self.report(cases='<test-case name="quest" result="Skipped"/>')
        self.assertTrue(validate_results(self.directory, self.assembly)[1])

    def test_failed_case_fails_even_if_root_claims_success(self):
        self.report(cases='<test-case result="Passed"/><test-case result="Failed"/>')
        self.assertTrue(validate_results(self.directory, self.assembly)[1])

    def test_failed_run_with_passing_case_fails(self):
        self.report(result="Failed")
        self.assertTrue(validate_results(self.directory, self.assembly)[1])

    def test_malformed_xml_fails(self):
        (self.directory / "results.xml").write_text("<broken", encoding="utf-8")
        self.assertTrue(validate_results(self.directory, self.assembly)[1])

    def test_coverage_xml_is_ignored(self):
        self.report()
        (self.directory / "coverage.xml").write_text("<coverage/>", encoding="utf-8")
        self.assertEqual((1, []), validate_results(self.directory, self.assembly))

    def test_nested_reports_and_windows_assembly_paths(self):
        self.report(assembly="C:\\Unity\\Lumenfall.GameEvents.Tests")
        nested = self.directory / "nested"
        nested.mkdir()
        (self.directory / "EditMode-results.xml").rename(nested / "EditMode-results.xml")
        self.assertEqual((1, []), validate_results(self.directory, self.assembly))


if __name__ == "__main__":
    unittest.main()
