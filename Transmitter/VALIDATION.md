# Transmitter validation

Verified October 19, 2026.

- `assembleDebug`, `assembleDebugAndroidTest`, and `lintDebug` passed; lint reports
  no issues. The supported toolchain versions are pinned deliberately.
- Installed and launched on a **Motorola Moto G Play (2024), Android 14**.
- All three instrumented device tests passed on that phone: full-screen display
  and recreation, image aspect ratio/bounds/centering, About navigation and both
  return paths, and controls toggling/auto-hide.
- Visually checked the phone's unobstructed landscape image and blue/maize About
  screen. The full project details, including institution, are scrollable.
- With the phone's normal **60-second screen timeout**, and its developer
  “stay awake while charging” setting **off**, the app remained foreground and
  awake for more than **five minutes without user input**. Android reported this
  app as the window holding the screen on. No phone timeout setting was changed.
- The bundled source PNG is byte-for-byte identical to the supplied image.
- A newer API 37 emulator launched the app, but automated testing there was
  interrupted by the emulator's low-memory process kills. API 37 is not claimed
  as fully tested. The device results above are from the physical Motorola.

Development APK: `app/build/outputs/apk/debug/app-debug.apk` (sideloadable, debug signed).
SHA-256: `54b1958efa836842fdc9ca623fdd96bb1e4b1bb90993a2e9519ba10550e451ed`.

The app supports API 23 and later; other Android versions and physical phones
have not been device-tested. No Google Play publication is included.
