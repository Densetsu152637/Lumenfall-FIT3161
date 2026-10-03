param(
    [Parameter(Mandatory = $true)][string]$ReferenceProject,
    [string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor',
    [string]$ProjectPath,
    [switch]$PrepareOnly
)
& (Join-Path $PSScriptRoot 'compile_assemblies.ps1') -ReferenceProject $ReferenceProject -ProjectPath $ProjectPath -UnityEditorPath $UnityEditorPath -PrepareOnly:$PrepareOnly
if (!$?) { exit 1 }
exit $LASTEXITCODE
