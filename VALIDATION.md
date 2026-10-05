# HoloCube build validation

Verified on October 4, 2026.

## Model and app

- Active weights: Ultralytics YOLO26 nano (`yolo26n.onnx`), exported from the
  official checkpoint with Ultralytics 8.4.56, PyTorch 2.12.0, ONNX 1.21.0.
- Input: `[1,3,320,320]` RGB floats in `[0,1]`, with aspect-preserving resizing
  and centered gray padding. Output: `[1,300,6]` end-to-end detection rows.
- YOLO26 model checksum and the 80-entry COCO label file verified.
- Export options, checkpoint URL, and hashes are recorded in
  `Assets/HoloCube/YOLO Model/provenance.json`.
- App name: HoloCube. APK version: 0.3 (code 3), package `com.holocube.questyolo`.

## Passed

- Unity 6000.0.66f2 compilation and Android ARM64/IL2CPP/Vulkan APK build.
- Rebuilt after renaming the app to HoloCube; generated Android resources confirm
  the display name, version 0.3, code 3, and unchanged package identifier.
- Saved scene connections, YOLO26 model, names, and letterbox shader references.
- Landscape/portrait/odd-sized image padding and original-image box mapping.
- Confidence and class filtering, invalid-value rejection, detection limits,
  empty confident results, and rejection of an incompatible output shape.
- End-to-end results are not passed through NMS again.
- **Actual Unity CPU inference:** a bus-image tensor produces the same five
  detections as the PyTorch checkpoint (four people and one bus). Class IDs agree;
  score differences are below 0.001 and box-vector differences below 1 original
  image pixel. This is a functional parity check, not a COCO accuracy benchmark.
- **Actual Unity GPU inference:** the full runtime `YOLOInference.Detect` pipeline
  produces four people and one bus from the same image on the workstation's
  NVIDIA GeForce RTX 3050 Ti Laptop GPU using Vulkan.
- GPU shader checks confirm gray padding, top/bottom orientation, and restoration
  of sRGB camera values when Unity samples in linear color space.
- Documentation links and source/documentation whitespace checks pass.

## Headset status

The earlier YOLOv9 APK was installed successfully on the connected Quest on
October 4, 2026. This YOLO26 APK has not been installed or run on the headset.
Quest GPU compatibility, live camera permissions, overlay alignment, frame rate,
and sustained thermal behavior still require device testing. Desktop GPU success
is not a Quest performance measurement.

## APK and logs

Build outputs and logs are local artifacts excluded from Git. Rebuild them with
`Tools/run-unity.sh` or the Unity menu.

- Current APK: `Builds/HoloCube.apk`
- Size: 59,840,684 bytes
- SHA-256: `1e0f16ca9102139af441afefa6916c619f9621327b1901885a51c86a5a492768`
- Previous APK retained: `Builds/HoloCubeYOLO-before-YOLO26.apk`
- Build/CPU parity log: `Builds/Build.log`
- Graphics test log: `Builds/ValidateGPU.log`

Run `bash Tools/run-unity.sh Validate` for CPU/scene checks, or
`bash Tools/run-unity.sh ValidateGPU` with a working Vulkan graphics environment
for GPU checks. `Build` also runs the CPU/scene checks before producing an APK.

The model carries Ultralytics' included AGPL-3.0 license (or an applicable
Ultralytics Enterprise license). The original Meta sample assets retain their
licenses and the legacy weights remain in the sample folder.
