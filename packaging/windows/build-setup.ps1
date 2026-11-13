$ErrorActionPreference = "Stop"

dotnet publish src/OPTISENS.App/OPTISENS.App.csproj -r win-x64 -c Release `
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true `
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist/win-x64

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidate = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (Test-Path $candidate) { $iscc = $candidate }
}

if ($iscc) {
    & $iscc "packaging\windows\OPTISENS.iss"
} else {
    Write-Warning "Inno Setup 6 (ISCC.exe) not found. Publish succeeded at dist/win-x64/OPTISENS.App.exe; install Inno Setup 6 to compile packaging/windows/OPTISENS.iss into OPTISENSSetup.exe."
}
