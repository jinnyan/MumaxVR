# MumaxVR — Design Document

**Project:** Real-time 3D/VR visualizer for mumax3 micromagnetic simulations
**Status:** Design document — describes the full vision; see the root README and
SETUP.md for what is actually implemented today.
**Last updated:** 2026-07-02

> **Implementation status (v1.0.0).** Implemented: OVF file playback
> (`OvfLoader`, `MumaxPlaybackManager`), live streaming via `mumax-bridge`
> (`main.go`, `server.go`, `protocol.go`; frames only, no command channel yet),
> `MumaxBridgeClient`, and the GPU renderer with physics filter, proximity LOD
> and box / lens / slice ROI. Not yet implemented: the JSON command channel
> (`commands.go`), `MumaxSimulationManager`, `MumaxUIManager` / VR sliders,
> `Materials/` and shipping the bridge via `StreamingAssets/` (the bridge is
> built by the user and run from `mumax-bridge/`). The bridge reads the
> magnetization with `engine.Eval` on the main goroutine rather than
> `InjectAndWait`. Sections 4–5 below describe the target design.

---

## 1. Vision

Stand inside a running micromagnetic simulation. Watch domains form, skyrmions
move, and vortices spin — in VR, in real time — and change the physics
(field, material parameters) with your hands while it runs.

The same idea has been proven for **atomistic** spin simulations (e.g. Spirit).
MumaxVR does it for **continuum micromagnetics** (mumax3), which is the
standard tool for realistic device-scale magnet simulations.

---

## 2. The one big architectural decision (read this first)

### The in-process DLL approach: the engine lives INSIDE Unity

An atomistic engine like Spirit is a C++ library with an official C API,
compiled as `Spirit.dll`.
Unity loads it directly via P/Invoke. One process, shared memory, done.

```
┌─────────────── Unity.exe ───────────────┐
│  C# Scripts ──P/Invoke──► Spirit.dll    │
│       ▲                        │        │
│       └──── spin array ◄───────┘        │
└─────────────────────────────────────────┘
```

### MumaxVR: the engine lives NEXT TO Unity

**We do NOT build a DLL.** Three reasons:

1. **No C API.** mumax3 is written in Go. It exposes no C functions to import.
   Its only interfaces are a script language and a web GUI. Forcing it into a
   DLL means writing a Go wrapper, embedding the whole Go runtime + CUDA
   inside Unity's process — fragile and painful.
2. **Singleton design.** mumax3's simulation state is global (one simulation
   per process, GPU-resident), accessed through an internal thread-injection
   mechanism. It was never designed to be driven from outside its own process.
3. **GPLv3 license.** Linking mumax3 into the Unity app would legally make the
   whole app GPL. A separate process talking over a socket does not.

So mumax runs as its own small program — the **Bridge** — and streams data to
Unity over a local connection:

```
┌────── mumax-bridge.exe (Go) ──────┐         ┌────────── Unity.exe ──────────┐
│  imports mumax3 engine package    │         │                               │
│  runs solver on the GPU (CUDA)    │ frames  │  BridgeClient (reader thread) │
│  every N steps: copy M to host ───┼────────►│    ▼ locked Vector3[]         │
│                                   │ localhost│  SimulationManager           │
│  applies commands (field, Msat…) ◄┼─────────┤    ▼ compute buffers          │
│                                   │ commands│  Renderer (instanced arrows)  │
└───────────────────────────────────┘         └───────────────────────────────┘
```

**Key insight: Unity doesn't care where the vectors come from.** The renderer,
the thread-safe data handoff, the command-queue pattern — all standard —
stay identical. Only the data source changes: instead of
`DllImport` + `Marshal.Copy`, a background thread reads frames from a pipe.

---

## 3. Background: Spirit vs. Mumax

| | Spirit (in-process DLL) | mumax3 (MumaxVR) |
|---|---|---|
| Physics | Atomistic: individual spins on a lattice | Continuum: magnetization averaged over finite-difference cells |
| Typical scale | 40×40 = 1,600 spins | 256×256×1 up to 512³ cells (65k – millions) |
| Language | C++ with C API | Go + CUDA kernels |
| GPU | Optional | **Required** (NVIDIA, compute capability ≥ 5.0) |
| Unit of visualization | One arrow per spin | One arrow per cell (or per slice/LOD region) |
| Key parameters | Exchange J, DMI D, field B, anisotropy K, temperature | Msat, Aex, Dind, Ku1, alpha, B_ext, spin-torque terms |
| License | MIT | GPLv3 |
| Integration | In-process DLL | Sidecar process + IPC |

What stays the same physically: both produce a grid of 3D unit vectors that we
render as colored arrows. The shader doesn't know the difference.

---

## 4. Components

### 4.1 The Bridge (`mumax-bridge/` in the project root, Go — separate program, outside `Assets\`)

A small Go program (~300–500 lines) that imports `github.com/mumax/3/engine`
and acts as the simulation server.

Responsibilities:
- Initialize the mumax engine (grid size, cell size, material parameters).
- Run the solver loop; every N steps copy the magnetization `M` from GPU to
  host memory (via the engine's `InjectAndWait` thread-safe channel).
- Serve one local connection (TCP on `127.0.0.1` or a named pipe):
  - **Outgoing — frames** (binary): header + raw `float32` array.
  - **Incoming — commands** (JSON lines): set parameters, run/pause, reset.
- Shut down cleanly when Unity disconnects.

Files:
```
mumax-bridge/
├── main.go        entry point, engine setup, solver loop
├── server.go      listener, connection lifecycle
├── protocol.go    frame encoding, command decoding
└── commands.go    command → engine.InjectAndWait(...) mapping
```

### 4.2 Unity side (`Assets/MumaxVR/`)

```
Assets/MumaxVR/
├── Scripts/
│   ├── MumaxBridgeClient.cs       starts mumax-bridge.exe, connects,
│   │                              reads frames on a background thread
│   ├── MumaxSimulationManager.cs  command queue in, locked Vector3[] out
│   ├── MumaxRenderer.cs           compute buffers + DrawMeshInstancedIndirect
│   │                              (GPU cull: physics filter, proximity LOD,
│   │                              box/lens/slice region of interest)
│   ├── MumaxUIManager.cs          VR sliders → JSON commands
│   └── OvfLoader.cs               (Phase 0 only) parse OVF files from disk
├── Shaders/
│   ├── MumaxCellShader.shader     instanced glyph shader (arrows + LOD sticks)
│   └── MumaxRoiHandle.shader      translucent grabbable ROI handle
├── Resources/
│   └── MumaxCull.compute          per-frame visibility: filter / LOD / ROI
├── Materials/
├── Scenes/
│   ├── Playback_OvfSamples.unity   bundled sample frames (default)
│   ├── Playback_MumaxOutput.unity  watch your own mumax³ .out folder
│   └── Live_MumaxBridge.unity      live GPU streaming via the bridge
└── StreamingAssets/
    └── mumax-bridge.exe           shipped alongside the build
```

### 4.3 Wire protocol (v1)

**Frame (Bridge → Unity), binary, little-endian:**

| Field | Type | Meaning |
|---|---|---|
| magic | uint32 | `0x4D583346` ("MX3F"), sanity check |
| nx, ny, nz | 3 × uint32 | grid dimensions |
| step | uint64 | solver step count |
| time | float64 | simulation time (s) |
| payload | float32 × nx·ny·nz·3 | magnetization vectors, x-fastest order |

Size check: 256×256×1 grid → ~786 KB/frame → 30 Hz ≈ 24 MB/s. Trivial for
localhost TCP/pipes. 128³ → ~25 MB/frame → stream at reduced rate or
downsampled (see §6 Risks).

**Command (Unity → Bridge), one JSON object per line:**
```json
{"cmd":"set",   "key":"B_ext",  "value":[0.0, 0.0, 0.05]}
{"cmd":"set",   "key":"alpha",  "value":0.02}
{"cmd":"run"}   {"cmd":"pause"}   {"cmd":"reset"}
{"cmd":"config","key":"skyrmion"}
{"cmd":"rate",  "value":10}        // solver steps per streamed frame
```

---

## 5. Roadmap

### Phase 0 — Prototype: prove the Unity side (no Go code yet)
*Goal: arrows moving in Unity, driven by real mumax output. ~1–2 sessions.*

- Run stock `mumax3.exe` with a script containing `autosave(m, 10e-12)`.
- Write `OvfLoader.cs`: watch the output folder, parse OVF2 files, fill
  `Vector3[]`.
- Copy `SpinRenderer` (from the sister project [SpiritVR](https://github.com/jinnyan/SpiritVR)) → `MumaxRenderer`, feed it the loaded frames.
- **Milestone: a mumax simulation plays back in the Unity editor.**
- This de-risks everything downstream and the OVF loader stays useful forever
  (loading saved simulations without a live engine).

### Phase 1 — The Bridge: live streaming
*Goal: replace file polling with a live connection.*

- Write the Go bridge: engine init, solver loop, frame streaming (no commands
  yet, hardcoded simulation).
- Write `MumaxBridgeClient.cs`: connect, read frames on a background thread,
  hand off via lock (background reader + locked handoff).
- **Milestone: live simulation streams into the Unity editor at ≥ 30 fps
  for a 256×256 grid.**

### Phase 2 — Interactivity: commands
*Goal: change the physics while it runs.*

- Command channel in bridge + client.
- `MumaxSimulationManager` with a command-queue pattern.
- Desktop debug UI first (sliders for B_ext, alpha, Msat, Aex, Dind;
  buttons for reset / preset configurations: random, vortex, skyrmion).
- **Milestone: drag a field slider, watch domains respond live.**

### Phase 3 — VR
*Goal: add the VR interaction layer.*

- Wire the XR rig (already in the scene) and `MumaxUIManager` to the command channel.
- Grab/scale/rotate the sample volume; hand-mounted parameter panel.
- **Milestone: full VR session — stand in the magnet, change the field.**

### Phase 4 — Scale & polish
*Goal: handle grids bigger than arrows can show.*

- Slice-plane mode (clip plane in shader, move it with your hand).
- LOD: downsample the grid on the bridge side (`"rate"`-style command) for
  distant/overview viewing.
- Optional: volume/color rendering mode (mz as color field, no arrows).
- Optional: live table quantities (energy, average m) as a floating HUD graph.

---

## 6. Risks & mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Go + CUDA build environment on Windows is fiddly | Blocks Phase 1 | mumax3 ships official Windows binaries, so the toolchain is proven; Phase 0 needs no Go at all |
| Big 3D grids overwhelm arrow rendering | Ugly/slow at 128³+ | Slice plane + LOD downsampling (Phase 4); start with 2D films where arrows work great |
| GPU→host copy every frame slows the solver | Lower solver throughput | `rate` command: stream every N steps; visualization doesn't need every step |
| Bridge crashes / orphaned process | Bad UX | Client owns the process handle, kills on exit; bridge exits when the socket closes |
| GPLv3 contamination | Legal | Bridge is a separate GPL process (its source published if ever distributed); Unity app stays independent across the process boundary |
| mumax⁺ (C++ rewrite) matures and offers a real C API | Rework | Architecture is source-agnostic: only `MumaxBridgeClient` would be replaced by a DLL binding; manager/renderer/UI untouched |

---

## 7. Requirements

- **Simulation machine:** NVIDIA GPU (compute capability ≥ 5.0), CUDA-capable
  driver. mumax3 binary or Go ≥ 1.22 + CUDA toolkit (≤ 12.9) to build the bridge.
- **Unity:** Unity 6.4 (6000.4.11f1) with URP + XR setup (already present in this project).
- Bridge and Unity run on the same machine (localhost only, v1). A remote
  simulation server is a natural later extension — the protocol already
  allows it.

---

## 8. Decision log

| Date | Decision | Why |
|---|---|---|
| 2026-07-02 | No in-process DLL; sidecar process + IPC instead | mumax3 has no C API, singleton Go engine, GPLv3 (see §2) |
| 2026-07-02 | mumax3 over mumax⁺ | mumax3 is mature and feature-complete; mumax⁺ Windows build is unstable, API not settled |
| 2026-07-02 | Phase 0 uses OVF file polling | Zero new tooling, proves the whole Unity pipeline in hours (same approach ubermag uses) |
| 2026-07-02 | Binary frames + JSON commands | Frames are hot-path (compact binary); commands are rare (debuggable JSON) |
| 2026-07-02 | `Assets/MumaxVR` fully self-contained (own shader, no external asset references) | The visualizer stays independent of any host project; it is just `Assets/MumaxVR` + `mumax-bridge/` + `OvfSamples/` |
