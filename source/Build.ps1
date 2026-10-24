$ErrorActionPreference = 'Stop'
$app = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$temporary = Join-Path $app ('build-' + [guid]::NewGuid().ToString('N') + '.exe')
$target = Join-Path $app '旧F! レアキャHyperion v1.0.0.exe'
$sourceNames = @(
    'AppInfo.cs',
    'NormalEngine.cs', 'NormalAppearance.cs', 'NormalTool.cs',
    'BirthEngine.cs', 'BirthTool.cs', 'GekiEngine.cs', 'GekiTool.cs',
    'LineageDiagram.cs', 'LineagePortrait.cs', 'SavedBirthStorage.cs',
    'DopingMemo.cs', 'ResumableSearch.cs', 'SearchSessionUI.cs',
    'ManualDopingMemo.cs', 'ManualMemoStorage.cs', 'ManualMemoEditors.cs',
    'ManualMemoTournament.cs', 'ManualMemoAppearance.cs', 'ManualMemoDoping.cs',
    'AutoDopingPlanner.cs', 'AutoDopingUI.cs'
)
$sources = $sourceNames | ForEach-Object { Join-Path $PSScriptRoot $_ }

try {
    & $compiler /nologo /warn:4 /warnaserror /codepage:65001 /target:winexe /platform:anycpu /optimize+ /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Runtime.Serialization.dll ('/out:' + $temporary) $sources
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    if ([IO.File]::Exists($target)) {
        [IO.File]::Replace($temporary, $target, [NullString]::Value)
    } else {
        [IO.File]::Move($temporary, $target)
    }
} finally {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
}
Write-Output 'HYPERION_1_0_0_BUILD_OK'
