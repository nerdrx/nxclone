# Unity regression checks

These smoke scripts live in `Tests~/Editor/`, outside the shipped UPM package.
Run them only in a disposable Unity project with nxclone and the VRChat Avatars
SDK installed. Copy the selected script(s) into that project's
`Assets/Editor/`, then invoke `-batchmode -executeMethod <Class>.Run`. Use
`-quit` for checks that stay in Edit Mode. Keep logs in
`artifacts/validation/` when running the persistent local fixtures.

## Edit Mode and asset checks

- `NxCloneSmoke.Run`: independent visibility and placement bindings, source FX
  preservation, afterimage material mirroring, and visemes. Supports
  `-nographics`.
- `NxCloneGestureSmoke.Run`: native Animator gesture press/hold/release/menu
  behavior for each hand selection, with integer/bool and optimized Float
  gesture inputs. The VRChat parameter-driver schema is inspected and its
  write is mirrored because the Unity Editor does not execute the SDK driver.
- `NxCloneExpressionRecordingSmoke.Run`: typed expression snapshots, playback
  buffers, and SDK parameter-driver schema.
- `NxCloneRecordingSmoke.Run`: generated body recording assets, constraints,
  and controller schema.
- `NxCloneSourceFxSmoke.Run`: copied source menus, parameters and FX, including
  numeric built-in aliases and preservation of the original source assets.
- `NxCloneContactAnchorSmoke.Run`: Contact Tracker assets, receiver settings,
  controller integration, and synced control parameters.
- `NxCloneLimbContactsSmoke.Run`: four limb trackers, separate target drivers,
  parameter namespaces, and receiver/controller settings.
- `NxCloneLimbIkSmoke.Run`: humanoid chains, grabbable targets, and serialized
  FinalIK/layer-control settings. The SDK stub does not execute FinalIK.
- `NxCloneWearSmoke.Run`: native Animator selector states, SDK layer-control
  schema, root placement and mask transfer. It does not enter Play Mode.
- `NxCloneOptionsSmoke.Run`: integrated `BuildVisuals(..., true)` test with two
  clones; checks menus and the 256-bit budget, expression snapshots,
  recording, contact trackers, IK targets, wear selectors, masks and
  afterimages. Also copy `NxClonePosingSmoke.cs`, which supplies its synthetic
  humanoid fixture.
- `NxCloneDeferredBudgetSmoke.Run`: exact 256/257-bit boundary and a full SDK
  preprocessing callback pipeline on the isolated avatar fixture. Requires
  VRCFury enabled; checks that the VRCFury compressor receives an over-budget
  avatar, compresses generated controls, and leaves the final avatar within
  budget.
- `NxCloneRenderSmoke.Run`: renders overlapping silhouettes into a
  depth/stencil RenderTexture, checks alpha and primary-avatar priority, and
  saves `nxclone-flat-afterimages.png` in the OS temp directory. Requires
  graphics; on Linux use a headless Gamescope session, not `-nographics`.

These checks use `-quit` after their method returns. For example:

```sh
Unity -batchmode -nographics -projectPath /path/to/test-project \
  -executeMethod NxCloneOptionsSmoke.Run -quit \
  -logFile /path/to/nxclone/artifacts/validation/options-unity.log
```

## Native Play Mode checks

Do not pass `-quit` to these checks. They enter Play Mode, wait for native
Animator/constraint frames, return to Edit Mode, then call `EditorApplication.Exit`
with the test result. `NxCloneAvatarSmoke.Run` also performs SDK preprocessing
and editor assertions before entering Play Mode. Its project-specific fixture
is `Assets/NX.unity` with the `Nixomi cloned` avatar; those avatar assets are
not distributed in this repository. The test temporarily disables VRCFury's
Play Mode scan for its generated test avatar and restores the prior setting on
exit.

- `NxCloneDanceSmoke.Run`: native nonzero-origin/yaw root motion, clone-own-left stepping, turning without orbit, vertical movement, drop/resume, and visibility origin capture/reset. Omit `-quit`.
- `NxClonePlacementSmoke.Run`: moves, rotates and scales a synthetic avatar;
  checks anchors, world drop, return to following, hidden visuals and trail lag.
- `NxCloneRecordingSmoke.RunNativePlayMode`: checks captured local pose and
  world travel through native constraint playback. The fixture mirrors the
  local command driver's durable Take writes and the menu button reset that
  the Unity Editor does not execute. It separately checks the SDK driver schema
  and verifies that a second take resets native freeze caches.
- `NxClonePosingSmoke.Run`: checks native source-follow ownership through
  posing, freeze and resume. The fixture disables ungrabbed PhysBone solvers
  for this measurement; it does not simulate a VRChat hand grab.
- `NxCloneAvatarSmoke.Run`: full generated-avatar integration, SDK callback
  preprocessing, linked mesh/bone checks and native Play Mode wear assertions.

Native Play Mode checks need Unity's Play Mode runtime; do not use
`-nographics` if the environment cannot enter Play Mode without a graphics
device. A direct `Animator.Update` or clip-sampling check does not prove native
constraint movement. SDK stubs also cannot prove VRChat parameter-driver,
layer-control or remote-contact behavior in a live avatar.

## Placement and afterimage regression checks (0.3.1)

- `NxClonePositionSmoke.Run`: native Play Mode neutral/XYZ endpoints on a rotated avatar, dropped driver stability during dial edits, and resume to the latest placement. Omit `-quit`; the test exits itself.
- `NxCloneAfterimagePlacementSmoke.Run`: native scaled/rotated source and ghost rig registration, delayed ancestor translation/rotation, direct local scale and settled baked render shape. Omit `-quit`.
- `NxCloneRenderSmoke.Run`: GPU flat silhouette, near-camera fade, distinct overlapping colours, explicit queue precedence and main-body priority with a physically closer ghost and late transparent main material. Requires graphics; run under hidden Gamescope on Linux.
- `NxCloneUiSmoke.RunOnly`: keeps a 300 px Unity utility window open for visual inspection. On Linux, capture the actual X11 window (`import -window WINDOW_ID`), since root framebuffer and Unity screen-pixel reads can return black under headless Gamescope. This utility does not exit itself.

The original avatar project is never modified by these isolated fixture checks.

- `NxCloneRotationSmoke.Run`: native neutral, signed rotation, combined local axes, retained base orientation/position, world-drop hold and resume. Run under hidden Gamescope; omit `-quit` because the fixture exits itself.

- `NxCloneOptionsSmoke.RunBoth`: checks integrated all-axis and default Distance/Yaw generation, menu pagination, exported axis parameters and 8-bit-per-axis costs.
- `NxCloneAxisSelectionSmoke.Run`: checks all eight axis masks plus native Distance/Yaw movement, retained disabled coordinates and world-drop hold/resume. Use hidden Gamescope; omit `-quit`.

- `NxCloneAfterimagesOnlySmoke.Run`: zero clone slots, empty preset persistence, upload generation, afterimage-only menus and one-bit budgeting with clone options retained.

## Independent avatar toggles (0.3.7)

- `NxCloneMenuFilterSmoke.Run`: rich-text GoGo labels, nested/shared/cyclic
  menus, exclusive parameter removal and source preservation.
- `NxCloneToggleSmoke.Run`: synthetic independent clothing and native effect
  toggles, retargeted masks, menu aliases/icons, GoGo layer placeholders,
  global tracking behavior exclusion, audio paths, contact output aliases and
  mixed numeric parameter metadata. Repeated on/off cycles and Direct Blend
  Trees with complementary animated weights check main/clone isolation.
  Uses native `Animator.Update` in Edit Mode and exits itself; omit `-quit`.
  Also copy `NxClonePosingSmoke.cs` for its humanoid fixture.
- `NxCloneAvatarToggleSmoke.Run`: disposable copy of `Assets/NX.unity`, full SDK
  preprocessing through VRCFury, cloned avatar menu aliases, GoGo exclusion
  and original source preservation. Direct child weights and main-avatar
  transition isolation are checked after the full build. This checks generated assets, not live
  VRChat interaction. Omit `-quit`; the check exits itself.
- `NxCloneSourceFxSmoke.Run`: includes a controller referencing a state machine
  owned by another asset. Merge must copy its graph before remapping and leave
  the original transition conditions unchanged.
