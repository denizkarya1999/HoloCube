# YOLO26 parity fixture

`input.f32` is a little-endian float32 RGB tensor in NCHW order, shape
`[1,3,320,320]`, values 0–1. It contains Ultralytics' public bus example image
resized without distortion and padded with RGB 114. It is used by desktop checks and by a one-time startup
check inside the APK to verify the actual Android PyTorch runtime.

`expected.json` records the confident PyTorch detections from the official
YOLO26n checkpoint. The box coordinates still include model-input padding.
Unity checks class IDs, scores, and the coordinates after removing padding.

Source image: https://github.com/ultralytics/ultralytics/blob/main/ultralytics/assets/bus.jpg
Checkpoint/runtime details: `Assets/HoloCube/YOLO Model/provenance.json`.
The reference detections came from the original checkpoint with Ultralytics 8.4.56
and PyTorch 2.12.0. `Tools/validate-pt.py` verifies that direct loading under the
embedded runtime's PyTorch version preserves these results. No model export is
used by this validation or the active app.
