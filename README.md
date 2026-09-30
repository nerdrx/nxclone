# nxclone

Independent, source-first VRChat avatar clone builder targeting Unity 2022.3 on Linux and Windows. No login, license server, vendor DLL, or vendor assets.

## Install

Add `Packages/dev.nx.nxclone` as a local package in VRChat Creator Companion or Unity Package Manager. Requires VRChat Avatars SDK 3.7.3 or newer. Open **Tools > nxclone**.

## Avatar requirements

- Scene avatar root with a `VRCAvatarDescriptor`, valid `Animator`, and humanoid rig.
- Clone source with an identical transform hierarchy to the root. The default source is the root itself. Different meshes are fine if bone paths match.
- An FX controller asset and Expressions Menu/Parameters assets are required for the in-game toggle. The builder creates copies of those assets; originals stay untouched.
- Free expression menu slot and one free Bool expression parameter. The preflight displays exact blockers.
- PC avatar shader support for the translucent afterimages. Quest needs a separate mobile shader.

Choose clone count and offset. Optionally enable afterimages, choose count, dampening, and color. **Generate** creates a new scene avatar and a new generated assets folder under `Assets/nxclone-generated`. Inspect the avatar and test in Unity/VRChat before uploading. Generation can multiply mesh and constraint counts substantially.

Afterimage delay is constraint feedback: it is frame-rate dependent, not a fixed number of milliseconds. Existing blendshape and object-toggle animations are not automatically copied to the clones. This package does not implement recording, playback, limb attachment, or Final IK.

## Distribution

This repository contains only original source. Do not commit purchased `.unitypackage` files, vendor DLLs, or avatar assets. Local VPM installation works from the folder; publishing a public VPM listing needs a hosted zip URL and repository index.

## Validation status

The editor source compiles against local Unity 2022.3.22f1 and VRChat SDK 3.10.5 assemblies. Full Unity batch import and VRChat upload remain unverified because the local Unity licensing client exits before project load. Test on a copy of your avatar before release.
