# Transmitter inference references

Three real validation-split frames from the 300-frame HoloCube dataset, letterboxed to
320 × 320 with RGB 114 padding, normalized to float32 NCHW, little-endian.
`input.f32` / `expected.json` are the bright on-device startup fixture. `angled` and
`low_light` are additional desktop regression cases. Source filenames and hashes,
model checksum, and independent oracle versions are recorded in each JSON file.

Expected boxes come from the official Ultralytics 8.4.56 implementation with
PyTorch 2.12.0, using the supplied model without fusion. The embedded PyTorch
1.8.1 runtime must match within 0.001 confidence and 1 original-image pixel.
Each frame contains one transmitter above the 35% threshold.

These fixtures verify numerical compatibility and preprocessing, not generalization.
They come from the validation split of `HoloCube_Transmitter_YOLO26_300frames_20261023.zip`
(210 training / 60 validation / 30 testing images from two same-room recordings).
The source archive checksum is recorded in each JSON. The supplied model-only
checkpoint contains no training history or accuracy metrics. One additional clipped
close-up validation frame (`holocube_frame_1044.png`) produced no detection above
35% in a spot check; these three parity cases do not establish detection accuracy.
`Tools/validate-pt.py` also checks blank input, signed RGBA bytes,
startup reference mismatch, and altered-checkpoint rejection.
