import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parents[1]
POWERSHELL = shutil.which('pwsh') or shutil.which('powershell')


@unittest.skipUnless(POWERSHELL, 'PowerShell required for offline compiler portability fixtures')
class OfflineCompileTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='offline compile fixture ')
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.project = root / 'clean source/Lumenfall'
        self.reference = root / 'read only reference/Lumenfall'
        self.dag = self.reference / 'Library/Bee/artifacts/fresh.dag'
        self.dag.mkdir(parents=True)
        (self.dag / 'Dependency.ref.dll').write_bytes(b'dependency fixture')
        self.assemblies = [('Lumenfall.GameEvents', 'Systems/GameEvents', [], False), ('Lumenfall.Runtime', 'Systems', ['Lumenfall.GameEvents'], False), ('Lumenfall.GameEvents.Tests', 'Systems/GameEvents/Tests/EditMode', ['Lumenfall.GameEvents', 'Lumenfall.Runtime'], True), ('Lumenfall.World.PlayModeTests', 'Systems/DataDriven/Tests/PlayMode', ['Lumenfall.Runtime'], True), ('Lumenfall.Extra.Tests', 'Systems/Tests/Extra', ['Lumenfall.Runtime'], True)]
        for name, relative, references, test in self.assemblies:
            folder = self.project / 'Assets' / relative
            folder.mkdir(parents=True, exist_ok=True)
            definition = dict(name=name, references=references)
            if test:
                definition['optionalUnityReferences'] = ['TestAssemblies']
            (folder / (name + '.asmdef')).write_text(json.dumps(definition), encoding='utf-8')
            (folder / (name + '.cs')).write_text('class Fixture {}', encoding='utf-8')
            (self.dag / (name + '.rsp')).write_text('\n'.join(['-define:UNITY_EDITOR', '-r:"Library/Bee/artifacts/fresh.dag/Dependency.ref.dll"', '-r:"Library/Bee/artifacts/fresh.dag/Lumenfall.Stale.New.ref.dll"', '-r:"Library\\Bee\\artifacts\\fresh.dag\\Lumenfall.Stale.Windows.ref.dll"', '-r:"Library/Bee/artifacts/fresh.dag/Assembly-CSharp.ref.dll"', '"Assets/old-source.cs"']), encoding='utf-8')
        self.before = {p.relative_to(self.reference): (p.read_bytes(), p.stat().st_mtime_ns) for p in self.reference.rglob('*') if p.is_file()}
    def run_script(self, name, *args):
        return subprocess.run([POWERSHELL, '-NoProfile', '-File', str(SCRIPTS / name), '-ReferenceProject', str(self.reference), '-ProjectPath', str(self.project), '-PrepareOnly', *args], capture_output=True, text=True, cwd=self.temp.name)
    def test_clean_project_all_asmdefs_stale_refs_excluded_reference_read_only(self):
        runtime = self.run_script('compile_runtime.ps1')
        self.assertEqual(0, runtime.returncode, runtime.stdout + runtime.stderr)
        tests = self.run_script('compile_editmode_tests.ps1')
        self.assertEqual(0, tests.returncode, tests.stdout + tests.stderr)
        self.assertFalse((self.project / 'Library').exists())
        out = self.project / 'Temp/codex-compile'
        self.assertEqual({name + '.rsp' for name, *_ in self.assemblies}, {p.name for p in out.glob('*.rsp')})
        for rsp in out.glob('*.rsp'):
            text = rsp.read_text()
            self.assertNotIn('Lumenfall.Stale', text)
            self.assertNotIn('Assembly-CSharp', text)
            self.assertNotIn('old-source', text)
            self.assertIn(str(self.dag / 'Dependency.ref.dll'), text)
        runtime_rsp = (out / 'Lumenfall.Runtime.rsp').read_text()
        self.assertNotIn('Tests/EditMode', runtime_rsp.replace('\\', '/'))
        self.assertNotIn('Tests/PlayMode', runtime_rsp.replace('\\', '/'))
        self.assertNotIn('GameEvents.cs"', runtime_rsp)
        self.assertIn(str(out / 'Lumenfall.GameEvents.dll'), runtime_rsp)
        after = {p.relative_to(self.reference): (p.read_bytes(), p.stat().st_mtime_ns) for p in self.reference.rglob('*') if p.is_file()}
        self.assertEqual(self.before, after)
    def test_missing_dependency_returns_failure(self):
        (self.dag / 'Dependency.ref.dll').unlink()
        result = self.run_script('compile_runtime.ps1')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Missing imported dependency', result.stderr)
    def test_new_test_asmdef_without_reference_template_returns_failure(self):
        (self.dag / 'Lumenfall.Extra.Tests.rsp').unlink()
        result = self.run_script('compile_editmode_tests.ps1')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Expected exactly one imported reference template', result.stderr)
    def test_unsupported_platform_constraints_return_failure(self):
        path = self.project / 'Assets/Systems/Lumenfall.Runtime.asmdef'
        value = json.loads(path.read_text())
        value['includePlatforms'] = ['Android']
        path.write_text(json.dumps(value))
        result = self.run_script('compile_runtime.ps1')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Unsupported offline asmdef constraints', result.stderr)

    def test_reference_project_cannot_receive_compile_outputs(self):
        result = subprocess.run([POWERSHELL, '-NoProfile', '-File', str(SCRIPTS / 'compile_runtime.ps1'), '-ReferenceProject', str(self.reference), '-ProjectPath', str(self.reference), '-PrepareOnly'], capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('outside the read-only ReferenceProject', result.stderr)
        self.assertFalse((self.reference / 'Temp').exists())


if __name__ == '__main__':
    unittest.main()
