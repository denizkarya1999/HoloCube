# Transmitter validation

## Optical image update — October 23, 2026

- Replaced the bundled pattern with the supplied `unnamed.png` (1920 × 1200).
  The source PNG bytes and the image pixels packaged in the APK match the input.
- `assembleDebug`, `assembleDebugAndroidTest`, and `lintDebug` passed; lint reports
  no issues. Updated the existing image checks for the new 16:10 aspect ratio.
- Installed on the connected **Amazon Fire Tablet (KFTUWI), Android 11**.
  All three instrumented device tests passed, including full-image bounds,
  activity recreation, About Us navigation, and controls toggling/auto-hide.
- After launch completed, the full 1920 × 1200 device screenshot matched the
  supplied image pixel-for-pixel, with no cropping, stretching, or visible controls.
- Android reported the app holding the screen awake and requesting maximum
  brightness (`mScreenBrightnessOverrideFromWindowManager=1.0`). The app was left
  open on the new image.
- Installed debug APK SHA-256:
  `6685b850bf6043fccf67b3ae49f57dd35529c6e4f948777955f60d43be23a90b`.

## Initial validation

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
SHA-256: `78ab6cb2e5e1fb67032d97563db17aa0e8b68ebca6cae46a6e3b44b67f89e2d1`.

The app supports API 23 and later; other Android versions and physical phones
have not been device-tested. No Google Play publication is included.

## Maximum brightness update — October 19, 2026

- Rebuilt and passed Android lint, then updated the Motorola installation.
- Android reported `screenBrightnessOverride=1.0` and brightness reason `override`
  on both the image and About Us screens, including after returning to the app.
- On Home, the override cleared and the brightness reason returned to `automatic`.
- The phone's saved brightness (125) and adaptive-brightness mode (1) stayed
  unchanged throughout. The app was left open on the image at full brightness.
