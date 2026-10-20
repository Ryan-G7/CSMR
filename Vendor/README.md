# Pinned dependencies

`MRTK/` contains the six packages used by CarAR, including their licenses and
Unity compatibility changes, as ordinary repository files. No submodule update
is needed.

Source: https://github.com/MixedRealityToolkit/MixedRealityToolkit-Unity

Local snapshot: `e54736851ec6ed7768e628d0791c43d5e8826571`, based on upstream
`acdec2d5` (see `CarAR/MRTK3-MIGRATION.md` for compatibility notes).

MRTK is BSD-3-Clause; see `MRTK/LICENSE.md` and each package's license notices.
CarAR also embeds XRI 2.6.5 and Graphics Tools 0.8.0 under `CarAR/Packages`;
their source and licenses are included there. Unity registry dependencies and
exact resolved versions are recorded in `CarAR/Packages/packages-lock.json`.
