<p align="center">
  <img src="docs/assets/nxclone-banner.svg" width="100%" alt="nxclone — avatar clones and translucent afterimages" />
</p>

<p align="center">
  <code>0.2.1 preview</code> &nbsp; <code>Unity 2022.3</code> &nbsp; <code>PC VRChat</code> &nbsp; <code>VPM</code>
</p>

nxclone is a source-first editor helper for VRChat avatar clones. It checks avatar requirements before generating a scene copy with optional translucent afterimages and independent FX toggles assembled during upload. It does not ask you to log in or contact a license server.

<h2 align="center"><a href="https://nerdrx.github.io/nxclone/#install">Install nxclone</a></h2>
<p align="center">
  ALCOM / Creator Companion<br /><br />
  <a href="https://github.com/nerdrx/nxclone/releases/tag/v0.2.1">Download preview</a> &nbsp;·&nbsp;
  <a href="docs/INSTALL.md">Installation help</a>
</p>

Add the nxclone repository, then install **nxclone** in your avatar project. The current preview is **0.2.1**.

<details><summary>Manual VPM repository URL</summary>

```text
https://nerdrx.github.io/nxclone/index.json
```

</details>

---

## Features

| Feature | What it does |
| :--- | :--- |
| **Avatar check** | Explains rig, bone path, parameter space, and menu space blockers before generation. |
| **Clone layout** | Configures up to four clones independently: scene source, offset, scale, X mirror, and root or body-bone attachment. Layout presets save reusable settings. |
| **Runtime controls** | Adds independent clone and afterimage visibility toggles (off on spawn), optional per-clone world drop and pose freeze, and an optional in-game scale dial. |
| **Expressions** | Copies compatible blendshape visemes and mirrors renderer, blendshape, and object-toggle curves from the root FX controller onto root-source clones. |
| **Afterimages** | Adds up to four flat, single-color translucent silhouettes with their own toggle. The main avatar silhouette takes priority. Constraint feedback adjusts the lag. The lag varies with frame rate. |
| **Generated assets** | Creates missing FX, expression menu, and parameter assets on the scene copy; copies existing custom assets. Originals stay unchanged. |
| **Local workflow** | Uses source code and Unity/VRChat SDK APIs. No vendor DLL, purchased asset, account login, or network step. |

## Avatar requirements

- Scene avatar root with a `VRCAvatarDescriptor`, valid `Animator`, and humanoid rig.
- Each clone source needs the same bone hierarchy as the root. Leaving its source empty uses the root avatar.
- One free expression menu slot. Controls use one parameter bit for clone visibility, one for afterimage visibility, one per clone for world drop, one per clone for pose freeze, and eight for the scale dial when those options are enabled. Missing expression assets are created automatically.
- PC shader support for translucent afterimages. Quest needs a separate mobile shader.

Open **Tools → nxclone**, choose the avatar and options, review the check, then select **Generate scene copy**. The scene copy contains hidden preview meshes and an nxclone setup component. Final meshes and controls assemble during SDK upload after VRCFury Armature Link and other avatar tools finish. Regenerate from the original avatar when updating from 0.2.0. Clones multiply mesh and constraint counts; [VRChat's PC performance ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/) list 350 constraints as the upper Poor threshold.



Flat afterimages use geometry silhouettes: textures, alpha-cutout holes, and material displacement are not reproduced. The shaders reserve stencil bits 0 and 1; world shaders or camera effects using those bits can interfere. Opaque world depth still occludes the trail. Afterimages and their invisible main-avatar mask share a toggle and stay off on spawn.

## Current scope

This preview does not implement motion recording/playback, interactive limb posing, or Final IK. Presets store layout and options; scene source references must be reselected when loading a preset. FX curve mirroring applies only when a clone uses the root avatar as its source. Afterimage delay uses constraint feedback, not a fixed duration. [VRLabs explains the feedback method.](https://github.com/VRLabs/Damping-Constraints)

The editor source compiles against Unity 2022.3.22f1 and VRChat SDK 3.10.2. Isolated Linux Unity tests passed full SDK preprocessing on the Nixomi avatar, checked linked mesh/bone mappings, and executed clone/afterimage toggles, world drop, pose freeze, and scale through its generated Animator. Additional checks preserve source clips and keep material swaps out of silhouette shaders. An offscreen GPU render under headless Gamescope verified uniform opacity and main-avatar silhouette priority. Live VRChat and the Windows editor remain untested.

This repository contains original source only. Do not commit purchased `.unitypackage` files, vendor DLLs, or avatar assets. Licensed under MIT. [Read the package manifest](Packages/dev.nx.nxclone/package.json) · [Report an issue](https://github.com/nerdrx/nxclone/issues).
