#!/usr/bin/env python3
"""Check transmitter inference against independent Ultralytics reference boxes."""
from pathlib import Path
import argparse
import hashlib
import json
import sys
import tempfile
import shutil
import numpy as np
import torch

root = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(root / 'Assets/HoloCube/Transmitter Inference/Python'))
from checkpoint_runtime import load_model, CHECKPOINT_SHA256
import holocube_runtime as runtime

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--rgba', type=Path, help='Optional real Unity GPU preprocessing output')
args = parser.parse_args()
checkpoint = root / 'Assets/HoloCube/Transmitter Model/holocube_transmitter.pt'
torch.set_num_threads(2)
model = load_model(str(checkpoint))
fixture = root / 'Tools/Fixtures/Transmitter'


def compare(rows, reference):
    assert rows.shape == (1, 300, 6), rows.shape
    assert np.isfinite(rows).all()
    assert np.all(rows[:, :, 5] == 0), 'Only Transmitter class 0 is allowed'
    assert reference['checkpoint_sha256'] == CHECKPOINT_SHA256
    found = rows[0][rows[0, :, 4] >= reference['threshold']]
    expected = reference['detections']
    assert expected and len(found) == len(expected), (len(found), len(expected))
    for row, other in zip(found, expected):
        assert int(row[5]) == other['classId'] == 0
        assert abs(float(row[4]) - other['confidence']) < .001
        difference = (row[:4] - other['box']) * max(reference['width'], reference['height']) / 320
        assert np.linalg.norm(difference) < 1, difference


for name, reference_name in [('input', 'expected'), ('angled', 'angled'), ('near', 'near')]:
    reference = json.loads((fixture / (reference_name + '.json')).read_text())
    data = np.fromfile(fixture / (name + '.f32'), dtype='<f4').reshape(1, 3, 320, 320)
    with torch.no_grad():
        compare(model(torch.from_numpy(data)).numpy(), reference)
    print('Reference boxes match:', reference['source_image'])

# Blank input must not generate a confident transmitter.
with torch.no_grad():
    blank = model(torch.zeros(1, 3, 320, 320)).numpy()
assert (blank[:, :, 4] < .35).all()

# Test signed Java-byte values, Unity orientation, and normalization.
rgba = np.zeros((320, 320, 4), dtype=np.uint8)
rgba[:160, :, 2] = 255
rgba[160:, :, 0] = 255
tensor = runtime.rgba_to_tensor(rgba.view(np.int8).reshape(-1)).numpy()
assert tensor[0, 0, 0, 0] == 1 and tensor[0, 2, -1, 0] == 1
assert tensor[0, 2, 0, 0] == 0 and tensor[0, 0, -1, 0] == 0

# Exercise the packaged startup check and the exact Android RGBA entry point.
# A mismatched reference must fail without marking the runtime ready.
with tempfile.TemporaryDirectory() as temporary:
    package = Path(temporary)
    shutil.copyfile(checkpoint, package / checkpoint.name)
    shutil.copyfile(fixture / 'input.f32', package / 'reference.f32')
    reference = json.loads((fixture / 'expected.json').read_text())
    invalid = dict(reference, checkpoint_sha256='wrong-model')
    (package / 'reference.json').write_text(json.dumps(invalid))
    runtime.__file__ = str(package / 'holocube_runtime.py')
    try:
        runtime.initialize()
        raise AssertionError('Mismatched reference accepted')
    except RuntimeError as error:
        assert 'another model' in str(error)
    assert runtime._model is None
    (package / 'reference.json').write_text(json.dumps(reference))
    print(runtime.initialize())
    data = np.fromfile(package / 'reference.f32', dtype='<f4').reshape(1, 3, 320, 320)
    pixels = np.zeros((320, 320, 4), dtype=np.uint8)
    pixels[:, :, :3] = np.rint(data[0].transpose(1, 2, 0)[::-1] * 255).astype(np.uint8)
    compare(np.array(runtime.detect_rgba(pixels.view(np.int8).reshape(-1))).reshape(1, 300, 6), reference)
    # Reject changed weights before unpickling.
    (package / checkpoint.name).write_bytes(b'not the approved transmitter checkpoint')
    try:
        load_model(str(package / checkpoint.name))
        raise AssertionError('Changed weights accepted')
    except ValueError as error:
        assert 'bundled Transmitter' in str(error)

if args.rgba:
    with torch.no_grad():
        output = model(runtime.rgba_to_tensor(np.fromfile(args.rgba, dtype=np.int8))).numpy()
    compare(output, reference)
    print('HOLOCUBE_PT_GPU_INPUT_PASSED: Unity RGBA -> trained transmitter .pt -> reference boxes.')

assert hashlib.sha256(checkpoint.read_bytes()).hexdigest() == CHECKPOINT_SHA256
print('HOLOCUBE_PT_PARITY_PASSED: three transmitter frames match within 0.001 confidence and 1 original-image pixel; PyTorch ' + torch.__version__)
