#!/usr/bin/env bash
set -euo pipefail

DEST=${1:-models/LFM3-1.7B-ONNX}
mkdir -p "$DEST/onnx"
BASE="https://huggingface.co/LiquidAI/LFM3-1.7B-ONNX/resolve/main"

for f in config.json generation_config.json tokenizer.json tokenizer_config.json chat_template.jinja; do
  curl -L "$BASE/$f" -o "$DEST/$f"
done

curl -L "$BASE/onnx/model_q4.onnx"      -o "$DEST/onnx/model_q4.onnx"
curl -L "$BASE/onnx/model_q4.onnx_data" -o "$DEST/onnx/model_q4.onnx_data"

DATA_SIZE=$(wc -c < "$DEST/onnx/model_q4.onnx_data")
TOK_SIZE=$(wc -c < "$DEST/tokenizer.json")
if [ "$DATA_SIZE" -lt 1500000000 ]; then
  echo "ERROR: model_q4.onnx_data too small ($DATA_SIZE bytes)" >&2
  exit 1
fi
if [ "$TOK_SIZE" -lt 17000000 ]; then
  echo "ERROR: tokenizer.json too small ($TOK_SIZE bytes)" >&2
  exit 1
fi

echo "Model verified at $DEST"
