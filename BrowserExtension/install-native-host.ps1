# Install Native Messaging Host for Background Muter
# Run this script as Administrator after loading the extension to get the Extension ID

param(
    [Parameter(Mandatory=$true)]
    [string]$ExtensionId
)

$hostName = "com.backgroundmuter.tabcontrol"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $scriptDir "native-messaging-host.json"

# Read and update the manifest with the extension ID
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.allowed_origins = @("chrome-extension://$ExtensionId/")
$manifest.path = Join-Path (Split-Path -Parent $scriptDir) "WinBGMute\bin\Release\net8.0-windows10.0.22621.0\WinBGMuter.exe"

# Save updated manifest
$updatedManifestPath = Join-Path $scriptDir "native-messaging-host-installed.json"
$manifest | ConvertTo-Json -Depth 10 | Set-Content $updatedManifestPath -Encoding UTF8

# Register for Edge (Chromium-based)
$edgeKey = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
if (-not (Test-Path $edgeKey)) {
    New-Item -Path $edgeKey -Force | Out-Null
}
Set-ItemProperty -Path $edgeKey -Name "(Default)" -Value $updatedManifestPath

# Register for Chrome (if installed)
$chromeKey = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"
if (-not (Test-Path $chromeKey)) {
    New-Item -Path $chromeKey -Force | Out-Null
}
Set-ItemProperty -Path $chromeKey -Name "(Default)" -Value $updatedManifestPath

Write-Host "Native messaging host registered successfully!" -ForegroundColor Green
Write-Host "Manifest path: $updatedManifestPath"
Write-Host ""
Write-Host "Registered for:"
Write-Host "  - Microsoft Edge"
Write-Host "  - Google Chrome"
Write-Host ""
Write-Host "Please restart the browser for changes to take effect."
