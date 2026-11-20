# Kerr renderer frame times

Measured with `KerrDemo.unity`: camera at (0, 3, −30) looking at a hole at the origin, 1 unit = 1 M, grid sky, 1920×1080, HDR.

Method: `Camera.Render()` into a 1920×1080 HDR render texture, 20 frames after 3 warm-up frames. A 1-pixel `ReadPixels` after each frame waits for the GPU, so each number is one whole CPU + GPU frame with no overlap. That makes it an upper bound on the frame time.

| Date | Machine | Phase | Spin a/M | ms/frame | FPS |
|---|---|---|---|---|---|
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 1 (sky + shadow, no disk) | 0.6 | 26.6 | 38 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 1 (sky + shadow, no disk) | 0.999 | 36.7 | 27 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (disk off, auto exposure, bloom + ACES) | 0.6 | 27.1 | 37 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Realistic disk) | 0.6 | 28.5 | 35 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Interstellar disk) | 0.6 | 24.5 | 41 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Realistic disk) | 0.999 | 27.4 | 36 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Interstellar disk, far view) | 0.6 | 36.7 | 27 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Realistic disk, far view) | 0.6 | 37.9 | 26 |

Phase 3 rows use 10 warm-up frames so auto exposure has settled; repeat runs spread by about ±2 ms. "Far view" is the Fig. 15a-style framing: camera 60 M out, 4° above the disk plane, 30° vertical field of view. An opaque disk ends rays at their first hit, so at a = 0.999 the disk makes the frame cheaper than the bare hole (rays no longer wind around the photon orbit); the far view costs about 10 ms more, not yet profiled (likely the longer path from 60 M in and more pixels on the disk's rim).

Target: 60 FPS at 1080p (spec, open question 2). Phase 8 closes the gap: STP upscaling, a lower internal resolution, adaptive step limits.
