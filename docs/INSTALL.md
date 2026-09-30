# Install nxclone

[Open the nxclone install page](https://nerdrx.github.io/nxclone/#install) and select **Add to VCC**. The button needs a registered `vcc://` handler. On Linux, you can add this URL in ALCOM or another VPM-compatible manager:

```text
https://nerdrx.github.io/nxclone/index.json
```

In Creator Companion, open **Settings → Packages → Add Repository**, paste the URL, and confirm. Then open your avatar project, add **nxclone**, and choose **Tools → nxclone** in Unity.

The package requires VRChat Avatars SDK 3.10.2+. Open **Tools → nxclone**, select your original scene avatar and review the check. Missing FX and expression assets are created on the generated copy. Menus wrap and paginate automatically.

After updating to 0.3.0, regenerate from your original avatar. Clones start hidden; afterimages have an independent toggle. An empty anchor uses the avatar root. World drop holds placement until switched off.

Final extraction runs after VRCFury Armature Link. Optional deferred parameter budgeting runs before its compressor and checks the final 256-bit limit. The new posing, IK, recording, wear and contact options are documented in the [usage guide](https://github.com/nerdrx/nxclone#features). [Validation limits](PARITY.md) separate native Linux checks from live VRChat and Windows tests.
