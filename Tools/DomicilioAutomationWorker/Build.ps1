param(
    [Parameter(Mandatory = $true)][string]$DirStudioRoot,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$BackendServiceSource
)

$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path -LiteralPath $DirStudioRoot).Path
$outputFolder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$artifactsFolder = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\work\DomicilioAutomationBuild'))
$arguments = @('publish', (Join-Path $PSScriptRoot 'DomicilioAutomationWorker.csproj'),
    '--configuration', 'Release', '--no-self-contained', '--output', $outputFolder,
    '--artifacts-path', $artifactsFolder, "-p:DirStudioRoot=$backendRoot")
if ($BackendServiceSource) {
    $arguments += "-p:BackendServiceSource=$((Resolve-Path -LiteralPath $BackendServiceSource).Path)"
}
$originalDirectory = [System.Environment]::CurrentDirectory
Push-Location -LiteralPath (Join-Path $backendRoot 'Dirstudio.Controllers')
try {
    # MSBuild deve risolvere anche i riferimenti relativi fra i progetti backend.
    [System.Environment]::CurrentDirectory = (Get-Location).ProviderPath
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Compilazione del worker non completata.'
    }
}
finally {
    [System.Environment]::CurrentDirectory = $originalDirectory
    Pop-Location
}
