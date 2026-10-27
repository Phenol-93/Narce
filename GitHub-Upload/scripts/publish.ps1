param([switch]$Portable)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $projectRoot 'artifacts/releases'
$version = ([xml][IO.File]::ReadAllText((Join-Path $projectRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
$packageName = "Nacre-$version-win-x64" + $(if ($Portable) { '-portable' } else { '' })
$destination = Join-Path $releaseRoot $packageName
$archive = "$destination.zip"
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $archive)) {
    throw "Release output already exists: $destination. Keep or move it before building another package."
}
$stage = Join-Path $releaseRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Push-Location $projectRoot
try {
    & dotnet publish src/Nacre/Nacre.csproj -c Release -r win-x64 --self-contained true -p:PublishProfile=WinX64 -p:DebugType=None -p:DebugSymbols=false -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    if (!(Test-Path -LiteralPath (Join-Path $stage 'Nacre.exe'))) { throw 'Nacre.exe was not generated.' }
    foreach ($file in @('README.md', 'LICENSE', 'SECURITY.md', 'THIRD-PARTY-NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $stage
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $stage -Recurse
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $stage -Recurse
    if ($Portable) { [IO.File]::WriteAllText((Join-Path $stage 'portable.flag'), '') }
    if ((Test-Path -LiteralPath (Join-Path $stage 'Data')) -or (Get-ChildItem -LiteralPath $stage -Filter '*.nacre' -Recurse)) {
        throw 'Refusing to package user data.'
    }
    Move-Item -LiteralPath $stage -Destination $destination
    Compress-Archive -LiteralPath $destination -DestinationPath $archive -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$archive.sha256", "$hash  $([IO.Path]::GetFileName($archive))" + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Output "EXE: $(Join-Path $destination 'Nacre.exe')"
    Write-Output "ZIP: $archive"
    Write-Output "SHA256: $hash"
} finally {
    Pop-Location
    # On failure keep the isolated staging directory for inspection. Never delete a user's release/Data directory.
}
