param(
    [Parameter(Mandatory=$true)][string]$ReferenceProject,
    [string]$UnityEditorPath = 'C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'Lumenfall'
$reference = (Resolve-Path $ReferenceProject).Path
$dag = Join-Path $reference 'Library/Bee/artifacts/1900b0aE.dag'
$outDir = Join-Path $project 'Temp/accessibility-validation'
New-Item -ItemType Directory -Force $outDir | Out-Null
$dotnet = Join-Path $UnityEditorPath 'Data/NetCoreRuntime/dotnet.exe'
$csc = Join-Path $UnityEditorPath 'Data/DotNetSdkRoslyn/csc.dll'

# Reuse the repository compiler's source partitions, but never use stale first-party references.
$runtimeScript = Get-Content -Raw (Join-Path $PSScriptRoot 'compile_runtime.ps1')
$runtimeScript = $runtimeScript.Replace("(Resolve-Path (Join-Path `$PSScriptRoot '..')).Path", "'$($root.Replace("'", "''"))'")
$runtimeScript = $runtimeScript.Replace("Join-Path `$project 'Library/Bee/artifacts/1900b0aE.dag'", "'$($dag.Replace("'", "''"))'")
$runtimeScript = $runtimeScript.Replace("'Assembly-CSharp|Lumenfall.GameEvents|GameEvents.Tests|Unity.InputSystem'", "'^Assembly-CSharp|^Lumenfall[.]|^Unity[.]InputSystem'")
$runtimeScript = $runtimeScript.Replace("'Temp/codex-compile'", "'Temp/accessibility-validation'")
$runtimePath = Join-Path $outDir 'compile_runtime.ps1'
[IO.File]::WriteAllText($runtimePath, $runtimeScript)
& $runtimePath -UnityEditorPath $UnityEditorPath
if ($LASTEXITCODE -ne 0) { throw 'Runtime compilation failed.' }

$testPartitions = @(
    @{ Name='Lumenfall.GameEvents.Tests'; Sources='Assets/_Project/Systems/GameEvents/Tests/EditMode' },
    @{ Name='Lumenfall.World.PlayModeTests'; Sources='Assets/_Project/Systems/DataDriven/Tests/PlayMode' }
)
foreach ($partition in $testPartitions) {
    $template = Join-Path $dag ($partition.Name + '.rsp')
    if (!(Test-Path $template)) { throw "Missing test reference template: $template" }
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($line in Get-Content $template) {
        if ($line -match '^[-/]out:|^[-/]refout:|^[-/]additionalfile:|^"Assets/') { continue }
        if ($line -match '^[-/]r:.*(Lumenfall[.]|Assembly-CSharp)') { continue }
        if ($line -match '^[-/]r:"Library/') { $line = $line.Replace('-r:"Library/', '-r:"' + ($reference.Replace('\','/')) + '/Library/') }
        $lines.Add($line)
    }
    $lines.Add('-out:"' + (Join-Path $outDir ($partition.Name + '.dll')) + '"')
    $lines.Add('-r:"' + (Join-Path $outDir 'Lumenfall.GameEvents.dll') + '"')
    $lines.Add('-r:"' + (Join-Path $outDir 'Lumenfall.Runtime.dll') + '"')
    Get-ChildItem (Join-Path $project $partition.Sources) -Filter '*.cs' -File | ForEach-Object { $lines.Add('"' + $_.FullName + '"') }
    $rsp = Join-Path $outDir ($partition.Name + '.rsp')
    [IO.File]::WriteAllLines($rsp, $lines)
    & $dotnet $csc ('@' + $rsp)
    if ($LASTEXITCODE -ne 0) { throw "$($partition.Name) compilation failed." }
}

# Execute only settings tests that exercise pure C# behavior, never Unity native methods.
$nunitLine = Get-Content (Join-Path $dag 'Lumenfall.GameEvents.Tests.rsp') | Where-Object { $_ -match '^[-/]r:.*nunit.framework.dll' } | Select-Object -First 1
$nunitRelative = $nunitLine.Substring(4).TrimEnd('"')
$nunit = if ([IO.Path]::IsPathRooted($nunitRelative)) { $nunitRelative } else { Join-Path $reference $nunitRelative }
Copy-Item -LiteralPath $nunit -Destination (Join-Path $outDir 'nunit.framework.dll') -Force
$coreRuntime = Get-ChildItem (Join-Path $UnityEditorPath 'Data/NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$lines = [Collections.Generic.List[string]]::new()
@('-nostdlib+','-target:exe','-langversion:9.0','-nologo',('-out:"' + (Join-Path $outDir 'AccessibilityTests.dll') + '"'),('-r:"' + $nunit + '"')) | ForEach-Object { $lines.Add($_) }
Get-ChildItem $coreRuntime.FullName -Filter '*.dll' -File | Where-Object {
    try { [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true } catch { $false }
} | ForEach-Object { $lines.Add('-r:"' + $_.FullName + '"') }
@('Assets/_Project/Systems/Accessibility/AccessibilityPreferences.cs','Assets/_Project/Systems/Accessibility/Localization.cs','Assets/_Project/Systems/Accessibility/AccessibleDialogueText.cs','Assets/_Project/Systems/GameEvents/Tests/EditMode/AccessibilityPreferencesTests.cs') | ForEach-Object { $lines.Add('"' + (Join-Path $project $_) + '"') }
$lines.Add('"' + (Join-Path $PSScriptRoot 'tests/run_accessibility_tests.cs') + '"')
$rsp = Join-Path $outDir 'AccessibilityTests.rsp'
[IO.File]::WriteAllLines($rsp, $lines)
& $dotnet $csc ('@' + $rsp)
if ($LASTEXITCODE -ne 0) { throw 'Pure settings test compilation failed.' }
@{ runtimeOptions = @{ tfm = 'net8.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $coreRuntime.Name } } } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $outDir 'AccessibilityTests.runtimeconfig.json')
& $dotnet (Join-Path $outDir 'AccessibilityTests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Pure settings tests failed.' }
Write-Output 'PASS: first-party runtime and both test assemblies compiled; pure accessibility tests executed. Unity Play Mode and builds were not run.'
