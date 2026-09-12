$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    dotnet restore ValheimWorldSync.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet build ValheimWorldSync.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet test --solution ValheimWorldSync.slnx -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    dotnet run --project tools/ValheimWorldSync.UiSmoke -c Release --no-build -- artifacts/ui-smoke
    if ($LASTEXITCODE -ne 0) { throw 'WPF smoke failed' }
}
finally { Pop-Location }
