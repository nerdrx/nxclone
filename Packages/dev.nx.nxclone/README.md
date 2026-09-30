# nxclone

Independent VRChat clone and afterimage builder for Unity 2022.3. No login or license server.

[Install and usage guide](https://github.com/nerdrx/nxclone#avatar-requirements) · [Validation and limits](https://github.com/nerdrx/nxclone/blob/main/docs/PARITY.md)

Requires Avatars SDK 3.10.2+. Open **Tools > nxclone**. Empty source/anchor fields use the root avatar. Missing FX and expression assets are created on the generated copy; original assets stay unchanged. Final extraction runs after VRCFury Armature Link, before its parameter compressor.

Regenerate from your original avatar when updating. Controls include visibility, world drop, freeze, scale, posing, IK, contacts, recording, independent FX, gestures and wearing. New clone slots default to 1 m forward with 180° yaw. Optional in-game XYZ dials adjust ±2 m around setup placement. Flat afterimages follow the main avatar at zero offset, with per-trail colours, camera proximity fade, an independent toggle and primary-body masking.

Linux native tests cover constraints, capture/replay and SDK preprocessing. Live VRChat and Windows need validation; editor SDK stubs do not execute all runtime behaviors. Afterimage lag is frame-dependent and uses geometry silhouettes. Clone counts, samples and contact trackers increase avatar cost.

Original editor code and licensed MIT VRLabs Contact Tracker assets are included. No purchased packages, vendor DLLs or avatar meshes are distributed.
