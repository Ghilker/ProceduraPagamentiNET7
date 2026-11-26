#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$wsl = Join-Path $env:SystemRoot 'System32\wsl.exe'
if (-not (Test-Path -LiteralPath $wsl -PathType Leaf)) {
    throw 'WSL non disponibile. Installare e inizializzare Ubuntu su WSL 2 prima di configurare Unlimited-OCR 2.'
}

& $wsl --distribution Ubuntu --exec /bin/true *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Ubuntu non disponibile in WSL. Installare e inizializzare la distribuzione Ubuntu su WSL 2, poi ripetere la configurazione.'
}

$script = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'setup-unlimited-ocr.sh')).Replace("`r`n", "`n")
$scriptBytes = [Text.Encoding]::UTF8.GetBytes($script)
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $wsl
$start.Arguments = '--distribution Ubuntu --exec /bin/bash -s'
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $process.StandardInput.BaseStream.Write($scriptBytes, 0, $scriptBytes.Length)
    $process.StandardInput.Close()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Configurazione Unlimited-OCR 2 non riuscita (codice $($process.ExitCode))."
    }
} finally {
    $process.Dispose()
}
