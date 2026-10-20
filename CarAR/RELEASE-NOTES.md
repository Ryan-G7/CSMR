# Windows preview · v0.1.0-preview.20261020

The Windows x64 player is published as `CarAR-Windows-x64.zip` in GitHub Releases.
The launcher's pinned URL and SHA-256 are in `Launch/player-release.json`.
No Unity editor or Blender is needed to run this archive. MR additionally needs
the manufacturer's headset, camera drivers, AIXR Agent and OpenXR runtime.

This release was built from a fresh clone in a separate directory, without the
original Unity Library, original MRTK checkout, temporary Python environments or
locally installed vendor SDK binaries. The six required MRTK packages resolve to
the repository's `Vendor/MRTK` snapshot. Large source/model assets resolve through
Git LFS. The original Blender source SHA-256 remains
`5e20325bf38774c679fef495089ea6e6c75d005584120474c6cacef914fb8636`.

Validation on Windows with Unity 6000.6.3f1:

- `Tools/Build-Windows.ps1 -Smoke`: exit code 0.
- All four suites passed: MRTK manipulation, native Input System controller rays,
  review regressions (including the recorded 14.7m tracking discontinuity and
  missing door trim), and full vehicle animation / explode-and-assemble.
- The archive contains the complete 372-file player, including UnityPlayer,
  Mono runtime, plugins and `CarAR_Data`; local logs, screenshots, timing trials
  and Unity's build backup folder are excluded.
- After publication, a new HTTPS clone from GitHub passed `git lfs fsck`,
  resolved all six local MRTK packages, and matched the source/model hashes.
  The Windows PowerShell launcher downloaded the public release, verified its
  checksum and installed it in an empty directory. The downloaded player passed
  all four runtime suites with exit code 0; a second launcher call reused the
  installation without downloading again.

Existing Unity editor assembly-analysis and JobTempAlloc shutdown diagnostics
remain in build logs. These checks do not certify headset registration accuracy,
real-world lighting or hardware-independent MR support.

Before the initial GitHub publication, unpushed Git history was rewritten to
remove committed caches, logs and inspection environments and migrate large
assets to LFS. Commit IDs in older local review notes refer to that earlier
history; the full original history and index were backed up locally before the
rewrite. Runtime source and authored model bytes were preserved.
