# HoloCube

![Device: Quest 3 / 3S](https://img.shields.io/badge/Device-Quest%203%20%2F%203S-0866FF)
![Unity 6000.0.66f2](https://img.shields.io/badge/Unity-6000.0.66f2-222222)
![Model: YOLO26 nano](https://img.shields.io/badge/Model-YOLO26%20nano-6E40C9)
![Original PyTorch checkpoint](https://img.shields.io/badge/Model%20format-Original%20.pt-EF6C00)
![Runs on the headset](https://img.shields.io/badge/Inference-On%20device-238636)

A standalone object detection app for **Meta Quest 3 / Quest 3S**.
It sees the camera image, finds objects, and draws labelled boxes in your headset.
The camera, model, and detection code all run on the Quest. The APK embeds
Python, PyTorch, and the **original, unchanged `yolo26n.pt` checkpoint**. A computer
is only needed to build or install the app. The HoloCube pipeline uses no model
conversion, ONNX, TorchScript, server, or model download.

## Research inspiration

Inspired by **Dr. Xiao Zhang, Li Xiao, and Matt W. Mutka**, *HoloCube: 3D Optical
IoT Connections via Software Defined Pepper’s Ghost* (ICNP 2024). The paper
explores optical IoT communication using virtual 3D cubes, Pepper’s Ghost, and
spatial/color encoding. [Read the paper](https://www.zhangxiao.me/data/ICNP24_HoloCube.pdf).

This independent prototype provides the Quest camera → YOLO26 → AR label
pipeline. The bundled model recognizes everyday COCO objects. Detecting HoloCube
optical tags needs custom training; tag decoding, optical data transmission, and
the paper’s control channel remain future work. This repository is not the
authors’ official implementation or a reproduction of their reported results.

## Start here

Open `Assets/HoloCube/QuestYOLO.unity` in Unity. Select the **Main App** object
in the scene to see its camera, model, overlay, and detection settings.

Read these three files in order:

| Part | File | Its job |
| --- | --- | --- |
| **1. Main App** | `Assets/HoloCube/Main App/MainApp.cs` | Get a camera image, ask YOLO to process it, show the results. |
| **2. YOLO Inference** | `Assets/HoloCube/YOLO Inference/YOLOInference.cs` | Resize the image, run the network, read and filter its results. |
| **3. YOLO Model** | `Assets/HoloCube/YOLO Model/YOLOModel.cs` | Define the checkpoint, object names, and CPU settings. |

The complete flow is:

```text
Quest camera → MainApp → YOLOInference → list of Detection objects
                              ↑                      ↓
                          YOLOModel           DetectionOverlay
                                               boxes + labels
```

`MainApp.Update()` is the entry point for each display frame. It reads the
buttons, checks camera access, captures a new image when ready, and advances the
current detection. An Android background worker runs PyTorch while Unity keeps
drawing the headset view. The GPU prepares the camera image; model inference runs
on the headset CPU.

## Where things live

```text
HoloCube/
├── Assets/
│   ├── HoloCube/                         The standalone app
│   │   ├── QuestYOLO.unity                Launch scene
│   │   ├── Main App/
│   │   │   ├── MainApp.cs                 Camera → detection → display
│   │   │   └── DetectionOverlay.cs        Boxes and labels in the headset
│   │   ├── YOLO Inference/
│   │   │   ├── YOLOInference.cs           Submit an image and read detections
│   │   │   ├── ImagePreprocessor.cs       Copy a small camera image from the GPU
│   │   │   ├── Detection.cs               One detection result
│   │   │   ├── DetectionFilter.cs         Decode and filter YOLO26 output
│   │   │   ├── ImageLetterbox.cs          Resize and map box coordinates
│   │   │   ├── Letterbox.shader           Resize with gray padding
│   │   │   ├── Android/PyTorchWorker.java Background worker and Python bridge
│   │   │   └── Python/
│   │   │       ├── holocube_runtime.py    Pixels → PyTorch → detection rows
│   │   │       └── checkpoint_runtime.py Load original .pt and run its layers
│   │   ├── YOLO Model/
│   │   │   ├── YOLOModel.cs               Checkpoint constants and class labels
│   │   │   ├── QuestYOLO.asset            Saved model settings
│   │   │   ├── yolo26n.pt                 Original YOLO26 nano checkpoint
│   │   │   ├── coco.names.txt             80 labels in class-ID order
│   │   │   ├── provenance.json           Source, runtime versions, checksum
│   │   │   └── LICENSE.txt               Ultralytics model license
│   │   └── Editor/                       Scene setup, builds, validation
│   └── PassthroughCameraApiSamples/      Meta rig and reference samples
├── Packages/
│   ├── manifest.json                     Unity dependencies
│   └── com.meta.xr.sdk.core/              Embedded Meta SDK with Linux fix
├── ProjectSettings/                      Unity and Android settings
├── Tools/
│   ├── run-unity.sh                       Build or validate from a terminal
│   ├── setup-python.sh                    Prepare isolated build tools
│   ├── validate-pt.py                     Test the unchanged .pt checkpoint
│   └── Fixtures/YOLO26/                   PyTorch/Unity inference comparison
├── Docs/DEVELOPMENT.md                    Setup, model format, limitations
├── .github/labels.json                    GitHub issue-label definitions
├── VALIDATION.md                         Recorded checks and device status
├── UPSTREAM_README.md                    Original Meta documentation
├── LICENSE.txt                           Upstream license notice
└── README.md                             Start here
```

Unity `.meta` files are kept in Git beside their assets; they are omitted from
this diagram. Build output (`Builds/`), Unity caches, and local build tools are
excluded from the repository.

The `Editor` tools only run on the development computer. The folders named
`PassthroughCameraApiSamples` and `Packages` provide Meta's sample assets and SDK
support; begin with `Assets/HoloCube` when learning or changing this app.
Keep the supporting assets because the scene setup reuses Meta's configured rig.

## Common changes

| I want to… | Change this |
| --- | --- |
| Show fewer uncertain detections | Increase **Confidence** on Main App. |
| Change the number of boxes | Change **Max Detections** on Main App. |
| Change button actions or status messages | `MainApp.cs`. |
| Change label text or where boxes appear | `DetectionOverlay.cs`. |
| Understand the results | `DetectionFilter.cs` reads YOLO26 output and removes weak results. |
| Inspect the weights and object names | Select `YOLO Model/QuestYOLO.asset`. |
| Train for different objects | Extend the checkpoint loader and validation; arbitrary .pt files are not interchangeable. |

A **Detection** contains a class ID (an index into the names file), a confidence
score from 0 to 1, and a rectangle in original camera-image pixels. **Inference** means running the trained
model. This app uses YOLO26's **one-to-one (NMS-free)** output, so it does not
suppress overlapping detections a second time. **Letterboxing** fits the image
inside the 320 × 320 model input and fills the unused area with gray padding.

## Build, install, and use

1. Clone this repository and run `bash Tools/setup-python.sh` on the development
   computer. Then open it in **Unity 6000.0.66f2** with Android Build Support.
   See the [development notes](Docs/DEVELOPMENT.md) for other host setups.
2. Choose **HoloCube → Build standalone Quest APK**. This recreates the demo scene
   and writes `Builds/HoloCube.apk`; preserve custom scene edits before using it.
3. Connect a Quest with developer mode enabled and sideload the APK.
4. Open **HoloCube** in the headset and allow camera access.
5. On startup, only detection boxes and labels with confidence percentages are shown. Press the left controller’s **Menu (three lines)** button to show or hide the controls. **A** toggles YOLO inference; “Inference Mode: Off” disappears after three seconds. Hold **B** for YOLO data collection; a red circle and **Recording** appear above your view while recording. Release **B** to stop: the indicator disappears, followed by saving progress and the saved filename for six seconds. MP4s are saved in `Movies/HoloCube` on the headset. Recording failures also appear briefly in the same area. Press **X** to open About HoloCube and **Y** to view Settings. In Settings, use the left joystick up/down to select the confidence threshold or maximum number of boxes, then left/right to adjust the selected value.

Dialogs and recording messages use Michigan Maize text (`#FFCB05`) on Michigan Blue
backgrounds (`#00274C`), following the [U-M color palette](https://brand.umich.edu/wp-content/uploads/2025/02/250027-U-M-Style-Guide-February2025.pdf).
Maize borders and vector icons identify the guide, About, Settings, inference-off,
saving, saved, and warning messages.
Dialogs use larger 28-point text, and About features a maize cube-and-HoloCube
wordmark above the project details.
Rounded cards, soft shadows, maize accents, icon badges, and bold headings keep
the dialog styling consistent throughout the app.
Settings presents **Confidence Thresholds** and **Number of boxes** as bold rows
with individual icons and boxed values; the brighter value border marks the
joystick selection. The About logo displays only **HoloCube**.

You can disconnect the computer after installation. The weights and class names
are included in the app; no server or model download is needed. Labels marked
**~** use an estimated distance when scene depth is unavailable.

For scene, coordinate, decoder, and real `.pt` checks without an APK build,
choose **HoloCube → Validate standalone pipeline**.
For command-line builds, run `bash Tools/run-unity.sh Build` from this folder.

See [development notes](Docs/DEVELOPMENT.md) for SDK versions, model format,
performance settings, and limitations; [validation results](VALIDATION.md) record
what has actually been tested. The bundled model is **Ultralytics YOLO26 nano**,
with 80 COCO classes, an end-to-end `[1,300,6]` output, and 320 × 320 input. This sample detects objects; it does not track their
identities across frames.

## Labels

### Detection labels

The model predicts the 80 COCO classes below. Their zero-based IDs follow the
order in [coco.names.txt](Assets/HoloCube/YOLO%20Model/coco.names.txt), from
`person` (0) to `toothbrush` (79). These are object classes, not optical tag IDs.

<details>
<summary>Show all 80 object labels</summary>

```text
person, bicycle, car, motorcycle, airplane, bus, train, truck, boat, traffic light,
fire hydrant, stop sign, parking meter, bench, bird, cat, dog, horse, sheep, cow,
elephant, bear, zebra, giraffe, backpack, umbrella, handbag, tie, suitcase, frisbee,
skis, snowboard, sports ball, kite, baseball bat, baseball glove, skateboard,
surfboard, tennis racket, bottle, wine glass, cup, fork, knife, spoon, bowl, banana,
apple, sandwich, orange, broccoli, carrot, hot dog, pizza, donut, cake, chair, couch,
potted plant, bed, dining table, toilet, tv, laptop, mouse, remote, keyboard, cell
phone, microwave, oven, toaster, sink, refrigerator, book, clock, vase, scissors,
teddy bear, hair drier, toothbrush
```

</details>

### GitHub issue labels

Use these labels alongside the standard `bug`, `enhancement`, and `documentation`
labels when reporting an issue or proposing a change.

| Label | Area |
| --- | --- |
| `main-app` | Camera access, app lifecycle, controls, and headset overlays. |
| `yolo-inference` | Image preparation, inference, output decoding, and filtering. |
| `yolo-model` | YOLO weights, class labels, export settings, and training. |
| `quest` | Quest hardware, Android builds, permissions, and installation. |
| `research` | HoloCube paper, optical tags, and research extensions. |
| `performance` | Latency, frame rate, memory, and sustained headset behavior. |

Definitions are saved in [.github/labels.json](.github/labels.json).

## Credits and licenses

Based on [Meta's camera samples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples).
Original documentation is in [UPSTREAM_README.md](UPSTREAM_README.md).
Keep the supplied licenses when sharing or modifying the project.

The YOLO26 model is distributed under [Ultralytics' AGPL-3.0 or Enterprise licensing](https://docs.ultralytics.com/models/yolo26/).
Its license is included beside the weights. Meta's sample assets retain their
original licenses. The Python layer methods are adapted from Ultralytics under the same included
license. See the development notes for the direct checkpoint runtime.
