#!/usr/bin/env python3
"""Development-only exporter. The Quest app uses the exported ONNX file, not Python."""
import argparse
import hashlib
import json
from pathlib import Path

import cv2
import numpy as np
import onnx
import torch
import ultralytics
from ultralytics import YOLO

PINNED_VERSION = '8.4.56'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--checkpoint', type=Path, required=True, help='Official yolo26n.pt checkpoint')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--test-image', type=Path, help='Optional image for a Unity/PyTorch comparison fixture')
    args = parser.parse_args()
    if ultralytics.__version__ != PINNED_VERSION:
        raise RuntimeError(f'Use ultralytics=={PINNED_VERSION}; export options changed in later releases.')
    args.output.mkdir(parents=True, exist_ok=True)
    model = YOLO(str(args.checkpoint.resolve()))
    exported = Path(model.export(format='onnx', imgsz=320, batch=1, dynamic=False,
        simplify=False, opset=15, end2end=True, nms=False, half=False, device='cpu'))
    output_path = args.output / 'yolo26n.onnx'
    output_path.write_bytes(exported.read_bytes())
    graph = onnx.load(output_path)
    onnx.checker.check_model(graph)
    shape = lambda value: [d.dim_value for d in value.type.tensor_type.shape.dim]
    assert shape(graph.graph.input[0]) == [1, 3, 320, 320]
    assert shape(graph.graph.output[0]) == [1, 300, 6]
    (args.output / 'coco.names.txt').write_text('\n'.join(model.names[i] for i in range(80)) + '\n')
    provenance = {
        'model': 'Ultralytics YOLO26n',
        'checkpoint_url': 'https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26n.pt',
        'checkpoint_sha256': hashlib.sha256(args.checkpoint.read_bytes()).hexdigest(),
        'onnx_sha256': hashlib.sha256(output_path.read_bytes()).hexdigest(),
        'ultralytics': ultralytics.__version__, 'torch': torch.__version__, 'onnx': onnx.__version__,
        'export': {'imgsz': 320, 'batch': 1, 'dynamic': False, 'simplify': False,
                   'opset': 15, 'end2end': True, 'nms': False, 'half': False},
        'input': [1, 3, 320, 320], 'output': [1, 300, 6],
        'output_columns': ['x1', 'y1', 'x2', 'y2', 'confidence', 'class_id'],
        'license': 'AGPL-3.0; alternative commercial license from Ultralytics',
    }
    (args.output / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')

    if args.test_image:
        # Match the app: RGB [0,1], aspect-preserving resize, centered 114/255 padding.
        image = cv2.imread(str(args.test_image))
        if image is None:
            raise RuntimeError(f'Cannot read {args.test_image}')
        height, width = image.shape[:2]
        scale = min(320 / width, 320 / height)
        resized_width, resized_height = round(width * scale), round(height * scale)
        left, top = (320 - resized_width) // 2, (320 - resized_height) // 2
        resized = cv2.resize(image, (resized_width, resized_height), interpolation=cv2.INTER_LINEAR)
        padded = cv2.copyMakeBorder(resized, top, 320-resized_height-top, left, 320-resized_width-left,
                                   cv2.BORDER_CONSTANT, value=(114,114,114))
        data = np.ascontiguousarray(padded[:, :, ::-1].transpose(2, 0, 1)[None], dtype=np.float32) / 255.0
        reference = YOLO(str(args.checkpoint.resolve())).model.eval()
        reference.end2end = True
        reference.fuse(verbose=False)
        with torch.inference_mode():
            output = reference(torch.from_numpy(data))[0].cpu().numpy()
        assert list(output.shape) == [1, 300, 6]
        confident = output[0][output[0, :, 4] >= 0.35]
        assert len(confident) > 0, 'Fixture did not produce any confident detections'
        data.astype('<f4').tofile(args.output / 'input.f32')
        fixture = {'width': width, 'height': height, 'modelSize': 320,
                   'detections': [{'classId': int(row[5]), 'confidence': float(row[4]),
                       'box': [float(row[0]),float(row[1]),float(row[2]),float(row[3])]} for row in confident]}
        (args.output / 'expected.json').write_text(json.dumps(fixture,indent=2)+'\n')
        print('Reference detections:', [(model.names[int(row[5])], round(float(row[4]), 3)) for row in confident])
    print('Export verified:', output_path)


if __name__ == '__main__':
    main()
