# HoloCube original .pt validation

Verified on October 4, 2026.

## Model and app

- App: HoloCube 0.4 (code 4), package `com.holocube.questyolo`.
- Model: original Ultralytics `yolo26n.pt`; no ONNX, TorchScript, or other model export.
- Checkpoint SHA-256: `9b09cc8bf347f0fc8a5f7657480587f25db09b34bf33b0652110fb03a8ad4fef`.
- The checkpoint extracted from the APK's embedded Python archive has the exact
  same SHA-256 as the original downloaded file.
- Embedded runtime: Python 3.8, PyTorch 1.8.1, NumPy 1.19.5, Chaquopy 16.1.0.
- Input `[1,3,320,320]` RGB floats in `[0,1]`; output `[1,300,6]` end-to-end rows.
- Export-free loading uses inference-only layer methods adapted from Ultralytics
  8.4.56 for this exact checkpoint. Arbitrary checkpoints are not supported.

## Passed on the development computer

- Unity 6000.0.66f2 compilation and Android ARM64/IL2CPP/Vulkan APK build.
- Scene wiring, original checkpoint hash, 80 labels, and shader references.
- Letterbox coordinate mapping; confidence/class checks; detection limits;
  invalid-value and incompatible-output rejection; no second NMS pass.
- Direct PyTorch 1.8.1 inference from the unchanged checkpoint produces four
  people and one bus, matching the independent PyTorch 2.12.0 fixture within
  0.001 confidence and 1 original-image pixel.
- Actual Vulkan GPU image preparation on the desktop NVIDIA RTX 3050 Ti passes
  padding, orientation, sRGB, and RGBA-readback checks.
- Those real Unity RGBA pixels also produce four people and one bus through
  the new Python preprocessing and direct checkpoint inference.
- APK inspection confirms the original `.pt` and reference fixture are embedded
  in `assets/chaquopy/app.imy`, with no exported model in that Python archive.

## Passed on the connected Quest

- Installed the APK over the existing app while preserving its data.
- Cold launch completed with no crash reported by the Quest launch verifier.
- Embedded Python/PyTorch loaded the original checkpoint on the headset.
- The startup reference check passed on the headset: five detections match the
  independent fixture's class IDs, confidence scores, and box coordinates.
- The live camera path returned 300 detection rows from a captured frame.
- The first logged camera-frame worker call took **155 ms**. This is one sample
  including Python input/output handling, not sustained FPS or total AR latency.

Headset log markers:

```text
HOLOCUBE_PT_DEVICE_PARITY_PASSED: original checkpoint; five reference detections match.
HOLOCUBE_PT_READY: original YOLO26n checkpoint loaded on device
HOLOCUBE_PT_CAMERA_FRAME: 300 detection rows in 155 ms
```

## Remaining checks

Visual stereo-overlay alignment, repeated pause/resume cycles, sustained frame
rate, memory use, and thermal behavior still need longer headset testing.
The runtime uses older Python/PyTorch packages to enable direct `.pt` loading;
desktop and initial headset success do not establish compatibility with every
future Horizon OS version or arbitrary trained YOLO checkpoints.

## Local artifacts

Builds and logs are excluded from Git. Reproduce them using the Unity menu or
`Tools/run-unity.sh` after running `Tools/setup-python.sh`.

- APK: `Builds/HoloCube.apk`
- Size: 110,003,196 bytes
- SHA-256: `07a5d2e7e52f2894a4c4d45a2cd70198d1298da881324fc7397b72f7d52728d3`
- Previous build: `Builds/HoloCube-before-PT.apk`
- Build/CPU checks: `Builds/Build.log`
- GPU preprocessing checks: `Builds/ValidateGPU.log`
- GPU-readback fixture: `Builds/pt-preprocessed.rgba`

Run `.build-tools/pt/bin/python Tools/validate-pt.py --rgba Builds/pt-preprocessed.rgba`
after `bash Tools/run-unity.sh ValidateGPU` to repeat the GPU-to-PyTorch comparison.

## Recording feedback update — October 5, 2026

- Rebuilt from the complete repository. The previously deployed APK lacked the
  native `QuestCameraVideoRecorder` class, even though its source was in Git.
- Android build guards now require the recorder source, independent recording
  and save-feedback canvases, the red circle renderer, and the recorder class
  in the completed APK/AAB's DEX files.
- The final build passed the existing scene/model validation and Android build
  guards. Log: `Builds/Build-final.log`.
- Installed on the connected Quest 3S with app data preserved. Cold launch
  completed, the app was foreground, and the launch verifier found no crash.
- After launch, four new B-button recordings completed on the Quest. Device
  logs reported `HOLOCUBE_VIDEO_STARTED`, `HOLOCUBE_VIDEO_SAVING`, and
  `HOLOCUBE_VIDEO_SAVED` for each, including the 34-frame
  `Movies/HoloCube/HoloCube_20261005_182656_785.mp4`. All four files were present
  in the headset's Movies/HoloCube folder.
- Device model parity also passed, and live camera inference returned 300
  detection rows. Log: `Builds/Quest-final.log`.
- The user confirmed the red recording badge and saved-file message now work
  in the headset. The inference-off timer is set to three seconds; a separate
  visual timing check was not reported.
- Final APK SHA-256:
  `4b12f0f8f26d9af11d023418022abb5e3d59acb2d0e45031b2102b486b910505`.
