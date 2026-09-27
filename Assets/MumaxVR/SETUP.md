# MumaxVR — Setup Guide

Everything you need to go from a fresh clone to standing inside a live
micromagnetic simulation in VR. Start at [Part 1](#part-1--file-playback-2-minutes-no-gpu);
each part builds on the last.

- [Part 1 · File playback](#part-1--file-playback-2-minutes-no-gpu) — runs on any machine, no GPU
- [Part 2 · Your own mumax³ output](#part-2--your-own-mumax³-output)
- [Part 3 · Viewing in VR](#part-3--viewing-in-vr)
- [Part 4 · Live GPU streaming](#part-4--live-gpu-streaming-the-bridge)
- [Troubleshooting](#troubleshooting)

There is one scene per workflow (all in `Assets/MumaxVR/Scenes/`, sharing the
same renderer and XR rig — only the frame source and its settings differ):

| Scene | Frame source | Covered in |
|-------|--------------|------------|
| `Playback_OvfSamples` | **MumaxPlaybackManager** → bundled `OvfSamples/`, loops | [Part 1](#part-1--file-playback-2-minutes-no-gpu) |
| `Playback_MumaxOutput` | **MumaxPlaybackManager** → your mumax³ `.out` folder, live-follow | [Part 2](#part-2--your-own-mumax³-output) |
| `Live_MumaxBridge` | **MumaxBridgeClient** → `mumax-bridge.exe` streaming over TCP | [Part 4](#part-4--live-gpu-streaming-the-bridge) |

---

## Prerequisites

| Need | Details |
|------|---------|
| **Unity 6.4 (6000.4.11f1)** or newer | Install via [Unity Hub](https://unity.com/download). Older 6.x usually works; the packages this project needs (URP, XR Interaction Toolkit) resolve automatically on first open. |
| A few minutes on first open | Unity imports packages and builds its `Library/` cache — this is normal and one-time. |
| *(VR only)* an OpenXR headset | Any OpenXR runtime: SteamVR, Meta Quest Link, Windows Mixed Reality, etc. |
| *(Live mode only)* an NVIDIA GPU | Compute capability ≥ 5.0, plus the mumax³ build toolchain — see [Part 4](#part-4--live-gpu-streaming-the-bridge). |

**Open the project:** Unity Hub → **Add** → select this folder → open it with a 6000.4 (Unity 6.4) editor.

---

## Part 1 · File playback (2 minutes, no GPU)

This mode replays saved mumax³ `.ovf` files. The repo ships with 48 synthetic
skyrmion frames, so it works immediately.

1. In the **Project** window, open `Assets/MumaxVR/Scenes/Playback_OvfSamples.unity`.
2. Press **▶ Play**.

**What you should see:** a skyrmion orbiting and "breathing" as a field of
little arrows. The uniform (saturated) background is hidden by the physics
filter, which is on by default — untick **physics filter** in the renderer
panel to see the full 64×64 grid. Two panels sit top-left of the Game view:
**Play / Pause** with a frame **scrub** slider, and below it the renderer
controls (filter, LOD, region of interest).

> The arrow color encodes the out-of-plane magnetization *m<sub>z</sub>*:
> **red** points +z, **blue** points −z, white is in-plane. Prefer mumax's
> hue-wheel convention (in-plane angle = hue)? Switch `Color Mode` on
> **MumaxRenderer** or in the renderer panel.

**Knobs** (select the `Manager` object → **MumaxPlaybackManager** in the Inspector):

| Field | What it does |
|-------|--------------|
| `Ovf Folder` | Folder to read `.ovf` files from. Default `OvfSamples` (relative to the project root). Absolute paths work too. |
| `Watch Folder` | Keep polling for new files — lets you follow a simulation as it runs. Off in this scene so the sample animation loops; on in `Playback_MumaxOutput` (Part 2). |
| `Playback Fps` | Animation speed. |
| `Loop` | Restart at the end (ignored while new files keep arriving). |

**Renderer knobs** (same `Manager` object → **MumaxRenderer**; all of these are
also live in the second on-screen panel, and they work identically in every
scene):

| Field | What it does |
|-------|--------------|
| `Color Mode` | **Out Of Plane** (default): red = +m<sub>z</sub>, blue = −m<sub>z</sub>, white = in-plane. **Hue Wheel** (mumax-style): in-plane angle = hue, m<sub>z</sub> = lightness — best for vortices and in-plane textures. |
| `Filter Uniform Cells` / `Min Neighbor Angle` | Physics-based filter: hides cells whose magnetization is within the given angle of all 6 neighbors. What survives are the high-gradient regions — domain walls, vortices, skyrmions. The panel header shows live glyph counts, handy while tuning the angle. |
| `Distance Lod` / `Lod Near / Far Distance` | Proximity LOD: full arrows within *near*, thin sticks out to *far*, hidden beyond. Keeps large grids readable (and fast) in VR. |
| `Roi Mode` / `Roi Transform` | Region of interest: render glyphs only inside a **box**, a spherical **lens**, or a movable 2D **slice** plane (`Slice Thickness` thick) to look inside a volume. Leave `Roi Transform` empty and a translucent handle is created for you — in VR, point at it and grab to move it. Assign your own transform to script or animate the region. |

---

## Part 2 · Your own mumax³ output

Point MumaxVR at any real mumax³ run.

1. Run the bundled reference script — the scene is pre-wired to its output
   folder:
   ```
   mumax3 mumax-bridge/scripts/reference_film.mx3
   ```
2. Open `Assets/MumaxVR/Scenes/Playback_MumaxOutput.unity` and press **▶ Play**
   *while mumax³ is still running*: `Ovf Folder` already points at
   `mumax-bridge/scripts/reference_film.out` (mumax³ creates the `.out` folder
   next to the script) and `Watch Folder` is on, so frames appear as they land.
3. For your own runs, set `Ovf Folder` on **MumaxPlaybackManager** to any
   `*.out` directory — absolute, or relative to the project root. Any script
   with `autosave(m, …)` or `save(m)` works (MumaxVR reads OVF 2.0 Text,
   Binary 4, and Binary 8). If nothing shows up, the top-left panel tells you
   exactly where it's looking.

---

## Part 3 · Viewing in VR

The scene already contains an **XR Origin** rig, so no scene changes are needed.

1. Start your VR runtime (e.g. **SteamVR**) and make sure your headset is
   tracking.
2. Confirm OpenXR is active: **Edit → Project Settings → XR Plug-in Management →
   OpenXR** should be enabled for the **Windows/Standalone** tab (it is by
   default in this repo).
3. Press **▶ Play** — the Game view renders to the headset; put it on and look
   around the arrow field.

> **Just changed anything under XR settings, or opened the project before the
> headset was ready?** Fully **close and reopen the project** — Unity only reads
> XR startup settings when the project loads, not on a scene refresh. This is the
> #1 reason "it won't enter VR." See [Troubleshooting](#troubleshooting).

---

## Part 4 · Live GPU streaming (the bridge)

Instead of reading files, run a *live* mumax³ simulation and stream frames into
Unity in real time. This needs an NVIDIA GPU and a one-time build of the
sidecar — **no prebuilt binary ships in this repo.**

1. Build the sidecar following [`mumax-bridge/README.md`](../../mumax-bridge/README.md).
   It opens with a **"which versions do I need"** guide (mumax³ / CUDA / Go /
   compute-capability), because matching them is the tricky part. Rule of thumb:
   *if plain mumax³ builds on your machine, the bridge builds too.* End result:
   `mumax-bridge/mumax-bridge.exe` **plus** its CUDA runtime DLLs
   (`cufft64_*.dll`, `curand64_*.dll`, `cudart64_*.dll`) sitting next to it — the
   exe builds without them but won't start.
2. Open `Assets/MumaxVR/Scenes/Live_MumaxBridge.unity` — its `Manager` object has
   **MumaxBridgeClient** instead of **MumaxPlaybackManager** (defaults:
   256×256×1, port 35999, auto-start on). Unity launches the exe for you; there's
   nothing to wire.
3. Press **▶ Play**. First start takes **~1 minute** (the solver computes the
   demag kernel and relaxes) before frames flow; the on-screen label tells you
   what it's waiting for. Bridge logs appear in the Console prefixed `[bridge]`.
4. Stop Play — the bridge disconnects and exits on its own.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| **Doesn't enter VR** even though the headset/SteamVR is running | **Close and reopen the project.** Unity loads XR startup settings only at project load. A scene refresh (Ctrl+R) is not enough. Then verify OpenXR is enabled under *Project Settings → XR Plug-in Management → Standalone*. |
| **`bridge exe not found …/mumax-bridge.exe`** on Play | You're in the **`Live_MumaxBridge`** scene but haven't built the bridge. Either open a `Playback_*` scene instead or build the bridge — see [Part 4](#part-4--live-gpu-streaming-the-bridge). |
| **`MumaxRenderer needs an enabled MumaxFrameSource`** | The `Manager` object has neither an enabled **MumaxPlaybackManager** nor **MumaxBridgeClient**. Enable one of them. |
| **Panel buttons, toggles and sliders don't react to clicks** (hover works) | Unity Editor quirk in the Game view, not a project problem: set the Game view toolbar to **Free Aspect** and the **Scale** slider to **1x**, then press Play again. |
| **Nothing renders / no arrows** | Check the Console. Confirm `Ovf Folder` points at a folder that actually contains `.ovf` files (the top-left panel says *"waiting for .ovf files in …"* if not). Also make sure no region of interest is active (renderer panel → ROI **none**). |
| **Fewer arrows than cells / the background is gone** | That's the physics filter (on by default) hiding saturated cells. Untick **physics filter** in the renderer panel, or lower `Min Neighbor Angle`. |
| **Arrows turn into flat sticks / vanish in the distance** | Proximity LOD. Raise `Lod Near / Far Distance` on **MumaxRenderer**, or untick **proximity LOD**. |
| **1 issue in "Project Validation"** (`Interaction Layer 31 … Teleport`) | Harmless XR convention check. Click **Fix / Fix All**. This repo already ships it fixed, so you shouldn't see it. |
| **Pink / magenta arrows** | The URP material didn't resolve. Reimport `Assets/MumaxVR`, or assign a material built from `Custom/MumaxCellShader` on **MumaxRenderer**. |
| **First open is slow / lots of importing** | Normal — Unity is building its `Library/` cache and importing packages. One-time. |

Still stuck? Open an issue with your Unity version, GPU, and the full Console log.
