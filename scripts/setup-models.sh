#!/usr/bin/env bash
set -euo pipefail

if ! command -v ollama >/dev/null 2>&1; then
  echo "Ollama is not installed. Install it from https://ollama.com/download" >&2
  exit 1
fi

ollama pull glm-ocr:latest
ollama pull qwen3.5:4b

echo "Purchase Memory models are ready."
