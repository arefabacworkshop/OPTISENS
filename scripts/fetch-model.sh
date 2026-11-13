#!/usr/bin/env bash
set -euo pipefail

if [ -t 1 ]; then
  palette=(51 45 39 75 105 141 75 39)
  printf '\033[?25l'
  trap 'printf "\033[?25h"' EXIT
  for ((f = 0; f < 6; f++)); do
    c1="\033[38;5;${palette[$(((f + 0) % 8))]}m"
    c2="\033[38;5;${palette[$(((f + 2) % 8))]}m"
    c3="\033[38;5;${palette[$(((f + 4) % 8))]}m"
    [ "$f" -gt 0 ] && printf '\033[7A'
    printf "${c1}   ╔═══╗  ${c2} ██████╗ ██████╗ ████████╗██╗███████╗███████╗███╗   ██╗███████╗\n"
    printf "${c1}  ─╣ ◎ ├─ ${c2}██╔═══██╗██╔══██╗╚══██╔══╝██║██╔════╝██╔════╝████╗  ██║██╔════╝\n"
    printf "${c2}   ╚═══╝  ${c3}██║   ██║██████╔╝   ██║   ██║███████╗█████╗  ██╔██╗ ██║███████╗\n"
    printf "${c2}          ${c3}██║   ██║██╔═══╝    ██║   ██║╚════██║██╔══╝  ██║╚██╗██║╚════██║\n"
    printf "${c3}          ${c1}╚██████╔╝██║        ██║   ██║███████║███████╗██║ ╚████║███████║\n"
    printf "\033[0m          \033[2mAI Coach Asset Fetcher\033[0m\n\n"
    [ "$f" -lt 5 ] && sleep 0.1
  done
  printf '\033[?25h'
  trap - EXIT
fi

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
