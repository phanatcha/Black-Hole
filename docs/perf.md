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
| 2026-11-24 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 4 (Realistic disk, Milky Way + Hipparcos) | 0.6 | 20.8 | 48 |
| 2026-11-24 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 4 (Realistic disk, Milky Way, no stars) | 0.6 | 20.7 | 48 |
| 2026-11-24 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 4 (disk off, Milky Way + Hipparcos) | 0.6 | 31.4 | 32 |
| 2026-11-24 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 4 (disk off, Milky Way, no stars) | 0.6 | 29.9 | 33 |

Phase 3 rows use 10 warm-up frames so auto exposure has settled, and each is the mean of two runs taken back to back. The same configuration drifted between 28 and 33 ms over one session (thermals), so compare rows within a phase rather than across phases. "Far view" is the Fig. 15a-style framing: camera 60 M out, 4° above the disk plane, 30° vertical field of view. An opaque disk ends rays at their first hit, so the disk makes the frame cheaper than the bare hole: rays no longer wind around the photon orbit. Bloom + ACES costs about 2.5 ms. The far view costs about 12 ms more than the demo view, not yet profiled (likely the longer path from 60 M in and more pixels on the disk's rim).

Phase 4 rows use the same method with the demo's Milky Way cube and the Hipparcos star map (512² per face, 117,955 stars). The star gather costs 0 to 1.5 ms, inside the run-to-run spread of about 1 ms. The pass bakes the star map on its first frame: 115 ms to unpack the catalogue and 186 ms to bake the map, a one-off hitch of about 0.3 s.

Target: 60 FPS at 1080p (spec, open question 2). Phase 8 closes the gap: STP upscaling, a lower internal resolution, adaptive step limits.
