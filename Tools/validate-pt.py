#!/usr/bin/env python3
"""Verify real inference from the unchanged checkpoint, without exporting it."""
from pathlib import Path
import argparse
import hashlib
import json
import sys
import numpy as np
import torch

root = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(root / 'Assets/HoloCube/YOLO Inference/Python'))
from checkpoint_runtime import load_model, CHECKPOINT_SHA256
from holocube_runtime import rgba_to_tensor

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--rgba', type=Path, help='Optional real Unity GPU preprocessing output')
args = parser.parse_args()
checkpoint = root / 'Assets/HoloCube/YOLO Model/yolo26n.pt'
torch.set_num_threads(2)
model = load_model(str(checkpoint))
fixture = root / 'Tools/Fixtures/YOLO26'
reference = json.loads((fixture / 'expected.json').read_text())
data = np.fromfile(fixture / 'input.f32', dtype='<f4').reshape(1, 3, 320, 320)
with torch.no_grad():
    rows = model(torch.from_numpy(data)).numpy()
assert rows.shape == (1, 300, 6), rows.shape
found = rows[0][rows[0, :, 4] >= .35]
expected = reference['detections']
assert len(found) == len(expected) == 5
for row, other in zip(found, expected):
    assert int(row[5]) == other['classId']
    assert abs(float(row[4]) - other['confidence']) < .001
    difference = (row[:4] - other['box']) * max(reference['width'], reference['height']) / 320
    assert np.linalg.norm(difference) < 1

# Test the byte order and signed Java-byte representation used on Android.
rgba = np.zeros((320, 320, 4), dtype=np.uint8)
rgba[:160, :, 2] = 255  # blue bottom half in Unity
rgba[160:, :, 0] = 255  # red top half in Unity
tensor = rgba_to_tensor(rgba.view(np.int8).reshape(-1)).numpy()
assert tensor[0, 0, 0, 0] == 1 and tensor[0, 2, -1, 0] == 1
assert tensor[0, 2, 0, 0] == 0 and tensor[0, 0, -1, 0] == 0

if args.rgba:
    with torch.no_grad():
        output = model(rgba_to_tensor(np.fromfile(args.rgba, dtype=np.int8))).numpy()[0]
    classes = output[output[:, 4] >= .35, 5].astype(int).tolist()
    assert sorted(classes) == [0, 0, 0, 0, 5], classes
    print('HOLOCUBE_PT_GPU_INPUT_PASSED: Unity RGBA preprocessing -> original .pt -> four people and one bus.')

assert hashlib.sha256(checkpoint.read_bytes()).hexdigest() == CHECKPOINT_SHA256
print('HOLOCUBE_PT_PARITY_PASSED: unchanged checkpoint; five detections match reference within 0.001 confidence and 1 original-image pixel; PyTorch ' + torch.__version__)
