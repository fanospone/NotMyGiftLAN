$ErrorActionPreference = 'Stop'
$dest = Join-Path $PSScriptRoot 'wwwroot\lib\signalr.min.js'
New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
Invoke-WebRequest -Uri 'https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js' -OutFile $dest
Write-Host "SignalR JS berhasil disimpan ke $dest"
