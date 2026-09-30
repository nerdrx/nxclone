# nxclone

Independent VRChat clone and afterimage builder for Unity 2022.3. No login or license server.

[Install and usage guide](https://github.com/nerdrx/nxclone#avatar-requirements) · [Validation and limits](https://github.com/nerdrx/nxclone/blob/main/docs/PARITY.md)

Requires Avatars SDK 3.10.2+. Open **Tools > nxclone**. Empty source/anchor fields use the root avatar. Missing FX and expression assets are created on the generated copy; original assets stay unchanged. Final extraction runs after VRCFury Armature Link, before its parameter compressor.

Regenerate from your original avatar when updating. Controls include visibility, world drop, freeze, scale, posing, IK, contacts, recording, independent FX, gestures and wearing. New clone slots default to 1 m forward with 180° yaw. Optional in-game XYZ dials adjust ±2 m around setup placement; pitch/yaw/roll dials add ±180° around successive local axes. Choose axes independently for position and rotation. Defaults select Distance (Z) and Yaw (Y). Each enabled axis costs 8 synced bits per clone and is neutral at 0.5. Flat afterimages follow the main avatar at zero offset, with per-trail colours, camera proximity fade, an independent toggle and primary-body masking.

Linux native tests cover constraints, capture/replay and SDK preprocessing. Live VRChat and Windows need validation; editor SDK stubs do not execute all runtime behaviors. Afterimage lag is frame-dependent and uses geometry silhouettes. Clone counts, samples and contact trackers increase avatar cost.

Original editor code and licensed MIT VRLabs Contact Tracker assets are included. No purchased packages, vendor DLLs or avatar meshes are distributed.

Root slots default to **DancePartner** movement: steps follow the clone’s facing axes and turns happen around its own root. Hide/show captures a fresh origin. Choose **Attached** to follow the avatar as an attached object; bone/custom anchors always use attachment. Dance mode adds three constraints per clone, without new parameters.

Use **Afterimages only** for trails without a clone, or remove all clone slots and enable afterimages. The standalone Afterimages toggle costs one synced bit. Clone-only controls are hidden and emit no parameters, even if a previous setup had them enabled.
