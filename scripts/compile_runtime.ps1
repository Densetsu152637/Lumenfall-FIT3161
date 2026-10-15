param([string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'Lumenfall'
$dag = Join-Path $project 'Library/Bee/artifacts/1900b0aE.dag'
$dotnet = Join-Path $UnityEditorPath 'Data/NetCoreRuntime/dotnet.exe'
$csc = Join-Path $UnityEditorPath 'Data/DotNetSdkRoslyn/csc.dll'
$outDir = Join-Path $project 'Temp/codex-compile'
New-Item -ItemType Directory -Force $outDir | Out-Null
$rsp = Join-Path $outDir 'compiler.rsp'
$lines = [System.Collections.Generic.List[string]]::new()
@('-noconfig','-nostdlib+','-target:library','-langversion:9.0','-deterministic','-nologo','-define:UNITY_6000_4_10;UNITY_EDITOR;ENABLE_INPUT_SYSTEM',('-out:"' + (Join-Path $outDir 'Lumenfall.GameEvents.dll') + '"')) | ForEach-Object { $lines.Add($_) }
$refs = Get-ChildItem $dag -File -Filter '*.ref.dll' | Where-Object Name -notmatch 'Assembly-CSharp|Lumenfall.GameEvents|Lumenfall.Runtime|GameEvents.Tests|Unity.InputSystem'
$inputSystemRef = Join-Path $dag 'Unity.InputSystem.ref.dll'
if (!(Test-Path $inputSystemRef)) { throw "Unity.InputSystem reference assembly is missing: $inputSystemRef" }
$refs | ForEach-Object { $lines.Add('-r:"' + $_.FullName + '"') }
$managed = Join-Path $UnityEditorPath 'Data/Managed/UnityEngine'
@('UnityEngine.CoreModule.dll','UnityEngine.InputModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.UIModule.dll','UnityEngine.JSONSerializeModule.dll','UnityEngine.AudioModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.UIElementsModule.dll','UnityEngine.TextCoreFontEngineModule.dll','UnityEngine.TextCoreTextEngineModule.dll') | ForEach-Object { $lines.Add('-r:"' + (Join-Path $managed $_) + '"') }
$lines.Add('-r:"' + (Join-Path $UnityEditorPath 'Data/NetStandard/ref/2.1.0/netstandard.dll') + '"')
$gameEventSources = Get-ChildItem (Join-Path $project 'Assets/_Project/Systems/GameEvents') -Recurse -File -Filter '*.cs' | Where-Object FullName -notmatch '\\Tests\\|\\Editor\\'
$gameEventSources | ForEach-Object { $lines.Add('"' + $_.FullName + '"') }
[IO.File]::WriteAllLines($rsp, $lines, (New-Object Text.UTF8Encoding($false)))
& $dotnet $csc ('@' + $rsp)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runtimeRsp = Join-Path $outDir 'runtime.rsp'
$runtimeLines = [System.Collections.Generic.List[string]]::new()
@('-noconfig','-nostdlib+','-target:library','-langversion:9.0','-deterministic','-nologo','-define:UNITY_6000_4_10;UNITY_EDITOR;ENABLE_INPUT_SYSTEM',('-out:"' + (Join-Path $outDir 'Lumenfall.Runtime.dll') + '"'),('-r:"' + (Join-Path $outDir 'Lumenfall.GameEvents.dll') + '"')) | ForEach-Object { $runtimeLines.Add($_) }
$runtimeLines.Add('-r:"' + $inputSystemRef + '"')
$refs | ForEach-Object { $runtimeLines.Add('-r:"' + $_.FullName + '"') }
@('UnityEngine.CoreModule.dll','UnityEngine.InputModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.UIModule.dll','UnityEngine.JSONSerializeModule.dll','UnityEngine.AudioModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.UIElementsModule.dll','UnityEngine.TextCoreFontEngineModule.dll','UnityEngine.TextCoreTextEngineModule.dll') | ForEach-Object { $runtimeLines.Add('-r:"' + (Join-Path $managed $_) + '"') }
$runtimeLines.Add('-r:"' + (Join-Path $UnityEditorPath 'Data/NetStandard/ref/2.1.0/netstandard.dll') + '"')
Get-ChildItem (Join-Path $project 'Assets/_Project/Systems') -Recurse -File -Filter '*.cs' | Where-Object FullName -notmatch '\\GameEvents\\|\\Tests\\|\\Editor\\' | ForEach-Object { $runtimeLines.Add('"' + $_.FullName + '"') }
[IO.File]::WriteAllLines($runtimeRsp, $runtimeLines, (New-Object Text.UTF8Encoding($false)))
& $dotnet $csc ('@' + $runtimeRsp)
exit $LASTEXITCODE
