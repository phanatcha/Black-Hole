# Kerr renderer frame times

Measured with `KerrDemo.unity`: camera at (0, 3, −30) looking at a hole at the origin, 1 unit = 1 M, grid sky, 1920×1080, HDR.

Method: `Camera.Render()` into a 1920×1080 HDR render texture, 20 frames after 3 warm-up frames. A 1-pixel `ReadPixels` after each frame waits for the GPU, so each number is one whole CPU + GPU frame with no overlap. That makes it an upper bound on the frame time.

| Date | Machine | Phase | Spin a/M | ms/frame | FPS |
|---|---|---|---|---|---|
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 1 (sky + shadow, no disk) | 0.6 | 26.6 | 38 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 1 (sky + shadow, no disk) | 0.999 | 36.7 | 27 |

Target: 60 FPS at 1080p (spec, open question 2). Phase 8 closes the gap: STP upscaling, a lower internal resolution, adaptive step limits.
