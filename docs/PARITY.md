# Feature coverage and validation

Version 0.3.2 adds in-game rotation controls to the existing position dials. Full feature parity is not established. This matrix distinguishes implementation from runtime proof; there is no claim of complete superiority over another product.

## Implemented

| Feature | Validation |
| :--- | :--- |
| Root/bone/custom anchors, 1 m forward / 180° new-slot defaults, rotation offsets | Native SDK movement, rotation, hidden-driver and scale tests on Linux; sustained world-drop and resume tests with SDK 3.10.2 and 3.10.5. |
| In-game XYZ position dials | Native rotated-avatar neutral/endpoints, world-drop hold during dial edits and resume to the latest placement pass. One nested blend tree keeps all three coordinates together. |
| In-game pitch/yaw/roll rotation dials | Native signed quarter-turns, combined local axes, rotated/custom anchors, retained yaw-180 setup orientation, position dial coexistence and world-drop hold/resume pass. Three local carriers avoid competing quaternion animation layers; integrated menu/parameter and Write Defaults checks also pass. |
| Afterimage rig registration | Native scaled/rotated rig render shape agrees at rest; animated armature translation/rotation follows with lag and returns to exact alignment. Local scale tracks directly to avoid feedback drift. The supplied avatar maps 178 render rig paths (534 constraints per ghost). |
| Hidden spawn, master and individual visibility, afterimage toggle | Generated Animator controls and menu/parameter composition checks. |
| VRCFury Armature Link | Full SDK preprocessing of an isolated copy of the supplied avatar, with six linked mesh/bone mappings checked. |
| Flat afterimages and primary silhouette priority | Offscreen GPU checks cover flat alpha, camera proximity fade, distinct overlapping colours and priority over physically nearer ghosts. Geometry silhouettes and reserved stencil bits remain the documented limits. |
| Whole-body freeze and grabbable posing | Actual native solver ownership tests: source follow, posing, freeze and return to live. Synthetic tests do not simulate a VRChat hand grab. |
| Body recording, hips and sampled root movement | Native capture, playback, stop and recapture tests. A durable synced take phase replaces a brief request pulse; capture stays in a non-looping clip tail so native freeze caches survive playback. Tests mirror SDK driver writes explicitly. |
| Independent clone FX and transition lock | Native root/clone state independence, base-layer weight, copied clips/menus/drivers, numeric built-in aliases and source preservation tests. |
| Gesture commands | Native press/hold/release/menu state-machine tests with standard and VRCFury numeric inputs. SDK command schema checked; driver writes explicitly mirrored in editor tests. |
| Expression/gesture recording | Timed snapshot and playback layers, observer-side request handling, typed buffers and backup/restore schema checks. Actual SDK Copy execution needs VRChat. |
| Wearing a clone | Exclusive integer selector, native override-layer composition, root placement and mask transfer checks. Conditional defaults restore unowned renderer, mask and placement properties; existing FX tracks retain ownership. SDK layer-control execution needs VRChat. |
| Limb IK | Four humanoid chains, limb-length grabbable targets, native shoulder/hip anchors, ownership guards and SDK layer-control schema. Editor FinalIK stubs do not execute the solver. |
| Whole-clone and limb contact attachment | Four standard hand/foot tags, separate target drivers, preserved normal sources and freeze state, independent namespaces and synced control switches checked. Remote tracking needs a live multi-user test. |
| Menus, presets and handles | Existing controls preserved, full menus wrapped, generated menus paginated. Scene-object references must be reselected after loading presets. |
| VRCFury parameter compression | Full SDK callback pipeline once: observer after nxclone saw an over-budget avatar; the real compressor unsynced five generated parameters and the final gate accepted exactly 256 bits. Separate 257-bit rejection test passes. |

## Functional gaps from the original documented workflow

- Final controls assemble during upload; there is no equivalent finished-avatar generation or apply-modular-systems switch in the editor.
- The original configurable local limb attachment list with per-point bone, radius and offset is not implemented. Current posing and remote hand/foot contacts cover different interactions.
- Sampled recording and optional expression snapshots differ from the original recording workflow.
- There is one responsive editor layout, without separate simplified/full modes.
- Login, purchase checks and commercial CloneID management are intentionally omitted as requested.

## Remaining validation and limits

- The 0.3.1 supplied-avatar full callback rerun is blocked by a legacy VRCFury `OriginalContactsHook` exception in the disposable fixture. Earlier Armature Link/compression gates and current native rig/control checks are separate evidence.
- Live VRChat, multi-user contact tracking and Windows editor operation remain unverified.
- FinalIK and VRChat parameter-driver/layer-control behavior cannot be inferred from SDK stub serialization alone.
- Afterimage feedback delay varies with frame rate; it is not a fixed time delay.
- Recordings are sampled, not continuous motion capture. More samples and trackers increase avatar cost.
- Clone extraction strips unsupported components. Independent FX generation rejects their animation tracks with the clip, path, component and a suggested workaround; arbitrary component parity is not claimed.
- Standard humanoid bones can map by identity across differing hierarchies. Unmatched accessory bones keep their source pose and produce a warning.
- Clones do not replace the avatar descriptor or its tracking/view position. Wear selects the visible body.

## Reproduce

See [test commands and their scope](../Tests~/README.md). Tests operate on disposable projects, never the original supplied avatar project. Persistent local logs are kept in ignored `artifacts/validation/`.

Contact assumptions follow [VRChat's built-in body tags](https://creators.vrchat.com/common-components/contacts/built-in-contact-tags/) and [contact receiver settings](https://creators.vrchat.com/common-components/contacts/). LimbIK is an [allowed component](https://creators.vrchat.com/avatars/whitelisted-avatar-components/whitelisted-avatar-components/), but VRChat's implementation differs from stock FinalIK.
