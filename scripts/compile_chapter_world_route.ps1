param([Parameter(Mandatory=$true)][string]$ReferenceProject, [string]$UnityEditorPath='C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor')
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project=Join-Path $root 'Lumenfall'
$referenceProject=(Resolve-Path $ReferenceProject).Path
$dag=Join-Path $referenceProject 'Library/Bee/artifacts/1900b0aE.dag'
$editor=$UnityEditorPath
$outDir=Join-Path $project 'Temp/world-route-compile'
New-Item -ItemType Directory -Force $outDir | Out-Null
$runtimeScript=Get-Content -Raw (Join-Path $root 'scripts/compile_runtime.ps1')
$runtimeScript=$runtimeScript.Replace('$root = (Resolve-Path (Join-Path $PSScriptRoot ''..'')).Path', '$root = '''+$root+'''')
$runtimeScript=$runtimeScript.Replace('$dag = Join-Path $project ''Library/Bee/artifacts/1900b0aE.dag''','$dag = '''+$dag+'''')
$runtimeScript=$runtimeScript.Replace('Temp/codex-compile','Temp/world-route-compile')
$runtimeScript=$runtimeScript.Replace('Assembly-CSharp|Lumenfall.GameEvents|GameEvents.Tests|Unity.InputSystem','Assembly-CSharp|Lumenfall\.|Unity.InputSystem')
$runtimePath=Join-Path $outDir 'runtime.ps1'
[IO.File]::WriteAllText($runtimePath,$runtimeScript)
& pwsh -NoProfile -File $runtimePath -UnityEditorPath $UnityEditorPath
if ($LASTEXITCODE -ne 0) {exit $LASTEXITCODE}
$dotnet=Join-Path $editor 'Data/NetCoreRuntime/dotnet.exe'
$csc=Join-Path $editor 'Data/DotNetSdkRoslyn/csc.dll'
foreach ($assembly in @('Lumenfall.GameEvents.Tests','Lumenfall.World.PlayModeTests')) {
    $lines=[Collections.Generic.List[string]]::new()
    foreach ($line in Get-Content (Join-Path $dag ($assembly+'.rsp'))) {
        if ($line -match '^-(out|refout):|^[-/]additionalfile:|^"Assets/|Lumenfall\..*\.ref\.dll|Assembly-CSharp|Unity\.InputSystem|^-analyzer:') {continue}
        if ($line -match '^-r:"Library/') {$line=$line.Replace('-r:"Library/','-r:"'+$referenceProject+'/Library/')}
        $lines.Add($line)
    }
    $lines.Add('-out:"'+(Join-Path $outDir ($assembly+'.dll'))+'"')
    foreach ($reference in @('Lumenfall.GameEvents.dll','Lumenfall.Runtime.dll')) {$lines.Add('-r:"'+(Join-Path $outDir $reference)+'"')}
    $lines.Add('-r:"'+(Join-Path $dag 'Unity.InputSystem.ref.dll')+'"')
    $sources=if($assembly -eq 'Lumenfall.GameEvents.Tests') {'Assets/_Project/Systems/GameEvents/Tests/EditMode'} else {'Assets/_Project/Systems/DataDriven/Tests/PlayMode'}
    Get-ChildItem (Join-Path $project $sources) -Recurse -Filter '*.cs' -File | ForEach-Object {$lines.Add('"'+$_.FullName+'"')}
    $rsp=Join-Path $outDir ($assembly+'.rsp')
    [IO.File]::WriteAllLines($rsp,$lines,(New-Object Text.UTF8Encoding($false)))
    & $dotnet $csc ('@'+$rsp)
    if ($LASTEXITCODE -ne 0) {exit $LASTEXITCODE}
}
