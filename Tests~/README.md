# Unity regression checks

Run these only in an isolated test project with nxclone and VRChat Avatars SDK installed. Copy the selected script from `Editor/` into that project's `Assets/Editor/`, then invoke Unity with `-batchmode -executeMethod <Class>.Run -quit`.

- `NxCloneSmoke.Run`: executes independent visibility toggles with an Animator; checks world-drop/freeze/scale bindings, source FX preservation, material-safe silhouette mirroring, and visemes. Use `-nographics`.
- `NxCloneRenderSmoke.Run`: renders overlapping silhouettes into a depth/stencil RenderTexture, asserts uniform alpha and main-avatar priority, and saves `nxclone-flat-afterimages.png` in the OS temp directory. Requires graphics; on Linux use `gamescope --backend headless -- Unity ...`.
- `NxCloneAvatarSmoke.Run`: project-specific integration check for the isolated `Assets/NX.unity` scene / `Nixomi cloned` fixture. Runs the full SDK callback pipeline, verifies linked mesh/bone mappings and hidden roots, then executes visibility, world drop, pose freeze, and scale through the resulting FX Animator. Avatar assets are not included in this repository.

Tests do not upload an avatar or prove VRChat runtime or Windows compatibility. The synthetic test deletes its own `Assets/nxclone-smoke*`, `Assets/nxclone-source`, and `Assets/nxclone-viseme-smoke` fixtures.
