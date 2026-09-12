$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    dotnet publish src/ValheimWorldSync.App/ValheimWorldSync.App.csproj -c Release -p:PublishProfile=WindowsX64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    $taskOutput = Join-Path $taskRoot 'artifacts\publish\win-x64'
    $taskFiles = @(Get-ChildItem -LiteralPath $taskOutput -File)
    if ($taskFiles.Count -ne 1 -or $taskFiles[0].Name -ne 'ValheimWorldSync.exe') {
        throw 'Expected a single executable in publish output'
    }
    Get-FileHash -LiteralPath $taskFiles[0].FullName -Algorithm SHA256
}
finally { Pop-Location }
