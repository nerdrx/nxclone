<p align="center">
  <img src="docs/assets/nxclone-banner.svg" width="100%" alt="nxclone — create a clone, leave a trail" />
</p>

<p align="center">
  <strong>Dance with a double. Leave a trail.</strong><br />
  Avatar clones and standalone afterimages for PC VRChat.
</p>

<p align="center">
  <code>0.3.6 preview</code> &nbsp; <code>Unity 2022.3</code> &nbsp; <code>VPM</code> &nbsp; <code>MIT</code>
</p>

<p align="center">
  <a href="https://nerdrx.github.io/nxclone/#install"><img src="docs/assets/install-nxclone.svg" width="260" height="60" alt="Install nxclone" /></a>
</p>
<p align="center">
  ALCOM / Creator Companion<br /><br />
  <a href="https://nerdrx.github.io/nxclone/">Website</a> &nbsp;·&nbsp;
  <a href="https://github.com/nerdrx/nxclone/releases/tag/v0.3.6">Releases</a> &nbsp;·&nbsp;
  <a href="docs/INSTALL.md">Install help</a> &nbsp;·&nbsp;
  <a href="docs/PARITY.md">Validation & limits</a>
</p>

---

nxclone builds a separate scene avatar with clone controls assembled during upload. Keep clothes, props and supported effects independent, choose only the placement axes you need, or add afterimages without a clone. No login or license server.

## Features

| | What you can do |
| :--- | :--- |
| **Dance partner** | Copy steps in the clone’s own facing direction and turn in place. New clones start 1 m ahead, facing you. |
| **Independent toggles** | Give each clone an **Avatar toggles** menu for clothes, props and supported effects. GoGo Loco stays on your main avatar. |
| **Placement** | Up to four clones, root/bone/custom anchors, Unity position and rotation controls, scale and mirroring. In-game dials let you select axes; Distance and Yaw are the default selections. |
| **Pose & record** | Freeze, grabbable limb posing, optional IK, sampled pose/movement playback and wearing a clone. |
| **World & contacts** | Drop a clone into world space or attach through hand/foot contacts. Copied contact receiver outputs use separate clone parameters. |
| **Afterimages** | Standalone flat silhouettes, individual colours, camera proximity fade, primary-avatar masking and a separate toggle. |
| **Setup** | Automatic missing FX/menu/parameter assets, paginated menus, presets, scene handles and optional VRCFury parameter compression. |

[Control costs and detailed behavior →](docs/USAGE.md)

## Install

1. Click **Install nxclone** above and add the VPM repository to ALCOM or Creator Companion.
2. Add **nxclone** to your avatar project.
3. Open **Tools → nxclone**, select your original avatar and review the automatic check.
4. Configure clones or choose **Afterimages only**, then **Generate scene copy**.

Clones start hidden. Their final visuals and menus assemble during SDK preprocessing after VRCFury Armature Link. **After updating, regenerate from your original avatar.** Existing uploaded avatars do not update automatically.

<details>
<summary>Manual VPM repository URL</summary>

```text
https://nerdrx.github.io/nxclone/index.json
```

On Linux, paste this URL into your VPM-compatible manager. The website’s VCC button requires a registered `vcc://` handler.

</details>

## Avatar requirements

- Unity 2022.3 and VRChat Avatars SDK **3.10.2+**.
- A scene avatar with a VRC Avatar Descriptor, a valid humanoid Animator and skinned meshes.
- Source mesh bones inside the source hierarchy. Unmatched accessory bones produce a warning.
- Enough synced parameter space for selected controls. The final upload limit remains **256 bits**, including when VRCFury compression is enabled.
- **PC avatar shaders** for afterimages. Quest/mobile afterimages are not supported by the current shader.

Missing expression assets are created on the scene copy. Your original assets and menu controls remain intact. Unsupported animation tracks report the clip, path and component.

## Preview status

| Checked | Evidence |
| :--- | :--- |
| **Linux Unity** | Native movement/drop, pose, playback, rendering and independent-toggle checks. |
| **Supplied avatar** | SDK preprocessing through VRCFury passes; copied toggle menus and baked SPS curves retained. Final synced cost: **124/256 bits** for the tested setup. |
| **Windows / live VRChat** | Still needs validation, including remote contact interaction and SDK runtime behaviors. |
| **Full feature parity** | Not established. Physics/script components and some original workflows remain limited. |

[Full coverage matrix and reproducible checks →](docs/PARITY.md)

## Source & support

Independent editor code is MIT licensed. Bundled VRLabs Contact Tracker assets retain their MIT license. Purchased packages, vendor DLLs and avatar assets are excluded.

[Report a bug](https://github.com/nerdrx/nxclone/issues) · [Releases](https://github.com/nerdrx/nxclone/releases) · [Package manifest](Packages/dev.nx.nxclone/package.json) · [License](LICENSE)
