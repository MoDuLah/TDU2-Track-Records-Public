param(
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"
$decoderRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = Join-Path $decoderRoot "dist"
$workRoot = Join-Path $decoderRoot "build"
$specRoot = Join-Path $decoderRoot "spec"

& $Python -m PyInstaller `
    --noconfirm `
    --clean `
    --onefile `
    --name "TDU2TrackRecords.Decoder" `
    --distpath $outputRoot `
    --workpath $workRoot `
    --specpath $specRoot `
    --add-data "$(Join-Path $decoderRoot 'data\contest_reference.json');data" `
    --add-data "$(Join-Path $decoderRoot 'data\house_reference.json');data" `
    (Join-Path $decoderRoot "track_records_decoder.py")

if ($LASTEXITCODE -ne 0) {
    throw "The decoder build failed with exit code $LASTEXITCODE."
}

Write-Output (Join-Path $outputRoot "TDU2TrackRecords.Decoder.exe")
