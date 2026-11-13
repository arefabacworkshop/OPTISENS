param(
    [string]$Dest = "models/LFM3-1.7B-ONNX"
)

$ErrorActionPreference = "Stop"

$colors = @("Cyan", "Blue", "Magenta", "DarkCyan")
$lines = @(
    "   +===+   OPTISENS  |  AI Coach Asset Fetcher",
    "  -| @ |-  AI-Assisted Open-Source Sensitivity Finder",
    "   +===+   ---------------------------------------------"
)
for ($f = 0; $f -lt 4; $f++) {
    if ($f -gt 0 -and -not [Console]::IsOutputRedirected) {
        [Console]::SetCursorPosition(0, [Math]::Max(0, [Console]::CursorTop - $lines.Count))
    }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        Write-Host $lines[$i] -ForegroundColor $colors[($f + $i) % $colors.Count]
    }
    if (-not [Console]::IsOutputRedirected -and $f -lt 3) { Start-Sleep -Milliseconds 120 }
}

New-Item -ItemType Directory -Force -Path "$Dest/onnx" | Out-Null
$Base = "https://huggingface.co/LiquidAI/LFM3-1.7B-ONNX/resolve/main"

$Files = @(
    "config.json",
    "generation_config.json",
    "tokenizer.json",
    "tokenizer_config.json",
    "chat_template.jinja"
)

foreach ($f in $Files) {
    Write-Host "Downloading $f..."
    Invoke-WebRequest -Uri "$Base/$f" -OutFile "$Dest/$f"
}

Write-Host "Downloading onnx/model_q4.onnx..."
Invoke-WebRequest -Uri "$Base/onnx/model_q4.onnx" -OutFile "$Dest/onnx/model_q4.onnx"

Write-Host "Downloading onnx/model_q4.onnx_data (~1.63 GB)..."
Invoke-WebRequest -Uri "$Base/onnx/model_q4.onnx_data" -OutFile "$Dest/onnx/model_q4.onnx_data"

$dataSize = (Get-Item "$Dest/onnx/model_q4.onnx_data").Length
$tokSize  = (Get-Item "$Dest/tokenizer.json").Length
if ($dataSize -lt 1500000000) { throw "model_q4.onnx_data too small ($dataSize bytes)" }
if ($tokSize -lt 17000000)    { throw "tokenizer.json too small ($tokSize bytes)" }

Write-Host "Model verified at $Dest"
