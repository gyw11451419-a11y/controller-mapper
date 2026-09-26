param(
    [string]$Version = '0.1.0-preview.1'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Invalid version' }
$root = Split-Path $PSScriptRoot -Parent
$releaseRoot = Join-Path $root "dist/releases/v$Version"
$publish = Join-Path $releaseRoot 'ControllerMapper-win-x64'
if (-not (Test-Path (Join-Path $publish 'ControllerMapper.exe'))) { throw 'Publish the application first; see README.md.' }
foreach ($name in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $publish
}
Copy-Item -LiteralPath (Join-Path $root 'docs/INSTALL.md') -Destination (Join-Path $publish 'START-HERE.md')
$assets = Get-Content (Join-Path $root 'src/ControllerMapper.Desktop/obj/project.assets.json') -Raw | ConvertFrom-Json
$packageRoot = @($assets.packageFolders.PSObject.Properties.Name)[0]
$licenseRoot = Join-Path $publish 'licenses'
New-Item $licenseRoot -ItemType Directory -Force | Out-Null
$packages = @($assets.libraries.PSObject.Properties.Name)
# Single-file output embeds managed libraries; resolved runtime versions are in the restore graph.
$frameworks = $assets.project.frameworks.PSObject.Properties.Value
$runtimeRefs = @($frameworks | ForEach-Object { $_.downloadDependencies } | Where-Object { $_.name -match '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$' })
foreach ($ref in $runtimeRefs) { $packages += "$($ref.name)/$($ref.version.Trim('[',']').Split(',')[0].Trim())" }
if ($runtimeRefs.Count -ne 2) { throw 'Expected NETCore and WindowsDesktop runtime references' }
$mitSource = Join-Path $packageRoot (($packages | Where-Object { $_ -match '^Microsoft.NETCore.App.Runtime.win-x64/' }) + '/LICENSE.TXT')
$mitText = Get-Content $mitSource -Raw
foreach ($package in $packages | Sort-Object -Unique) {
    $packagePath = Join-Path $packageRoot $package.ToLowerInvariant()
    $dest = Join-Path $licenseRoot ($package -replace '/', '-')
    New-Item $dest -ItemType Directory -Force | Out-Null
    $nuspec = Get-ChildItem $packagePath -Filter '*.nuspec' | Select-Object -First 1
    if (-not $nuspec) { throw "Missing metadata: $package" }
    Copy-Item $nuspec.FullName $dest
    $legalFiles = @(Get-ChildItem $packagePath -File | Where-Object { $_.Name -match '(?i)license|notice' })
    foreach ($file in $legalFiles) { Copy-Item $file.FullName $dest }
    [xml]$xml = Get-Content $nuspec.FullName
    $metadata = $xml.package.metadata
    if (-not ($legalFiles | Where-Object { $_.Name -match '(?i)license' })) {
        $expression = [string]$metadata.license.'#text'
        if ($expression -eq 'MIT') {
            $copyright = [string]$metadata.copyright
            if (-not $copyright) { throw "Missing MIT copyright: $package" }
            $mitText.Replace('Copyright (c) .NET Foundation and Contributors', $copyright) | Set-Content (Join-Path $dest 'LICENSE.txt') -Encoding UTF8
        } elseif ($expression -eq 'Apache-2.0') {
            Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dest 'LICENSE.txt')
            "Package: $package`nAuthors: $($metadata.authors)`n$($metadata.copyright)`nRepository: $($metadata.repository.url)`nCommit: $($metadata.repository.commit)" | Set-Content (Join-Path $dest 'ATTRIBUTION.txt') -Encoding UTF8
        } else { throw "Missing license text for $package ($expression)" }
    }
}
$desktopNotices = Get-ChildItem (Join-Path $root '.dotnet/sdk/*/Sdks/Microsoft.NET.Sdk.WindowsDesktop/THIRD-PARTY-NOTICES.TXT') | Sort-Object FullName | Select-Object -Last 1
if (-not $desktopNotices) { throw 'Missing WindowsDesktop third-party notices' }
Copy-Item $desktopNotices.FullName (Join-Path $licenseRoot 'WindowsDesktop-SDK-THIRD-PARTY-NOTICES.TXT')
$sourceCommit = git -C $root rev-parse HEAD
"Version: $Version`nApplication source commit: $sourceCommit`nRuntime: win-x64, self-contained`nPackages:`n$($packages -join "`n")" | Set-Content (Join-Path $publish 'BUILD-INFO.txt') -Encoding UTF8
$zip = Join-Path $releaseRoot "ControllerMapper-v$Version-win-x64.zip"
if (Test-Path $zip) { throw "Archive already exists: $zip" }
Compress-Archive -Path $publish -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ASCII
Get-Item $zip | Select-Object FullName, Length
