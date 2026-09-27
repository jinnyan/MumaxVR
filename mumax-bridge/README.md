# mumax-bridge

Sidecar process for **MumaxVR**: runs a mumax3 micromagnetic simulation
in-process (Go + CUDA) and streams magnetization frames to Unity over localhost
TCP. Architecture: [`docs/MumaxVR_DesignDoc.md`](../docs/MumaxVR_DesignDoc.md) §4.

> **No prebuilt binaries ship in this repo.** `mumax-bridge.exe` and the CUDA
> runtime DLLs (`cufft64_*.dll` ~277 MB, `curand64_*.dll`) are **not** included —
> they are GPLv3 / NVIDIA-proprietary and the cuFFT DLL alone exceeds GitHub's
> 100 MB file limit. **You build the bridge yourself** (see below), and it only
> runs on a machine with a compatible NVIDIA GPU + CUDA.
>
> **You do NOT need any of this to use MumaxVR.** The Unity app runs out of the
> box in *file-playback* mode on the bundled sample data (`OvfSamples/`) — see
> [`../Assets/MumaxVR/SETUP.md`](../Assets/MumaxVR/SETUP.md). The bridge is only
> for *live* GPU streaming.

It lives beside the Unity project but deliberately **outside `Assets/`**: it is a
separate program, not a Unity asset. That process boundary is also the **license
boundary** — mumax3 is GPLv3, so this folder (which links the mumax3 engine) is
GPLv3; the Unity app never links it, it only reads the socket. See
[`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).

---

## Which versions do I need? (read this first)

The bridge is thin — it just imports `github.com/mumax/3/engine`. **All the real
constraints come from mumax3 itself.** So the one rule that always works:

> **If plain mumax3 builds and runs on your machine, this bridge builds and runs
> too — it uses the identical Go + CUDA toolchain.**

Get mumax3 working first, then point the bridge at that same setup.

### The four prerequisites (from mumax3 upstream)

| Component | Requirement | How to check yours |
|---|---|---|
| **NVIDIA GPU** | Compute capability ≥ 5.0 | `nvidia-smi --query-gpu=compute_cap --format=csv` |
| **NVIDIA driver** | Recent enough for your CUDA toolkit | `nvidia-smi` (top-right shows max CUDA it supports) |
| **CUDA Toolkit** | **≤ 12.9**, and ≤ what `nvidia-smi` reports | `nvcc --version` |
| **Go** | ≥ 1.22 | `go version` |
| **C compiler (cgo)** | Windows: MinGW-w64 gcc *or* MSVC · Linux: GCC | `gcc --version` |

Pick your CUDA toolkit as `min(12.9, max-CUDA-from-nvidia-smi)`. Each CUDA
version also caps the GCC version it accepts — if your GCC is too new, mumax3's
docs cover `NVCC_CCBIN` / choosing an older GCC.

### Telling the toolchain where CUDA is

- **Windows:** set `CUDA_PATH` to the toolkit directory (mumax3's cgo flags look
  there). Some setups expect it at `C:\cuda` — if the build can't find CUDA
  headers/libs, set `CGO_CFLAGS` / `CGO_LDFLAGS`, or symlink the toolkit there.
- **Linux:** CUDA at `/usr/local/cuda/` (symlink if installed elsewhere).
- **Compute capability:** mumax3 selects kernels via `CUDA_CC`, e.g. `CUDA_CC=89`
  for an RTX 4090 (CC 8.9). Match it to the number from `nvidia-smi` above.

### Runtime DLLs (Windows)

mumax-bridge.exe needs the CUDA runtime libraries `cudart64_*.dll`,
`cufft64_*.dll` and `curand64_*.dll` at run time. They are **not** shipped here
(they are NVIDIA's, and cuFFT alone is ~277 MB). Grab them from your CUDA Toolkit's `bin\` folder — or from a
mumax3 release built for your CUDA version — and put them next to
`mumax-bridge.exe` (or anywhere on `PATH`). The version numbers in the filenames
must match the CUDA toolkit you built against.

### Verified-with (this bridge's known-good config)

Go 1.22.4 · mumax3 commit [`f656494`](https://github.com/mumax/3/commit/f656494b29516bead825b444b1f0b38c6e6c7dbf)
(module-enabled, `go.mod` present) · CUDA 12.6 runtime (`cudart64_12.dll`,
`cufft64_11.dll`, `curand64_10.dll`) · NVIDIA RTX 4090 (CC 8.9) · Windows 10 ·
MinGW-w64 gcc for cgo.

Authoritative, always-current matrix: **mumax3's own build docs** —
<https://github.com/mumax/3> (README) and <https://mumax.github.io>.

---

## Build

```
# 1. Clone mumax3 into the repo root as <repo>/mumax3-src (matches go.mod's
#    replace). Run this from INSIDE mumax-bridge/ - hence the "../":
git clone https://github.com/mumax/3 ../mumax3-src
git -C ../mumax3-src checkout f656494b29516bead825b444b1f0b38c6e6c7dbf   # tested commit (optional)

# 2. Build (PATH must have go + gcc; CUDA_PATH/CUDA_CC set as above)
./build.ps1                 # Windows PowerShell
# or, cross-platform:
CGO_ENABLED=1 go build -o mumax-bridge.exe .

# 3. Put the CUDA runtime DLLs next to the exe (see "Runtime DLLs" above) —
#    without them the exe builds fine but won't start:
#    cufft64_*.dll, curand64_*.dll, cudart64_*.dll  from your toolkit's bin\
```

`build.ps1` checks that `go`, `gcc` and `../mumax3-src` are present, then sets
`CGO_ENABLED=1`, runs `go mod tidy`, and `go build`. If
mumax3 has no `go.mod` in your clone, run `go mod init github.com/mumax/3 &&
go mod tidy` inside `mumax3-src` first.

**The build is not done until step 3.** `go build` only links against the CUDA
*import libraries*; the matching runtime DLLs must sit next to `mumax-bridge.exe`
(or on `PATH`) or it dies at startup with a missing-DLL error. Once
`mumax-bridge.exe` and its DLLs are in this folder, Unity's `Live_MumaxBridge`
scene launches it automatically — nothing else to configure.

## Run

```
mumax-bridge.exe -nx 256 -ny 256 -nz 1 -cell 4e-9 -steps 25 -port 35999
```

Waits for one client (Unity's `MumaxBridgeClient`), then streams frames. Exits
when the client disconnects — no orphaned GPU processes. First start takes ~1 min
(demag kernel + `relax()`).

Flags: `-nx/-ny/-nz` grid cells · `-cell` cell size (m) · `-steps` solver steps
per streamed frame · `-port` localhost TCP port. The simulation itself (a
current-driven Néel skyrmion) is set up in [`main.go`](main.go); the equivalent
standalone script is [`scripts/reference_film.mx3`](scripts/reference_film.mx3).

## API used against the mumax3 engine

`cuda.Init(int)` · `engine.InitIO(inputfile, od, force)` · `engine.Close()` ·
`engine.Eval(line)` (sequential on the main goroutine — the `mumax3 -i` pattern) ·
`engine.M.Buffer().HostCopy()` → `data.Slice.Vectors()` `[3][z][y][x]float32` ·
`engine.Time` (float64, s). These are stable on mumax3 `master`.

## Protocol v1 (little-endian)

| field | type |
|---|---|
| magic `0x4D583346` "MX3F" | uint32 |
| nx, ny, nz | 3 × uint32 |
| step | uint64 |
| sim time (s) | float64 |
| payload, x-fastest (mx,my,mz per cell) | float32 × nx·ny·nz·3 |

Drop-stale policy: an undelivered frame is replaced by the newest one; the solver
never blocks on a slow client. A JSON command channel (Unity → bridge) on the
same connection is the planned next step.
