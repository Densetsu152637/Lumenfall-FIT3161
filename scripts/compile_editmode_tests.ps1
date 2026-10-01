param([string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'Lumenfall'
$dag = Join-Path $project 'Library/Bee/artifacts/1900b0aE.dag'
$template = Join-Path $dag 'Lumenfall.GameEvents.Tests.rsp'
$dotnet = Join-Path $UnityEditorPath 'Data/NetCoreRuntime/dotnet.exe'
$csc = Join-Path $UnityEditorPath 'Data/DotNetSdkRoslyn/csc.dll'
$outDir = Join-Path $project 'Temp/codex-compile'
$rspPath = Join-Path $outDir 'Lumenfall.GameEvents.Tests.codex.rsp'
$output = Join-Path $outDir 'Lumenfall.GameEvents.Tests.codex.dll'

if (!(Test-Path $template)) { throw "Unity's generated test reference response file is missing: $template" }
if (!(Test-Path $dotnet) -or !(Test-Path $csc)) { throw "Unity Roslyn compiler was not found under $UnityEditorPath" }
if (!(Test-Path (Join-Path $outDir 'Lumenfall.GameEvents.dll')) -or !(Test-Path (Join-Path $outDir 'Lumenfall.Runtime.dll'))) {
    throw 'Compile the runtime assemblies first with scripts/compile_runtime.ps1.'
}

New-Item -ItemType Directory -Force $outDir | Out-Null
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($line in Get-Content $template) {
    if ($line -match '^-out:|^-refout:|^[-/]additionalfile:|^"Assets/(_Project/Systems/GameEvents/)?Tests/EditMode/') { continue }
    if ($line -match 'Lumenfall\.GameEvents\.ref\.dll|Assembly-CSharp|Unity\.InputSystem') { continue }
    if ($line -match '^-r:"Library/') { $line = $line.Replace('-r:"Library/', '-r:"' + (Join-Path $project 'Library/')) }
    $lines.Add($line)
}
$lines.Add('-out:"' + $output + '"')
$lines.Add('-refout:"' + (Join-Path $outDir 'Lumenfall.GameEvents.Tests.codex.ref.dll') + '"')
$lines.Add('-r:"' + (Join-Path $outDir 'Lumenfall.GameEvents.dll') + '"')
$lines.Add('-r:"' + (Join-Path $outDir 'Lumenfall.Runtime.dll') + '"')
$inputSystemRef = Join-Path $project 'Library/Bee/artifacts/1900b0aE.dag/Unity.InputSystem.ref.dll'
if (!(Test-Path $inputSystemRef)) { throw "Unity.InputSystem reference assembly is missing: $inputSystemRef" }
$lines.Add('-r:"' + $inputSystemRef + '"')
Get-ChildItem (Join-Path $project 'Assets/_Project/Systems/GameEvents/Tests/EditMode') -File -Filter '*.cs' | ForEach-Object { $lines.Add('"' + $_.FullName + '"') }
[IO.File]::WriteAllLines($rspPath, $lines, (New-Object Text.UTF8Encoding($false)))
Push-Location $root
try { & $dotnet $csc ('@' + $rspPath); exit $LASTEXITCODE }
finally { Pop-Location }
