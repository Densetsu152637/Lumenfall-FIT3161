param(
    [Parameter(Mandatory = $true)][string]$ReferenceProject,
    [string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor',
    [string]$NUnitPath
)
# Executes only the pure collector NUnit fixture on .NET 8; never starts Unity.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$referenceRoot = (Resolve-Path -LiteralPath $ReferenceProject).Path
$dotnet = Join-Path $UnityEditorPath 'Data/NetCoreRuntime/dotnet.exe'
$csc = Join-Path $UnityEditorPath 'Data/DotNetSdkRoslyn/csc.dll'
$frameworkRoot = Join-Path $UnityEditorPath 'Data/NetCoreRuntime/shared/Microsoft.NETCore.App'
$framework = Get-ChildItem -LiteralPath $frameworkRoot -Directory | Where-Object { $_.Name -match '^8\.' } | Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
if (!$framework -or !(Test-Path -LiteralPath $dotnet) -or !(Test-Path -LiteralPath $csc)) { throw 'Installed Unity Roslyn and .NET 8 runtime are required.' }
if (!$NUnitPath) {
    $package = Get-ChildItem -LiteralPath (Join-Path $referenceRoot 'Library/PackageCache') -Directory -Filter 'com.unity.ext.nunit@*' | Sort-Object Name | Select-Object -First 1
    if (!$package) { throw 'Reference project has no imported Unity NUnit package. Specify -NUnitPath.' }
    $NUnitPath = Join-Path $package.FullName 'net40/unity-custom/nunit.framework.dll'
}
$NUnitPath = (Resolve-Path -LiteralPath $NUnitPath).Path
$outDir = Join-Path $root 'Lumenfall/Temp/diagnostics-validation'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$assembly = Join-Path $outDir 'CollectorHarness.dll'
$rsp = Join-Path $outDir 'CollectorHarness.rsp'
$lines = [System.Collections.Generic.List[string]]::new()
@('-nologo', '-nostdlib+', '-target:exe', '-langversion:9.0', '-deterministic', ('-out:"' + $assembly + '"'), ('-r:"' + $NUnitPath + '"')) | ForEach-Object { $lines.Add($_) }
Get-ChildItem -LiteralPath $framework.FullName -File -Filter '*.dll' | Where-Object { $_.Name -match '^System\..*\.dll$|^(mscorlib|netstandard)\.dll$' -and $_.Name -notmatch '\.Native\.' } | ForEach-Object { $lines.Add('-r:"' + $_.FullName + '"') }
@('Lumenfall/Assets/_Project/Systems/Diagnostics/PerformanceDiagnostics.cs', 'Lumenfall/Assets/_Project/Systems/GameEvents/Tests/EditMode/PerformanceDiagnosticsTests.cs', 'scripts/PerformanceCollectorHarness.cs') | ForEach-Object { $lines.Add('"' + (Join-Path $root $_) + '"') }
[IO.File]::WriteAllLines($rsp, $lines, (New-Object Text.UTF8Encoding($false)))
& $dotnet $csc ('@' + $rsp)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath $NUnitPath -Destination (Join-Path $outDir 'nunit.framework.dll') -Force
$config = @{ runtimeOptions = @{ tfm = 'net8.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $framework.Name } } } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $outDir 'CollectorHarness.runtimeconfig.json'), $config, (New-Object Text.UTF8Encoding($false)))
& $dotnet $assembly | Tee-Object -FilePath (Join-Path $outDir 'collector-tests.log')
exit $LASTEXITCODE
