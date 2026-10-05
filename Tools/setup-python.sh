#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
cd "$project_dir"
# Isolated build tools; no changes to system Python packages.
if [[ ! -x .build-tools/tooling/bin/uv ]]; then
    python3 -m pip install --target .build-tools/tooling 'uv==0.12.23'
fi
export UV_PYTHON_INSTALL_DIR="$project_dir/.build-tools/python"
export UV_PYTHON_BIN_DIR="$project_dir/.build-tools/bin"
export UV_CACHE_DIR="$project_dir/.build-tools/uv-cache"
uv="$project_dir/.build-tools/tooling/bin/uv"
"$uv" python install 3.8
if [[ ! -x .build-tools/pt/bin/python ]]; then
    "$uv" venv --python 3.8 .build-tools/pt
fi
"$uv" pip install --python .build-tools/pt/bin/python 'torch==1.8.1+cpu' 'numpy==1.19.5' \
    --extra-index-url https://download.pytorch.org/whl/cpu
echo 'Direct .pt build environment is ready.'
