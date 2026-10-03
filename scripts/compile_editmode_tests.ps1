param(
    [Parameter(Mandatory = $true)][string]$ReferenceProject,
    [string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor',
    [string]$ProjectPath,
    [switch]$PrepareOnly
)
# Compatibility filename: compiles every supported test asmdef, including PlayMode sources.
& (Join-Path $PSScriptRoot 'compile_assemblies.ps1') -ReferenceProject $ReferenceProject -ProjectPath $ProjectPath -UnityEditorPath $UnityEditorPath -TestsOnly -PrepareOnly:$PrepareOnly
if (!$?) { exit 1 }
exit $LASTEXITCODE
