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
| 2026-11-25 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 (Realistic disk, Milky Way + Hipparcos, gas off) | 0.6 | 16.2 | 62 |
| 2026-11-25 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 (hot gas, radio view) | 0.6 | 64.6 | 15 |
| 2026-11-25 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 (hot gas, polarization view) | 0.6 | 70.6 | 14 |
| 2026-11-25 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 (hot gas, eye view, disk off) | 0.6 | 76.1 | 13 |
| 2026-11-25 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 (hot gas, eye view, Realistic disk) | 0.6 | 42.0 | 24 |

Phase 3 rows use 10 warm-up frames so auto exposure has settled, and each is the mean of two runs taken back to back. The same configuration drifted between 28 and 33 ms over one session (thermals), so compare rows within a phase rather than across phases. "Far view" is the Fig. 15a-style framing: camera 60 M out, 4° above the disk plane, 30° vertical field of view. An opaque disk ends rays at their first hit, so the disk makes the frame cheaper than the bare hole: rays no longer wind around the photon orbit. Bloom + ACES costs about 2.5 ms. The far view costs about 12 ms more than the demo view, not yet profiled (likely the longer path from 60 M in and more pixels on the disk's rim).

Phase 4 rows use the same method with the demo's Milky Way cube and the Hipparcos star map (512² per face, 117,955 stars). The star gather costs 0 to 1.5 ms, inside the run-to-run spread of about 1 ms. The pass bakes the star map on its first frame: 115 ms to unpack the catalogue and 186 ms to bake the map, a one-off hitch of about 0.3 s.

Phase 5 rows add `HotGas` to the demo hole (the Sgr A* flow, toroidal field) and use the same method. Inside 50 M a step goes at most 0.1 r, so the gas costs 2.6 to 4.7 times the disk-only frame. Rays that cross the whole flow take about 23 steps per decade of radius, and rays near the photon orbit take far more. The eye view samples three frequencies per step and the radio views one, so the eye view costs about 11 ms more than the radio view. The polarization view adds about 6 ms for the Walker–Penrose constant at each step. The disk ends rays where they hit it, so the eye view with the disk costs less than without. The gas-off row ran 4.6 ms faster than the same scene in phase 4, which is within the session drift noted above.

Target: 60 FPS at 1080p (spec, open question 2). Phase 8 closes the gap: STP upscaling, a lower internal resolution, adaptive step limits.
