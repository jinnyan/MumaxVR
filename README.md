<div align="center">

# MumaxVR

**Real-time virtual-reality visualization for micromagnetic simulations**

A Unity/OpenXR front end for [mumax³](https://mumax.github.io).

[![Unity 6.4](https://img.shields.io/badge/Unity-6000.4-000000?logo=unity&logoColor=white)](https://unity.com/releases/editor)
[![Platform: Windows x64](https://img.shields.io/badge/platform-Windows_x64-0078D6?logo=windows&logoColor=white)](#requirements)
[![VR: OpenXR](https://img.shields.io/badge/VR-OpenXR-1a7f37)](#requirements)
[![License: AGPL-3.0 + GPL-3.0](https://img.shields.io/badge/license-AGPL--3.0_+_GPL--3.0-3b82f6)](#license)

<br>

<sub><i>Screenshots and demo animations are coming soon.</i></sub>

</div>

---

## About

MumaxVR renders [mumax³](https://mumax.github.io) micromagnetic simulations stereoscopically in VR. Every grid cell is drawn as a GPU-instanced arrow colored by its m<sub>z</sub> component; a physics filter, distance LOD and a grabbable region of interest (box, lens or slice) keep dense volumes readable. It plays back saved `.ovf` output — sample data is bundled — or streams a running GPU simulation through the `mumax-bridge` sidecar.

Built on [mumax³](https://mumax.github.io) by the DyNaMat group, Ghent University, and contributors. Rendering uses Unity's Universal Render Pipeline and the OpenXR-based XR Interaction Toolkit.

## Requirements

| | |
|---|---|
| **Unity** | Unity 6.4 (developed with **6000.4.11f1**) or newer — URP + XR Interaction Toolkit (packages auto-resolve on open) |
| **OS** | **Windows x64** |
| **GPU** | DirectX 11-class GPU with compute-shader support for the instanced arrow renderer (any vendor) |
| **VR** *(optional)* | Any OpenXR headset (SteamVR, Meta, WMR, …); the scenes ship with an XR Origin rig |
| **Live mode** *(optional)* | NVIDIA GPU (compute ≥ 5.0) + the mumax³ build toolchain (Go ≥ 1.22, CUDA ≤ 12.9, a C compiler) |

## Quick start

1. Open the project in **Unity 6.4 (6000.4.11f1)** or newer.
2. Open `Assets/MumaxVR/Scenes/Playback_OvfSamples.unity`.
3. Press **Play**.

48 synthetic skyrmion frames play back as a 64 × 64 arrow grid; the playback and rendering panels are shown top-left in the Game view. To view in a headset, connect an OpenXR device before pressing *Play*.

| Scene | Use |
|---|---|
| `Playback_OvfSamples` | Bundled sample data — no setup. |
| `Playback_MumaxOutput` | Plays back or live-follows a mumax³ `.out` folder. Preset to `mumax-bridge/scripts/reference_film.out`, which you create by running `reference_film.mx3` with mumax³ — or set your own folder in the Inspector. |
| `Live_MumaxBridge` | Streams a running GPU simulation; see [Live mode](#live-mode). |

**Full setup and troubleshooting → [`Assets/MumaxVR/SETUP.md`](Assets/MumaxVR/SETUP.md).**

## Live mode

The `Live_MumaxBridge` scene streams a running mumax³ simulation through the [`mumax-bridge`](mumax-bridge) sidecar. It needs an NVIDIA GPU and a one-time build of the bridge; the executable and NVIDIA's CUDA DLLs are not included in this repository. Without the build, the scene reports `bridge exe not found` — this is expected. Build instructions → [`mumax-bridge/README.md`](mumax-bridge/README.md).

The demo simulation drives a skyrmion with a spin-polarized current. After a few seconds it reaches the sample edge and disappears; the remaining uniform state is hidden by the physics filter. Untick **physics filter** to see it, or restart *Play*.

## Limitations

- Windows x64 only; live mode additionally needs an NVIDIA GPU and a self-built bridge.
- Tested in the Unity Editor only; sample data and the bridge are located relative to the project folder, so standalone builds are not supported yet.
- The controls are desktop panels in the Game view; there is no in-VR control panel yet.
- Live simulations can be watched, but not yet steered from VR.

## Roadmap

- In-VR control panel
- Steering live simulations (field, temperature, material) from VR
- Filter hysteresis, so cells at the threshold stop blinking
- Smooth transition between arrows and sticks at the LOD cutoff
- Combinable regions of interest

## How to cite

If MumaxVR contributes to published work, please cite this software and mumax³ — and, if you use the bundled interfacial-DMI example, Mulkers et al. (see [References](#references)). Citation metadata is provided in [`CITATION.cff`](CITATION.cff).

## Contributing

Issues and pull requests are welcome; see [`CONTRIBUTING.md`](CONTRIBUTING.md).

## License

© 2026 Janine Graser and Dr. Thomas Schlake. Licensed under AGPL-3.0-or-later ([`LICENSE`](LICENSE)); `mumax-bridge/` is licensed under GPL-3.0-or-later because it links the GPLv3 mumax³ engine ([`mumax-bridge/LICENSE`](mumax-bridge/LICENSE)). Redistributions must keep the attribution "Based on MumaxVR by Janine Graser and Dr. Thomas Schlake" (AGPL/GPL §7(b); see [`NOTICE`](NOTICE)).

mumax³ and NVIDIA's CUDA libraries are not included; see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for all third-party components.

## References

1. A. Vansteenkiste *et al.*, *The design and verification of MuMax3*, **AIP Advances 4**, 107133 (2014). <https://doi.org/10.1063/1.4899186>
2. J. Mulkers, B. Van Waeyenberge, M. V. Milošević, *Effects of spatially-engineered Dzyaloshinskii–Moriya interaction in ferromagnetic films*, **Phys. Rev. B 95**, 144401 (2017). <https://doi.org/10.1103/PhysRevB.95.144401>

<div align="center"><sub>An independent visualization tool. Not affiliated with, or endorsed by, the mumax³ project.</sub></div>
