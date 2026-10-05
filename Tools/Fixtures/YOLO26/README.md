# YOLO26 parity fixture

`input.f32` is a little-endian float32 RGB tensor in NCHW order, shape
`[1,3,320,320]`, values 0–1. It contains Ultralytics' public bus example image
resized without distortion and padded with RGB 114. It is only used by editor
checks and is not bundled into the APK.

`expected.json` records the confident PyTorch detections from the official
YOLO26n checkpoint. The box coordinates still include model-input padding.
Unity checks class IDs, scores, and the coordinates after removing padding.

Source image: https://github.com/ultralytics/ultralytics/blob/main/ultralytics/assets/bus.jpg
Model/export details: `Assets/HoloCube/YOLO Model/provenance.json`.
Regenerate with `Tools/export-yolo26.py --test-image /path/to/bus.jpg` plus the
checkpoint and output options described in `Docs/DEVELOPMENT.md`.
