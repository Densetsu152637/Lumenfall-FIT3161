param(
 [string]$ScopeRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
 [string]$ReferenceProject = 'D:/Git Repositories/Lumenfall-FIT3161/Lumenfall',
 [string]$UnityEditorPath = 'C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor'
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $ScopeRoot 'Lumenfall'
$dag = Join-Path $ReferenceProject 'Library/Bee/artifacts/1900b0aE.dag'
$editor = $UnityEditorPath
$out = Join-Path $project 'Temp/orio-compile'
New-Item -ItemType Directory -Force $out | Out-Null
$dotnet = Join-Path $editor 'Data/NetCoreRuntime/dotnet.exe'
$csc = Join-Path $editor 'Data/DotNetSdkRoslyn/csc.dll'
$refs = @(Get-ChildItem $dag -Filter '*.ref.dll' | Where-Object Name -notmatch '^(Lumenfall\.|Assembly-CSharp)')
$engines = @(Get-ChildItem (Join-Path $editor 'Data/Managed/UnityEngine') -Filter '*.dll')
function Compile-Assembly([string]$name, $sources, $extra) {
 $lines = [System.Collections.Generic.List[string]]::new()
 @('-noconfig','-nostdlib+','-target:library','-langversion:9.0','-deterministic','-nologo','-define:UNITY_6000_4_10;UNITY_EDITOR;ENABLE_INPUT_SYSTEM;UNITY_INCLUDE_TESTS',('-out:"' + (Join-Path $out ($name+'.dll')) + '"')) | ForEach-Object { $lines.Add($_) }
 foreach ($ref in $refs + $engines) { $lines.Add('-r:"' + $ref.FullName + '"') }
 if ($name -match 'Tests$') {
  foreach ($ref in Get-ChildItem (Join-Path $editor 'Data/UnityReferenceAssemblies/unity-4.8-api') -Recurse -Filter '*.dll') { $lines.Add('-r:"' + $ref.FullName + '"') }
 } else { $lines.Add('-r:"' + (Join-Path $editor 'Data/NetStandard/ref/2.1.0/netstandard.dll') + '"') }
 $lines.Add('-r:"' + (Join-Path $editor 'Data/Managed/UnityEditor.dll') + '"')
 foreach ($ref in $extra) { $lines.Add('-r:"' + $ref + '"') }
 foreach ($source in $sources) { $lines.Add('"' + $source.FullName + '"') }
 $rsp = Join-Path $out ($name+'.rsp')
 [IO.File]::WriteAllLines($rsp,$lines,[Text.UTF8Encoding]::new($false))
 & $dotnet $csc ('@'+$rsp)
 if ($LASTEXITCODE -ne 0) { throw "Compile failed for $name" }
 Write-Output "$name compiled ($($sources.Count) sources)."
}
$systems = Join-Path $project 'Assets/_Project/Systems'
$events = @(Get-ChildItem (Join-Path $systems 'GameEvents') -Recurse -Filter '*.cs' | Where-Object FullName -notmatch '\\Tests\\|\\Editor\\')
Compile-Assembly 'Lumenfall.GameEvents' $events @()
$runtime = @(Get-ChildItem $systems -Recurse -Filter '*.cs' | Where-Object FullName -notmatch '\\GameEvents\\|\\Tests\\|\\Editor\\')
$currentEvents = Join-Path $out 'Lumenfall.GameEvents.dll'
$currentRuntime = Join-Path $out 'Lumenfall.Runtime.dll'
Compile-Assembly 'Lumenfall.Runtime' $runtime @($currentEvents)
$nunit = Join-Path $editor 'Data/Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll'
$tests = @(Get-ChildItem (Join-Path $systems 'GameEvents/Tests/EditMode') -Filter '*.cs')
Compile-Assembly 'Lumenfall.GameEvents.Tests' $tests @($currentEvents,$currentRuntime,$nunit)
$play = @(Get-ChildItem (Join-Path $systems 'DataDriven/Tests/PlayMode') -Filter '*.cs')
Compile-Assembly 'Lumenfall.World.PlayModeTests' $play @($currentEvents,$currentRuntime,$nunit)
