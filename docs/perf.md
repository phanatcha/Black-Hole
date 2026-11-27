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
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 + streaks (Realistic disk, Milky Way + Hipparcos, gas off) | 0.6 | 22.6 | 44 |
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 + streaks (hot gas, radio view) | 0.6 | 131.8 | 8 |
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 + streaks (hot gas, polarization view) | 0.6 | 133.7 | 7 |
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 + streaks (hot gas, eye view, disk off) | 0.6 | 145.4 | 7 |
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 + streaks (hot gas, eye view, Realistic disk) | 0.6 | 83.7 | 12 |
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 5 + drifting, merging streaks (Realistic disk, Milky Way + Hipparcos, gas off) | 0.6 | 26.8 | 37 |
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 6 (demo view, ice-blue streams, disk opacity 0.34) | 0.6 | 59.2 | 17 |
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 6 (falling, 0.1 M inside r₊, toward the hole) | 0.6 | 72.3 | 14 |
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 6 (falling, 0.1 M inside r₊, looking back and up) | 0.6 | 31.8 | 31 |
| 2026-11-26 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 6 (falling, 0.03 M above r₋, looking back and up) | 0.6 | 41.8 | 24 |
| 2026-11-27 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 7 (KerrScene, lensed scene off) | 0.6 | 62.2 | 16 |
| 2026-11-27 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 7 (KerrScene, meshes only) | 0.6 | 79.7 | 13 |
| 2026-11-27 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 7 (KerrScene, scenery only) | 0.6 | 71.7 | 14 |
| 2026-11-27 | Apple M2 Pro, Metal, Unity 6000.4.1f1 | 7 (KerrScene, meshes + scenery) | 0.6 | 85.2 | 12 |

Phase 3 rows use 10 warm-up frames so auto exposure has settled, and each is the mean of two runs taken back to back. The same configuration drifted between 28 and 33 ms over one session (thermals), so compare rows within a phase rather than across phases. "Far view" is the Fig. 15a-style framing: camera 60 M out, 4° above the disk plane, 30° vertical field of view. An opaque disk ends rays at their first hit, so the disk makes the frame cheaper than the bare hole: rays no longer wind around the photon orbit. Bloom + ACES costs about 2.5 ms. The far view costs about 12 ms more than the demo view, not yet profiled (likely the longer path from 60 M in and more pixels on the disk's rim).

Phase 4 rows use the same method with the demo's Milky Way cube and the Hipparcos star map (512² per face, 117,955 stars). The star gather costs 0 to 1.5 ms, inside the run-to-run spread of about 1 ms. The pass bakes the star map on its first frame: 115 ms to unpack the catalogue and 186 ms to bake the map, a one-off hitch of about 0.3 s.

Phase 5 rows add `HotGas` to the demo hole (the Sgr A* flow, toroidal field) and use the same method. Inside 50 M a step goes at most 0.1 r, so the gas costs 2.6 to 4.7 times the disk-only frame. Rays that cross the whole flow take about 23 steps per decade of radius, and rays near the photon orbit take far more. The eye view samples three frequencies per step and the radio views one, so the eye view costs about 11 ms more than the radio view. The polarization view adds about 6 ms for the Walker–Penrose constant at each step. The disk ends rays where they hit it, so the eye view with the disk costs less than without. The gas-off row ran 4.6 ms faster than the same scene in phase 4, which is within the session drift noted above.

Streak rows turn on the orbiting pattern: `AccretionDisk.streaks` and `HotGas.clumps` at 1. In the same session with both at 0 the frames took 17.0, 68.8, 75.6, 80.1 and 45.5 ms. So the disk's streaks add about a third, and the gas's clumps nearly double the gas frame. The disk evaluates its three-octave noise once per crossing, but every gas sample blends the 3 × 3 cells around it. The rows with the pattern off ran 0.8 to 5 ms slower than phase 5's. Part of that is the coordinate time now integrated with every step, and part is session drift.

The drifting, merging streaks row bends each strand across the orbit and fades runs of them into ribbons: two more noise lookups per blended cell. Off, the same session took 17.3 ms, so the disk's pattern now costs 9.5 ms where it cost 5.6. The gas clumps are unchanged.

Phase 6 rows use the demo as committed in 8046a89 (ice-blue streams, disk opacity 0.34, 28° field of view) and the camera falling on the scripted plunge (key 1). In the same session the demo view took 59 ms, where the opaque disk took 27: rays now carry on through the disk. Inside the horizon a frame costs what the view holds, 72 ms toward the hole and 32 ms looking back. Not yet profiled.

Phase 7 rows use `Assets/KerrDemo/KerrScene.unity` as committed in 8e0ad49, before its test objects were deleted: the demo with `LensedScene` on the hole, 3,072 triangles traced within 50 M and a 512² scenery map out to 2,000 M. The off row disables `LensedScene`; the meshes-only and scenery-only rows turn the other part off. The scene adds 23 ms against a target of 10 ms: 17 for the meshes, 9 for the scenery. With the tree search cut to the root box test, the meshes cost 0.1 ms, so their cost is the search through the median-split tree on every step that passes through the scene. Skipping steps that stay inside 15 M saved nothing. One scenery sample per step costs 3.5 ms; the samples between a step's ends, which keep thin things from slipping through, cost the other 5. In Play `LensedScene` also captures one scenery face per frame, 2.7 ms. Not yet tried: a surface-area-heuristic tree. Phase 8's lower internal resolution scales all of it.

Target: 60 FPS at 1080p (spec, open question 2). Phase 8 closes the gap: STP upscaling, a lower internal resolution, adaptive step limits.
