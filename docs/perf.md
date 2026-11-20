# Kerr renderer frame times

Measured with `KerrDemo.unity`: camera at (0, 3, −30) looking at a hole at the origin, 1 unit = 1 M, grid sky, 1920×1080, HDR.

Method: `Camera.Render()` into a 1920×1080 HDR render texture, 20 frames after 3 warm-up frames. A 1-pixel `ReadPixels` after each frame waits for the GPU, so each number is one whole CPU + GPU frame with no overlap. That makes it an upper bound on the frame time.

| Date | Machine | Phase | Spin a/M | ms/frame | FPS |
|---|---|---|---|---|---|
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 1 (sky + shadow, no disk) | 0.6 | 26.6 | 38 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 1 (sky + shadow, no disk) | 0.999 | 36.7 | 27 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (disk off, bloom + ACES) | 0.6 | 34.5 | 29 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (disk off, no post) | 0.6 | 32.0 | 31 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Realistic disk) | 0.6 | 23.8 | 42 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Interstellar disk) | 0.6 | 23.2 | 43 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Realistic disk) | 0.999 | 26.1 | 38 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Interstellar disk, far view) | 0.6 | 35.8 | 28 |
| 2026-11-20 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 3 (Realistic disk, far view) | 0.6 | 35.8 | 28 |

Phase 3 rows use 10 warm-up frames so auto exposure has settled, and each is the mean of two runs taken back to back. The same configuration drifted between 28 and 33 ms over one session (thermals), so compare rows within a phase rather than across phases. "Far view" is the Fig. 15a-style framing: camera 60 M out, 4° above the disk plane, 30° vertical field of view. An opaque disk ends rays at their first hit, so the disk makes the frame cheaper than the bare hole: rays no longer wind around the photon orbit. Bloom + ACES costs about 2.5 ms. The far view costs about 12 ms more than the demo view, not yet profiled (likely the longer path from 60 M in and more pixels on the disk's rim).

Target: 60 FPS at 1080p (spec, open question 2). Phase 8 closes the gap: STP upscaling, a lower internal resolution, adaptive step limits.
