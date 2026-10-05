# Build and development notes

| Component | Version |
| --- | --- |
| Unity | 6000.0.66f2 |
| Meta MR Utility Kit | 85.0.0 |
| Meta XR Core | 85.0.0, embedded with an editor-only Linux compile fix |
| Unity Inference Engine | 2.2.1 |
| Detection model | Ultralytics YOLO26n, FP32 ONNX, opset 15 |
| OpenXR | 1.15.1 |
| Android | ARM64, IL2CPP, Vulkan; minimum API 32, target API 35 |

Open this folder in Unity Hub with **Android Build Support**, SDK/NDK, and
OpenJDK installed, then allow package restoration.

- Open `Assets/HoloCube/QuestYOLO.unity` to inspect the app.
- Choose **HoloCube → Validate standalone pipeline** to check scene wiring, resizing, decoding, and real model inference.
- Choose **HoloCube → Build standalone Quest APK** to regenerate the scene and
  build `Builds/HoloCube.apk`. This regenerates the scene from the base sample;
  preserve custom scene edits elsewhere before invoking it again.
- To build a scene you edited manually, use Unity's Android Build UI, with only
  `QuestYOLO.unity` enabled in the build scene list.

The model asset selects GPUCompute on the headset. CPU is available in its
Inspector for debugging, but can be much slower. Main App Inspector settings
control confidence, layers per frame, maximum detections, and result expiry.
The bundled model accepts RGB floats in `[0,1]`, shape `[1,3,320,320]` (NCHW),
and returns a single `[1,300,6]` tensor. Each row is
`[x1,y1,x2,y2,confidence,class_id]` in model-input pixels. The app removes padding,
rescales boxes to the original camera image, filters confidence, and caps results.
The model uses the one-to-one end-to-end head: **do not apply NMS again**.

## Re-export YOLO26

The ONNX model is already bundled. Python is only needed to regenerate it on a
development computer. `provenance.json` records the source URL, package versions,
export flags, and SHA-256 hashes. The original checkpoint is cached locally in
`.build-tools/yolo26/yolo26n.pt` (excluded from Git).

Use an isolated Python environment with `ultralytics==8.4.56`, `onnx==1.21.0`,
and PyTorch `2.12.0` (CPU works). Then run:

```bash
python Tools/export-yolo26.py \
  --checkpoint .build-tools/yolo26/yolo26n.pt \
  --output .build-tools/yolo26/export \
  --test-image /path/to/bus.jpg
```

The script exports `end2end=True`, `nms=False`, `imgsz=320`, `batch=1`,
`dynamic=False`, `opset=15`, `half=False`, `simplify=False`. These options are
for the pinned exporter; newer Ultralytics releases changed the head-selection
arguments. It checks the resulting graph shapes rather than assuming them.
Copy the ONNX, names, and provenance into `Assets/HoloCube/YOLO Model/`, and the
optional `input.f32`/`expected.json` pair into `Tools/Fixtures/YOLO26/` before
validating and rebuilding. Preserve the existing Unity `.meta` files when
replacing model contents. A different input size also requires updating the
fixed-size editor parity checks.

The 320-pixel input is a starting point for the headset, not a measured Quest
performance guarantee. Smaller inputs can miss small or distant objects.

Linux batch build: `bash Tools/run-unity.sh Build`. Set `UNITY_EDITOR_PATH` if
needed. Unity's older editor needs `libxml2.so.2` and compatible ICU libraries.
This workstation used project-local compatibility libraries, which are build-host
dependencies only. The embedded Meta XR Core package fixes a missing Linux branch
in its editor-only simulator installer; headset runtime code is unchanged.

## Behavior and limits

- Frames are processed sequentially; model layers and output readback span Unity
  frames so rendering can continue. No desktop offloading or image streaming.
- Boxes are detections, not persistent tracking or object identity.
- When available, environment depth places boxes along camera rays. Otherwise a
  2-metre plane gives approximate placement, marked **~**.
- Camera pose is cached with each frame. Old results and overlays expire, but fast
  head/object movement can still cause temporary misalignment.
- Preprocessing preserves image proportions and pads with RGB 114. It restores
  sRGB values when Unity samples a camera texture in a linear-color project.
  The overlay removes padding before mapping boxes to camera rays.
- This is not a metric 3D measurement system. Custom objects require trained weights.
- This is a sideloaded development sample. Store distribution is outside its scope.
- See `VALIDATION.md` for checks actually performed. Building an APK alone does
  not verify physical camera access, stereo rendering, or headset performance.

## Upstream source and licenses

Based on [Meta's Unity Passthrough Camera API samples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples),
commit `f9c382190907e20232a74c5ecf6513df47bc13b8`. Original documentation is preserved
in `UPSTREAM_README.md`. Other sample scenes remain as references but are excluded
from the standalone APK's scene list.

Keep `LICENSE.txt`, `Assets/PassthroughCameraApiSamples/LICENSE.txt`, and the
embedded SDK's licenses. The old YOLOv9 model remains with the upstream sample
assets under its original license. The active YOLO26 weights are covered by the
included Ultralytics AGPL-3.0 license, or an applicable Ultralytics Enterprise
license; they are not covered by Meta's MIT model license.

Official references:

- [Quest camera access](https://developers.meta.com/vr/documentation/unity/unity-pca-documentation/)
- [Meta on-device YOLO sample](https://developers.meta.com/vr/documentation/unity/unity-pca-sentis/)

- [YOLO26 model and licensing](https://docs.ultralytics.com/models/yolo26/)
- [End-to-end detection format](https://docs.ultralytics.com/guides/end2end-detection/)

## Graphics checks

`bash Tools/run-unity.sh ValidateGPU` starts Unity with Vulkan graphics enabled
and checks padding, image orientation, sRGB conversion, and the actual runtime
inference path against the reference objects. It needs a working graphics
session; the regular `Validate` command works without one and compares model
outputs on the CPU. The input fixture lives under `Tools/Fixtures/YOLO26` and is
excluded from the headset build because it is outside `Assets`.
