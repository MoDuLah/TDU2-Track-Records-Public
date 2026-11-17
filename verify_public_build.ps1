param(
    [string]$OutputDirectory = "TDU2 Track Records\bin\Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
$applicationPath = Join-Path $outputRoot "TDU2 Track Records Public.exe"
$decoderPath = Join-Path $outputRoot "Decoder\TDU2TrackRecords.Decoder.exe"
$seedPath = Join-Path $outputRoot "database.seed.db"

foreach ($requiredPath in ($applicationPath, $decoderPath, $seedPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required public-build file is missing: $requiredPath"
    }
}

$assembly = [Reflection.Assembly]::LoadFrom($applicationPath)
$typeNames = $assembly.GetTypes() | Select-Object -ExpandProperty FullName
$forbiddenTypes = @(
    "TDU2_Track_Records.Drag",
    "TDU2_Track_Records.DragAttemptsWindow",
    "TDU2_Track_Records.BrakingTestWindow",
    "TDU2_Track_Records.Vehicle",
    "TDU2_Track_Records.OdometerWindow"
)
$includedForbiddenTypes = @($forbiddenTypes | Where-Object { $typeNames -contains $_ })
if ($includedForbiddenTypes.Count) {
    throw "Restricted public-build types were compiled: $($includedForbiddenTypes -join ', ')"
}
if ($typeNames -notcontains "TDU2_Track_Records.VehicleDetails") {
    throw "The shared vehicle data model is missing from the public build."
}

& $decoderPath --help | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "The bundled decoder could not start."
}

Write-Output "Public build verification passed."
