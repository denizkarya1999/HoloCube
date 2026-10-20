# Transmitter inference references

Three real frames from the local HoloCube transmitter dataset, letterboxed to
320 × 320 with RGB 114 padding, normalized to float32 NCHW, little-endian.
`input.f32` / `expected.json` are the on-device startup fixture. `angled` and
`near` are additional desktop regression cases. Source filenames and hashes,
model checksum, and independent oracle versions are recorded in each JSON file.

Expected boxes come from the official Ultralytics 8.4.56 implementation with
PyTorch 2.12.0, using the supplied model without fusion. The embedded PyTorch
1.8.1 runtime must match within 0.001 confidence and 1 original-image pixel.
Each frame contains one transmitter above the 35% threshold.

These fixtures verify numerical compatibility and preprocessing, not generalization.
They come from the source recording used for dataset preparation and may overlap
training. No independent training history or held-out metrics were supplied with
the checkpoint. `Tools/validate-pt.py` also checks blank input, signed RGBA bytes,
startup reference mismatch, and altered-checkpoint rejection.
