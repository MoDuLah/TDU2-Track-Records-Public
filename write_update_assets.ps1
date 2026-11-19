param(
    [Parameter(Mandatory = $true)][string]$PackageFolder,
    [Parameter(Mandatory = $true)][string]$ArtifactsFolder,
    [Parameter(Mandatory = $true)][string]$ArtifactName,
    [Parameter(Mandatory = $true)][string[]]$Files
)

$ErrorActionPreference = "Stop"
$folder = Join-Path $ArtifactsFolder ($ArtifactName + "-update-assets")
New-Item -ItemType Directory -Path $folder -Force | Out-Null

$entries = for ($i = 0; $i -lt $Files.Count; $i++) {
    $relative = $Files[$i]
    $source = Join-Path $PackageFolder $relative
    $asset = "{0}-file-{1:D2}.bin" -f $ArtifactName, ($i + 1)
    Copy-Item -LiteralPath $source -Destination (Join-Path $folder $asset) -Force
    [ordered]@{
        path = $relative.Replace('\', '/')
        asset = $asset
        sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{ format = 1; files = @($entries) }
$manifest | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $folder ($ArtifactName + "-update.json")) -Encoding UTF8
$folder
