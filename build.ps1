param(
    [Parameter(Mandatory = $true)]
    [string]$YMM4DirPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sdkRoot = (Resolve-Path -LiteralPath $YMM4DirPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'YukkuriMovieMaker.Plugin.dll'))) {
    throw 'YMM4DirPathにYMM4本体のフォルダーを指定してください。'
}
Push-Location -LiteralPath $projectRoot
try {
    dotnet build KeyframeStudio.sln -c Release "-p:YMM4DirPath=$sdkRoot"
    if ($LASTEXITCODE -ne 0) { throw 'ビルドが失敗しました。' }
    dotnet run --project tests/KeyframeStudio.Integration -c Release --no-build -- $sdkRoot
    if ($LASTEXITCODE -ne 0) { throw 'SDK統合テストが失敗しました。' }
    $packageFolder = Join-Path $projectRoot 'dist\KeyframeStudio'
    New-Item -ItemType Directory -Force -Path $packageFolder | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\KeyframeStudio.Plugin\bin\Release\net10.0-windows10.0.19041.0\KeyframeStudio.dll') -Destination $packageFolder
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\KeyframeStudio.Plugin\bin\Release\net10.0-windows10.0.19041.0\0Harmony.dll') -Destination $packageFolder
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageFolder
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\Harmony-LICENSE.txt') -Destination $packageFolder
    $zipPath = Join-Path $projectRoot 'dist\KeyframeStudio.zip'
    $packageFiles = @('KeyframeStudio.dll', '0Harmony.dll', 'README.md', 'Harmony-LICENSE.txt') | ForEach-Object { Join-Path $packageFolder $_ }
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $zipPath -Force
    Copy-Item -LiteralPath $zipPath -Destination (Join-Path $projectRoot 'dist\KeyframeStudio.ymme') -Force
    Get-FileHash -LiteralPath (Join-Path $packageFolder 'KeyframeStudio.dll') -Algorithm SHA256
    Write-Output '配布物: dist\KeyframeStudio.ymme'
}
finally { Pop-Location }
