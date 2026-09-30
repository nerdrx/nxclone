# nxclone

Independent, source-first VRChat avatar clone builder targeting Unity 2022.3 on Linux and Windows. No login, license server, vendor DLL, or vendor assets.

## Install

In VRChat Creator Companion, add the repository URL `https://nerdrx.github.io/nxclone/index.json`, then add `nxclone` to an avatar project. For local development, add `Packages/dev.nx.nxclone` as a user package. Requires VRChat Avatars SDK 3.10.2 or newer. Open **Tools > nxclone**.

## Avatar requirements

- Scene avatar root with a `VRCAvatarDescriptor`, valid `Animator`, and humanoid rig.
- Each clone source needs matching bone paths. An empty source field uses the root avatar.
- Missing FX controller, Expressions Menu, and Expression Parameters assets are created on the generated avatar copy. Existing custom assets are copied; originals stay untouched.
- Existing custom Expressions Menu and Parameters assets need one free menu slot. Controls use one bit for visibility, one per clone for world drop, one per clone for pose freeze, and eight for the optional scale dial when enabled. The avatar check displays exact blockers.
- PC avatar shader support for the translucent afterimages. Quest needs a separate mobile shader.

Configure up to four clones with separate source, offset, scale, X mirror, and root or body-bone attachment. Optionally enable world drop, pose freeze, viseme copying, root FX visual mirroring, an in-game scale dial, and afterimages. Layout presets save these options but cannot store scene source references. **Generate scene copy** creates a new scene avatar and generated assets under `Assets/nxclone-generated`. Inspect and test it before uploading. Generation can multiply mesh and constraint counts substantially.

Afterimage delay is constraint feedback: it is frame-rate dependent, not a fixed number of milliseconds. FX visual mirroring works for root-source clones and renderer, blendshape, and object-toggle curves. Motion recording/playback, interactive limb posing, upload-time application, and Final IK are not implemented in this preview.

## Distribution

This repository contains only original source. Do not commit purchased `.unitypackage` files, vendor DLLs, or avatar assets. The VPM index points to the release zip.

## Validation status

The editor source compiles against Unity 2022.3.22f1 and VRChat SDK 3.10.2. Isolated Linux Unity tests generated a copy of the user's Nixomi avatar with world drop, pose freeze, scale, visemes, and afterimages; separate smoke tests checked FX curve mirroring and preservation of source clips. The game runtime and Windows editor remain untested. Test on a copy of your avatar before release.
