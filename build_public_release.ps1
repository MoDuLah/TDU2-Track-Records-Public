param(
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repositoryRoot "TDU2 Track Records\TDU2 Track Records.csproj"
$decoderBuild = Join-Path $repositoryRoot "Decoder\build_decoder.ps1"
$verification = Join-Path $repositoryRoot "verify_public_build.ps1"
$outputRoot = Join-Path $repositoryRoot "TDU2 Track Records\bin\Release"
$artifactsRoot = Join-Path $repositoryRoot "artifacts"

& $decoderBuild -Python $Python
if ($LASTEXITCODE -ne 0) {
    throw "The bundled decoder build failed."
}

$msbuild = Get-Command msbuild.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $msbuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
            Select-Object -First 1
    }
}
if (-not $msbuild -or -not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild with .NET Framework desktop support was not found."
}

& $msbuild $projectPath /restore /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /m /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "The Release application build failed."
}

& $verification -OutputDirectory $outputRoot
if ($LASTEXITCODE -ne 0) {
    throw "The Release verification failed."
}

$artifactName = "TDU2-Track-Records-Public-v2.5.1-win"
$stageRoot = Join-Path (Join-Path $artifactsRoot ([guid]::NewGuid().ToString("N"))) "TDU2 Track Records Public"
$zipPath = Join-Path $artifactsRoot ($artifactName + ".zip")
$releaseFiles = @(
    "TDU2 Track Records Public.exe",
    "TDU2 Track Records Public.exe.config",
    "System.Data.SQLite.dll",
    "x64\SQLite.Interop.dll",
    "x86\SQLite.Interop.dll",
    "database.seed.db",
    "Decoder\TDU2TrackRecords.Decoder.exe",
    "Decoder\LICENSE",
    "README.md",
    "THIRD_PARTY_NOTICES.md"
)
foreach ($relativePath in $releaseFiles) {
    $source = Join-Path $outputRoot $relativePath
    $destination = Join-Path $stageRoot $relativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}

Compress-Archive -LiteralPath $stageRoot -DestinationPath $zipPath -CompressionLevel Optimal -Force
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath
$checksumPath = $zipPath + ".sha256"
Set-Content -LiteralPath $checksumPath -Encoding ascii -NoNewline -Value ($hash.Hash + " *" + (Split-Path -Leaf $zipPath))

[pscustomobject]@{
    PreviewFolder = $stageRoot
    Zip = $zipPath
    Checksum = $checksumPath
    Sha256 = $hash.Hash
}
