# Controls and behavior

[Install nxclone](INSTALL.md) · [Feature coverage and validation](PARITY.md)

## Control costs and limits

| Option | Synced bits |
| :--- | ---: |
| Master visibility | 1 |
| Individual enable / world drop / freeze / posing / IK | 1 per selected control per clone |
| Afterimage visibility | 1 |
| Scale dial | 8 |
| Position dials | 8 per enabled axis per clone |
| Rotation dials | 8 per enabled axis per clone |
| Body record + replay + speed | 10 per clone |
| Wear selector | 8 total |
| Contact attachment | 1 per tracker; seven tracking inputs remain local |
| Gesture commands / expression snapshot buffers | 0 additional |

Copied source-menu parameters and FX transition locks add their own cost. **Defer parameter limit to VRCFury** requires VRCFury; upload is still rejected if the final cost exceeds 256 bits. Recording supports up to 15 samples, reduced to reserve sources for world contact attachment and wear. More samples, meshes and trackers increase avatar cost.

## Behavior and compatibility

- **Independent clone toggles / expressions** gives each clone its own **Avatar toggles** submenu for clothing, props and supported effects. New setups enable it by default. **Exclude GoGo Loco from clone menus** removes its menu branch and exclusive network parameters, and disables its dedicated FX layers on the clone. The main avatar’s menu remains intact. Existing saved setups may need the independent option enabled manually.
- **Afterimages only** removes all clone slots and enables the trails directly. You can also remove the last clone manually. The standalone **Afterimages** menu toggle costs one synced bit; clone-only controls are hidden and generate nothing. Empty layouts persist in presets and saved setup.
- **Root movement: DancePartner** copies steps in the clone’s facing direction and turns it around its own root. With 180° yaw, your left becomes its left (your right). Hiding and showing captures a fresh starting point. Choose **Attached** for the previous root-relative following behavior; bone and custom anchors remain attached. Dance mode adds three constraints per clone and no expression parameters.
- New clones default to 1 m forward from the avatar root, facing back toward it (180° yaw). Use **At anchor** for zero offset. Existing saved layouts retain their placement. Bone anchors apply offsets in the selected anchor's frame. Position dials adjust ±2 m per axis around the configured placement; 0.5 is neutral. Rotation dials add ±180° of pitch, yaw and roll to the configured orientation, in that order around successive local axes. Rotation does not change the configured placement position. Select the axes you need in each control group; the default selections are **Distance (Z)** and **Yaw (Y)**. Only enabled axes produce menus and synced parameters (16 bits per clone with those two defaults).
- Afterimages follow the main avatar with zero placement offset. Each trail follows the full render rig, including armature ancestors. Newer trails blend over older ones; the primary silhouette masks every trail. Nearby fragments fade between 0.25 and 0.6 m from the camera. Afterimage lag uses constraint feedback, so it varies with frame rate. It is not a fixed millisecond delay.
- Afterimages are geometry silhouettes: textures, cutout holes and material displacement are not reproduced. Stencil bits 0 through 4 are reserved; opaque scene depth still occludes them.
- Independent FX requires transferable animation bindings. If a source clip animates a component removed from clone visuals, generation names the clip and track and explains how to disable or remove it.
- Presets store settings; scene source and custom-anchor references must be reselected.
- Remote contacts need avatar interaction permissions. Built-in body tags such as HandL and FootR are normally generated on humanoid avatars; custom tags require matching senders. [VRChat contact tags](https://creators.vrchat.com/common-components/contacts/built-in-contact-tags/).

