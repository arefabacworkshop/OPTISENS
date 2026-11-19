$ErrorActionPreference = "Stop"

[xml]$props = Get-Content "Directory.Build.props"
$version = $props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read <Version> from Directory.Build.props."
}

Write-Host "Publishing OPTISENS.App v$version for win-x64..."
dotnet publish src/OPTISENS.App/OPTISENS.App.csproj -r win-x64 -c Release `
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true `
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist/win-x64

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "WiX CLI ('wix') not found. Install it with: dotnet tool install --global wix --version 5.0.2"
}

New-Item -ItemType Directory -Force -Path "dist\installer" | Out-Null
$outMsi = "dist\installer\OPTISENS-$version-win-x64.msi"

Write-Host "Building MSI installer: $outMsi..."
wix build packaging\windows\OPTISENS.wxs `
  -d "Version=$version" `
  -arch x64 `
  -o $outMsi

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$info = Get-Item $outMsi
Write-Host ("MSI ready: {0} ({1:N1} MB)" -f $info.FullName, ($info.Length / 1MB))
