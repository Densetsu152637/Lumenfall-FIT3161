param(
    [string]$UnityEditorPath = 'C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor',
    [string]$ReferenceProjectPath = ''
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ReferenceProjectPath)) { $ReferenceProjectPath = Join-Path $root 'Lumenfall' }
$outDir = Join-Path $root 'Lumenfall/Temp/production-ui-policy-tests'
New-Item -ItemType Directory -Force $outDir | Out-Null
$dotnet = Join-Path $UnityEditorPath 'Data/NetCoreRuntime/dotnet.exe'
$csc = Join-Path $UnityEditorPath 'Data/DotNetSdkRoslyn/csc.dll'
$runtime = Get-ChildItem (Join-Path $UnityEditorPath 'Data/NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$nunit = Get-ChildItem (Join-Path $ReferenceProjectPath 'Library/PackageCache/com.unity.ext.nunit*') -Directory | ForEach-Object { Join-Path $_.FullName 'net40/unity-custom/nunit.framework.dll' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($null -eq $runtime -or $null -eq $nunit) { throw 'Installed Unity .NET runtime and existing NUnit package are required. No package is installed by this script.' }
Copy-Item -LiteralPath $nunit -Destination (Join-Path $outDir 'nunit.framework.dll') -Force
$runner = @"
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
public static class Runner
{
    public static int Main()
    {
        var suite = new Lumenfall.Tests.GameEvents.ProductionUiPolicyTests();
        int passed = 0;
        foreach (var method in suite.GetType().GetMethods().Where(m => m.GetCustomAttribute<TestAttribute>() != null).OrderBy(m => m.Name))
        {
            try { method.Invoke(suite, null); Console.WriteLine("PASS " + method.Name); passed++; }
            catch (TargetInvocationException error) { Console.Error.WriteLine(error.InnerException); return 1; }
        }
        Console.WriteLine(passed + " production UI policy tests passed (plain C#, no Unity lifecycle).");
        return passed == 4 ? 0 : 1;
    }
}
"@
$utf8 = New-Object Text.UTF8Encoding($false)
$runnerPath = Join-Path $outDir 'Runner.cs'
[IO.File]::WriteAllText($runnerPath, $runner, $utf8)
$lines = [Collections.Generic.List[string]]::new()
@('-nologo','-nostdlib+','-target:exe','-langversion:9.0',('-out:"' + (Join-Path $outDir 'PolicyTests.dll') + '"')) | ForEach-Object { $lines.Add($_) }
Get-ChildItem $runtime.FullName -File -Filter '*.dll' | ForEach-Object {
    try { $null = [Reflection.AssemblyName]::GetAssemblyName($_.FullName); $lines.Add('-r:"' + $_.FullName + '"') }
    catch [System.BadImageFormatException] { }
}
$lines.Add('-r:"' + (Join-Path $outDir 'nunit.framework.dll') + '"')
@($runnerPath,(Join-Path $root 'Lumenfall/Assets/_Project/Systems/GameEvents/OverlayUiPolicy.cs'),(Join-Path $root 'Lumenfall/Assets/_Project/Systems/GameEvents/Tests/EditMode/ProductionUiPolicyTests.cs')) | ForEach-Object { $lines.Add('"' + $_ + '"') }
$rsp = Join-Path $outDir 'policy-tests.rsp'
[IO.File]::WriteAllLines($rsp, $lines, $utf8)
& $dotnet $csc ('@' + $rsp)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$config = @{ runtimeOptions = @{ tfm = ('net' + ([version]$runtime.Name).Major + '.0'); framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $outDir 'PolicyTests.runtimeconfig.json'), $config, $utf8)
& $dotnet (Join-Path $outDir 'PolicyTests.dll')
exit $LASTEXITCODE
