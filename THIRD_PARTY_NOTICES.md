# Third-party notices

MumaxVR links or redistributes the following third-party components. Each is the
property of its respective authors and is used under its own license.

---

## mumax3 (NOT bundled — linked by `mumax-bridge/` at build time)

GPU micromagnetic simulator — <https://github.com/mumax/3>. Licensed under
**GPL-3.0**. mumax3 source is not included in this repository; you clone it
yourself when building the bridge. Because `mumax-bridge/` links mumax3, that
subdirectory is distributed under GPL-3.0-or-later — see
[`mumax-bridge/LICENSE`](mumax-bridge/LICENSE) and
[`mumax-bridge/NOTICE`](mumax-bridge/NOTICE).

If you use mumax3 in published work, cite:

> A. Vansteenkiste, J. Leliaert, M. Dvornik, M. Helsen, F. Garcia-Sanchez, and
> B. Van Waeyenberge, *The design and verification of MuMax3*,
> AIP Advances **4**, 107133 (2014). https://doi.org/10.1063/1.4899186

## CUDA runtime libraries (NOT bundled)

The bridge needs NVIDIA's `cudart64_*.dll`, `cufft64_*.dll` and
`curand64_*.dll` at run time. These are NVIDIA's property, governed by the CUDA
Toolkit EULA. They are not included in this repository (cuFFT alone is ~277 MB,
above GitHub's file-size limit, and the versions must match the toolkit you
build against) — copy them from your own CUDA Toolkit installation.

## Unity assets (`Assets/Samples/`, `Assets/TextMesh Pro/`)

The XR Interaction Toolkit and XR Hands sample assets and the TextMesh Pro
essentials are © Unity Technologies and are redistributed under the
**Unity Companion License**
(<https://unity.com/legal/licenses/unity-companion-license>).

## Liberation Sans

The font `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` (shipped with TextMesh
Pro) is licensed under the **SIL Open Font License 1.1**. The full license text
is included next to the font as
[`LiberationSans - OFL.txt`](Assets/TextMesh%20Pro/Fonts/LiberationSans%20-%20OFL.txt).
