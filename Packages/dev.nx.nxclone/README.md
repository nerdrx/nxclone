# nxclone

Independent, source-first VRChat avatar clone builder targeting Unity 2022.3 on Linux and Windows. No login, license server, vendor DLL, or vendor assets.

## Install

In VRChat Creator Companion, add the repository URL `https://nerdrx.github.io/nxclone/index.json`, then add `nxclone` to an avatar project. For local development, add `Packages/dev.nx.nxclone` as a user package. Requires VRChat Avatars SDK 3.7.3 or newer. Open **Tools > nxclone**.

## Avatar requirements

- Scene avatar root with a `VRCAvatarDescriptor`, valid `Animator`, and humanoid rig.
- Clone source with an identical transform hierarchy to the root. The default source is the root itself. Different meshes are fine if bone paths match.
- Missing FX controller, Expressions Menu, and Expression Parameters assets are created on the generated avatar copy. Existing custom assets are copied; originals stay untouched.
- Existing custom Expressions Menu and Parameters assets need one free menu slot and one free Bool parameter bit. The avatar check displays exact blockers.
- PC avatar shader support for the translucent afterimages. Quest needs a separate mobile shader.

Choose clone count and offset. Optionally enable afterimages, choose count, dampening, and color. **Generate scene copy** creates a new scene avatar and a new generated assets folder under `Assets/nxclone-generated`. Inspect the avatar and test in Unity/VRChat before uploading. Generation can multiply mesh and constraint counts substantially.

Afterimage delay is constraint feedback: it is frame-rate dependent, not a fixed number of milliseconds. Existing blendshape and object-toggle animations are not automatically copied to the clones. This package does not implement recording, playback, limb attachment, or Final IK.

## Distribution

This repository contains only original source. Do not commit purchased `.unitypackage` files, vendor DLLs, or avatar assets. The VPM index points to the release zip.

## Validation status

The editor source compiles against local Unity 2022.3.22f1 and VRChat SDK 3.10.5 assemblies. An isolated Linux Unity import reached asset import but did not finish within the test timeout. VRChat upload and Windows editor behavior remain unverified. Test on a copy of your avatar before release.
