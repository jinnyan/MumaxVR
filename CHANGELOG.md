# Changelog

All notable changes to MumaxVR are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-09-28

First public release.

### Added

- **File playback** of mumax³ / OOMMF `.ovf` output, with 48 bundled sample frames of a moving
  skyrmion (`Playback_OvfSamples` scene) and play / pause / scrub controls.
- **Live-follow** of a mumax³ `.out` folder while the simulation is still writing frames
  (`Playback_MumaxOutput` scene).
- **Live GPU streaming** from a running mumax³ simulation through `mumax-bridge`, a Go sidecar
  that embeds the mumax³ engine and sends frames to Unity over localhost TCP
  (protocol v1, drop-stale; `Live_MumaxBridge` scene). The bridge is built by the user.
- **GPU-instanced rendering** of one arrow per cell, with a compute-shader culling pass and two
  indirect draws (arrows near, sticks farther away).
- **Readability tools:** physics filter that hides uniform (bulk-domain) cells, proximity LOD,
  and a grabbable region of interest (box, lens or slice plane).
- **Color modes:** *m<sub>z</sub>* red/blue and hue wheel.
- **OpenXR rig** in every scene for viewing in any OpenXR headset.
- In-app **legal notice** (copyright and license) in the desktop view.

[1.0.0]: https://github.com/jinnyan/MumaxVR/releases/tag/v1.0.0
