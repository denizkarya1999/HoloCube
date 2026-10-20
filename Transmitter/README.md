# HoloCube Transmitter

A small, offline Android phone companion to the HoloCube VR app. It opens the
supplied **HoloCube_CenterBias_Frame_012153.png** on a black, full-screen canvas
and keeps the display awake while the app is visible.

## Use

1. Open **HoloCube Transmitter**. The image appears immediately, with no controls
   over it. Phones use landscape and can turn to either landscape orientation.
2. Tap anywhere on the image to show **About Us** in the top-right corner.
   Tap again to hide it, or wait four seconds. The button stays visible for
   screen-reader users until dismissed.
3. Tap **About Us** to see the same research project details as the VR app.
   Scroll to read all fields. **Back to image** or Android Back returns to the
   unobstructed image.
4. Swipe from a screen edge to reveal Android navigation and leave the app.

The complete 1920 × 1080 image is fitted without cropping or stretching. Extra
screen area stays black on displays with different proportions. Content avoids
camera cutouts. Android can override orientation preferences on large screens;
the image still fits within the available window.

The app prevents automatic screen timeout while visible, including on About Us.
Home, switching to another app, and the power button keep their normal behavior.
The app requests maximum screen brightness on both the image and About Us screens.
Your normal brightness policy resumes when you leave the app. This window-only
override does not change the phone's saved brightness or adaptive-brightness setting.
It needs no runtime permissions,
internet access, camera, microphone, storage access, or background service.
It displays a static optical pattern; it does not encode or send changing data.

## About Us

Michigan Blue (`#00274C`) and Michigan Maize (`#FFCB05`) match the VR dialogs.
The cube wordmark and project fields follow
[`MainApp.cs`](../Assets/HoloCube/Main%20App/MainApp.cs):

- **Name:** HoloCube Research Project
- **Version:** 1.0
- **Release Date:** TBD
- **Developers:** Deniz K. Acikbas and Ahmad Jayeb
- **Advisor:** Xiao Zhang
- **Institution:** University of Michigan-Dearborn

## Build and install

Open this `Transmitter` directory as a project in Android Studio, independently
of the Unity project. Requirements: JDK 17, Android SDK 36 and Build Tools 35.0.0.
The checked-in wrapper uses Gradle 8.13 with Android Gradle Plugin 8.13.2.
Set `ANDROID_HOME` to your SDK or set `sdk.dir` in a local `local.properties` file.

```sh
./gradlew :app:assembleDebug :app:lintDebug :app:assembleDebugAndroidTest
adb -s PHONE_SERIAL install -r app/build/outputs/apk/debug/app-debug.apk
adb -s PHONE_SERIAL shell am start -n com.developer27.holocube.transmitter/.MainActivity
```

On Windows use `gradlew.bat`. Android 6.0 (API 23) or newer is required.
The app ID is `com.developer27.holocube.transmitter`; it installs alongside the VR
app. Debug APKs are signed for development/sideloading. A store release requires
your own release signing configuration; signing secrets must stay outside Git.

The **HoloCube Transmitter Android** GitHub Actions workflow builds and checks
changes under this directory. Its `HoloCube-Transmitter-debug` artifact contains
the installable APK. Build outputs and local SDK/signing configuration are ignored.

## Verify

With an emulator or Android phone connected and USB debugging authorized:

```sh
ANDROID_SERIAL=PHONE_SERIAL ./gradlew :app:connectedDebugAndroidTest
```

The device tests cover full-screen landscape display, the complete image's
aspect ratio and bounds, the keep-screen-on flag, activity recreation, About Us
navigation, return via both the button and Android Back, and controls auto-hide.
For real timeout behavior, verify the phone remains awake beyond its configured
timeout with the image visible, then that normal timeout resumes after Home.

Image SHA-256 (original PNG bytes are preserved):
`2d6db5f2a9992d3b115d58cf285a79664ffe5aaba35ba3f4883d9fb8859fafd9`.

The app uses Android's [visible-window keep-screen-on behavior](https://developer.android.com/develop/background-work/background-tasks/awake/screen-on)
and [immersive system bars](https://developer.android.com/develop/ui/views/layout/immersive).
