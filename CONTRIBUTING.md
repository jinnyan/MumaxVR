# Contributing to MumaxVR

Thank you for your interest in MumaxVR! Bug reports, ideas and pull requests are welcome.

## Reporting bugs

Please [open an issue](https://github.com/jinnyan/MumaxVR/issues) and include:

- your **Unity version** (the project is developed with 6000.4.11f1),
- your **headset and OpenXR runtime** (SteamVR, Meta, WMR, …), or "desktop only",
- the **scene** you were running (`Playback_OvfSamples`, `Playback_MumaxOutput` or `Live_MumaxBridge`),
- the relevant **Console output**, and
- for live mode also your **GPU, CUDA and Go versions** plus the `[bridge]` lines from the Console.

## Suggesting features

Open an issue describing the use case. For larger changes, please discuss the design in an
issue first — the [Roadmap](README.md#roadmap) lists what is already planned.

## Development setup

1. Open the project with **Unity 6000.4.11f1** (or a newer Unity 6.4 editor).
2. Start with `Assets/MumaxVR/Scenes/Playback_OvfSamples.unity` — it needs no GPU and no build.
3. Live mode requires building the bridge yourself — see [`mumax-bridge/README.md`](mumax-bridge/README.md).

Full walkthrough: [`Assets/MumaxVR/SETUP.md`](Assets/MumaxVR/SETUP.md).

## Pull requests

- Keep each pull request focused on one change and describe what it does and why.
- Match the style of the surrounding code (C# in `Assets/MumaxVR/Scripts/`, Go in `mumax-bridge/`).
- Test in the Editor: press *Play* in the affected scene(s) and check that the Console is free of errors.
- Commit the `.meta` files of any assets you add, and keep Unity's text serialization.
- Do not commit generated folders or build output (`Library/`, `Temp/`, `Logs/`, `*.csproj`,
  `mumax-bridge.exe`, CUDA DLLs, `mumax3-src`) — `.gitignore` already covers them.

## License of contributions

MumaxVR is licensed under the **GNU AGPL-3.0-or-later**; `mumax-bridge/` is licensed under the
**GNU GPL-3.0-or-later** because it links the GPLv3 mumax³ engine. By submitting a contribution, you
agree that it is licensed under the license of the part of the repository it belongs to, including
the additional attribution term described in [`NOTICE`](NOTICE).

- **Keep existing notices intact:** the copyright headers at the top of source files, the
  [`NOTICE`](NOTICE) file and the legal notice shown in the application (`MumaxLegalNotice.cs`).
- **New source files** start with a header like this (use `GPL-3.0-or-later` inside `mumax-bridge/`):

  ```
  // Copyright (C) <year> <your name>
  // SPDX-License-Identifier: AGPL-3.0-or-later
  ```

- **Third-party code** may only be added if its license is compatible with the AGPL/GPL. Add it to
  [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) together with its license text.

## Code of conduct

Please be respectful and constructive in issues, pull requests and discussions.
