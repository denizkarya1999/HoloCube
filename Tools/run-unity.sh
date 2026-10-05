#!/usr/bin/env bash
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
editor_path="${UNITY_EDITOR_PATH:-$HOME/Unity/Hub/Editor/6000.0.66f2/Editor/Unity}"
method="${1:-Build}"
case "$method" in Build|CreateScene|Validate|ValidateGPU) ;; *) echo 'Use Build, CreateScene, Validate, or ValidateGPU.' >&2; exit 2 ;; esac
if [[ ! -x "$editor_path" ]]; then
    echo 'Set UNITY_EDITOR_PATH to your Unity 6000.0.66f2 executable.' >&2
    exit 1
fi
compat_dir="$project_dir/.build-tools/compat/usr/lib/x86_64-linux-gnu"
if [[ -d "$compat_dir" ]]; then
    export LD_LIBRARY_PATH="$compat_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi
mkdir -p "$project_dir/Builds"
graphics_args=(-nographics)
if [[ "$method" == ValidateGPU ]]; then graphics_args=(-force-vulkan); fi
exec "$editor_path" -batchmode "${graphics_args[@]}" -projectPath "$project_dir" -buildTarget Android \
    -executeMethod "HoloCube.Editor.QuestYoloBuild.$method" -quit \
    -logFile "$project_dir/Builds/$method.log"
