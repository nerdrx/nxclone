<p align="center">
  <img src="docs/assets/nxclone-banner.svg" width="100%" alt="nxclone — avatar clones and translucent afterimages" />
</p>

<p align="center">
  <code>0.1.1 preview</code> &nbsp; <code>Unity 2022.3</code> &nbsp; <code>PC VRChat</code> &nbsp; <code>VPM</code>
</p>

nxclone is a source-first editor helper for VRChat avatar clones. It checks avatar requirements before generating a scene copy, an FX toggle, and optional translucent afterimages. It does not ask you to log in or contact a license server.

<h2 align="center"><a href="https://nerdrx.github.io/nxclone/#install">Install nxclone</a></h2>
<p align="center">
  ALCOM / Creator Companion<br /><br />
  <a href="https://github.com/nerdrx/nxclone/releases/tag/v0.1.1">Download preview</a> &nbsp;·&nbsp;
  <a href="docs/INSTALL.md">Installation help</a>
</p>

Add the nxclone repository, then install **nxclone** in your avatar project. The current preview is **0.1.1**.

<details><summary>Manual VPM repository URL</summary>

```text
https://nerdrx.github.io/nxclone/index.json
```

</details>

---

## Features

| Feature | What it does |
| :--- | :--- |
| **Avatar check** | Explains missing humanoid rig, clone bone paths, FX controller, expression assets, parameter space, and menu space before generation. |
| **Clone layout** | Creates up to four visual clones with an adjustable offset and an in-game toggle. |
| **Afterimages** | Adds up to four single-color translucent afterimages with adjustable constraint feedback. The lag varies with frame rate. |
| **Generated assets** | Copies the FX controller, expression menu, and parameters into a new `Assets/nxclone-generated` folder. Original assets stay unchanged. |
| **Local workflow** | Uses source code and Unity/VRChat SDK APIs. No vendor DLL, purchased asset, account login, or network step. |

## Avatar requirements

- Scene avatar root with a `VRCAvatarDescriptor`, valid `Animator`, and humanoid rig.
- Clone source with the same transform hierarchy as the root. The default source is the root itself. Different meshes are supported when bone paths match.
- Custom FX controller plus Expressions Menu and Parameters assets.
- One free expression menu slot and one free Bool expression parameter bit.
- PC shader support for translucent afterimages. Quest needs a separate mobile shader.

Open **Tools → nxclone**, choose the avatar and options, review the check, then select **Generate scene copy**. Inspect the generated avatar before uploading. Clones multiply mesh and constraint counts; [VRChat's PC performance ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/) list 350 constraints as the upper Poor threshold.

## Current scope

This preview does not implement recording, playback, limb attachment, Final IK, or automatic copying of blendshape and object-toggle animations into clones. Afterimage delay uses constraint feedback, not a fixed duration. [VRLabs explains the feedback method.](https://github.com/VRLabs/Damping-Constraints)

The editor source compiles against Unity 2022.3.22f1 and VRChat SDK 3.10.5 assemblies. The published VPM archive and SHA-256 listing were verified. Full Unity project import and VRChat upload remain unverified: the local Unity licensing client exits before project load. Linux and Windows editor behavior need an avatar-project test.

This repository contains original source only. Do not commit purchased `.unitypackage` files, vendor DLLs, or avatar assets. Licensed under MIT. [Read the package manifest](Packages/dev.nx.nxclone/package.json) · [Report an issue](https://github.com/nerdrx/nxclone/issues).
