param([string]$Version = '0.2.1-beta.1')
$ErrorActionPreference = 'Stop'
if ($PSScriptRoot) { Set-Location (Split-Path $PSScriptRoot -Parent) }
if (!(Test-Path 'bin/Pegline.exe')) { throw 'Run build.cmd successfully before packaging.' }
$stage = Join-Path $PWD ('bin/package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item bin/Pegline.exe,bin/Pegline.exe.config,LICENSE,THIRD_PARTY_NOTICES.md,README.md,VERIFICATION.md -Destination $stage
$archive = Join-Path $PWD "bin/Pegline-$Version-win-x64.zip"
Compress-Archive -Path "$stage/*" -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" -Value "$hash  $(Split-Path $archive -Leaf)" -Encoding ascii
Write-Output $archive
