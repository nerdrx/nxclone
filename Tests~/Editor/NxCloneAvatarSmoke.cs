using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using nxclone;
using VRC.SDK3.Dynamics.Constraint.Components;
using UnityEngine.Playables;

public static class NxCloneAvatarSmoke
{
    const string PlayRunKey = "nxclone.avatar.wear.playmode.run";
    const string PlayPhaseKey = "nxclone.avatar.wear.playmode.phase";
    const string PlayErrorKey = "nxclone.avatar.wear.playmode.error";
    const string VrcFuryPlayModePref = "com.vrcfury.playMode";
    const string VrcFuryPlayModeOriginalKey = "nxclone.avatar.wear.vrcfury.playmode.original";
    const string PlayAvatarName = "__nxclone avatar wear smoke";
    static int playFrames;
    static PlayableGraph wearGraph;
    static AnimatorControllerPlayable wearController;
    static AnimatorController playFx;

    [InitializeOnLoadMethod]
    static void InstallPlayModeRunner()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/NX.unity");
        var source = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true).Single(x => x.name == "Nixomi cloned");
        int componentsBefore = source.GetComponentsInChildren<Component>(true).Length;
        var window = ScriptableObject.CreateInstance<NxCloneWindow>();
        Set(window, "avatar", source);
        Set(window, "worldDrop", true);
        Set(window, "poseFreeze", true);
        Set(window, "runtimeScale", true);
        Set(window, "afterimages", true);
        Set(window, "afterimageCount", 2);
        Set(window, "posing", true);
        Set(window, "limbIk", true);
        Set(window, "wear", true);
        Set(window, "cloneGesture", NxCloneGesture.Fist);
        Set(window, "afterimageGesture", NxCloneGesture.Victory);
        Set(window, "recording", true);
        Set(window, "recordingSamples", 3);
        Set(window, "recordingDuration", 2f);
        typeof(NxCloneWindow).GetMethod("Generate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
        var output = Selection.activeGameObject;
        if (!output || !output.name.EndsWith("_nxclone") || output == source.gameObject) throw new Exception("Generation failed");
        if (componentsBefore != source.GetComponentsInChildren<Component>(true).Length) throw new Exception("Source changed");
        var uploadCopy = UnityEngine.Object.Instantiate(output);
        if (!VRCBuildPipelineCallbacks.OnPreprocessAvatar(uploadCopy)) throw new Exception("Upload preprocessing rejected output");
        output = uploadCopy;
        if (output.GetComponent<NxCloneSetup>()) throw new Exception("Authoring component not removed");
        var descriptor = output.GetComponent<VRCAvatarDescriptor>();
        if (!descriptor.customizeAnimationLayers) throw new Exception("Custom FX disabled");
        var physBoneType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone", false)).First(t => t != null);
        var group = output.transform.Find("nxclone");
        if (!group || !group.Find("world") || !group.Find("world").gameObject.activeSelf) throw new Exception("Active world frame missing");
        foreach (var path in new[] { "world/placement-1/clone-1", "world/trail-1/afterimage-1", "world/trail-2/afterimage-2" })
            if (!group.Find(path) || group.Find(path).gameObject.activeSelf) throw new Exception("Visual not initially hidden: " + path);
        var clone = group.Find("world/placement-1/clone-1");
        if (clone.GetComponentsInChildren(physBoneType, true).Length != 8) throw new Exception("Native limb posing/IK components missing");
        if (clone.parent.GetComponent<VRCParentConstraint>().Sources.Count != 5) throw new Exception("Trajectory samples missing");
        var mainMeshes = output.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => !x.transform.IsChildOf(group) && !UnderMask(x.transform, output.transform)).ToArray();
        foreach (var original in mainMeshes)
        {
            var path = AnimationUtility.CalculateTransformPath(original.transform, output.transform);
            var counterpart = clone.Find(path)?.GetComponent<SkinnedMeshRenderer>();
            if (!counterpart || original.sharedMesh != counterpart.sharedMesh) throw new Exception("Baked clone mesh mismatch: " + path);
            for (int i = 0; i < original.bones.Length; i++)
            {
                if (!original.bones[i]) continue;
                var bonePath = AnimationUtility.CalculateTransformPath(original.bones[i], output.transform);
                if (!counterpart.bones[i] || AnimationUtility.CalculateTransformPath(counterpart.bones[i], clone) != bonePath)
                    throw new Exception("Armature link bone mismatch: " + path);
            }
        }
        var fx = descriptor.baseAnimationLayers.First(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
        playFx = fx;
        output.SetActive(true);
        var animator = output.GetComponent<Animator>();
        animator.runtimeAnimatorController = fx;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0.1f);
        animator.SetBool("nxclone_visible", true);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (!clone.gameObject.activeSelf || group.Find("world/trail-1/afterimage-1").gameObject.activeSelf) throw new Exception("Real clone toggle failed");
        animator.SetBool("nxclone_afterimages", true);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (!group.Find("world/trail-1/afterimage-1").gameObject.activeSelf || !output.transform.Find("__nxclone main silhouette mask").gameObject.activeSelf) throw new Exception("Real afterimages or silhouette mask toggle failed");
        animator.SetBool("nxclone_drop_1", true);
        animator.SetBool("nxclone_freeze_1", true);
        animator.SetFloat("nxclone_scale", 1f);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (!clone.parent.GetComponent<VRCParentConstraint>().FreezeToWorld) throw new Exception("Real world-drop toggle failed");
        var freezeHosts = clone.GetComponentsInChildren<Transform>(true)
            .Where(x => x.name.StartsWith("nxclone freeze constraints ", StringComparison.Ordinal)).ToArray();
        if (freezeHosts.Length == 0 || freezeHosts.Any(x => x.gameObject.activeSelf))
            throw new Exception("Real pose-freeze toggle did not disable native constraint hosts");
        if (Mathf.Abs(clone.localScale.x - 2f) > 0.01f) throw new Exception("Real scale dial failed");
        animator.SetBool("nxclone_visible", false);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (clone.gameObject.activeSelf || !group.Find("world/trail-1/afterimage-1").gameObject.activeSelf) throw new Exception("Real controls not independent");


        if (!fx.parameters.Any(p => p.name == "nxclone_wear" && p.type == AnimatorControllerParameterType.Int) ||
            !descriptor.expressionParameters.parameters.Any(p => p.name == "nxclone_wear" && p.networkSynced))
            throw new Exception("Wear control is missing from built FX/expression assets");
        var wearLayer = Array.FindIndex(fx.layers, layer => layer.name == "nxclone wear 1");
        var selector = fx.layers.Single(layer => layer.name == "nxclone wear selector");
        if (wearLayer < 0 || fx.layers[wearLayer].defaultWeight != 0 ||
            selector.stateMachine.states.SelectMany(state => state.state.behaviours).Any(behaviour =>
                behaviour is VRCAnimatorLayerControl control && (control.playable != VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.FX || control.layer != wearLayer)))
            throw new Exception("Wear SDK layer selector points at the wrong FX override");
        var wearClips = fx.layers[wearLayer].stateMachine.states.SelectMany(state => state.state.motion is AnimationClip clip
            ? new[] { clip } : Array.Empty<AnimationClip>()).ToArray();
        if (wearClips.Length == 0 || !wearClips.Any(clip => AnimationUtility.GetCurveBindings(clip).Any(binding =>
            binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive" && binding.path.Contains("clone-1"))))
            throw new Exception("Wear FX layer has no clone visibility clip.");

        // Keep the upload copy alive across Play Mode. Animator layer weights are
        // inert for this avatar in Edit Mode, so only runtime checks assert wear.
        uploadCopy.name = PlayAvatarName;
        animator.SetBool("nxclone_visible", false);
        // VRCFury rescans every active avatar descriptor on Play Mode entry.
        // This controller has already passed its upload callback; the runtime
        // fixture only needs its Animator and generated hierarchy.
        source.gameObject.SetActive(false);
        UnityEngine.Object.DestroyImmediate(uploadCopy.GetComponent<VRCAvatarDescriptor>());
        SessionState.SetBool(PlayRunKey, true);
        SessionState.SetInt(PlayPhaseKey, 0);
        SessionState.EraseString(PlayErrorKey);
        SessionState.SetBool(VrcFuryPlayModeOriginalKey, EditorPrefs.GetBool(VrcFuryPlayModePref, true));
        EditorPrefs.SetBool(VrcFuryPlayModePref, false);
        EditorApplication.EnterPlaymode();
        Debug.Log("NXCLONE_AVATAR_EDITMODE_OK: linked meshes, upload preprocessing, and wear SDK schema; starting Play Mode wear checks.");
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(PlayRunKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            playFrames = 0;
            EditorApplication.update -= PlayModeTick;
            EditorApplication.update += PlayModeTick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= PlayModeTick;
            if (wearGraph.IsValid()) wearGraph.Destroy();
            wearController = default;
            playFx = null;
            var avatar = GameObject.Find(PlayAvatarName);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            bool success = SessionState.GetInt(PlayPhaseKey, 0) == 3 && string.IsNullOrEmpty(SessionState.GetString(PlayErrorKey, ""));
            string error = SessionState.GetString(PlayErrorKey, "");
            EditorPrefs.SetBool(VrcFuryPlayModePref, SessionState.GetBool(VrcFuryPlayModeOriginalKey, true));
            SessionState.SetBool(PlayRunKey, false);
            SessionState.EraseString(PlayErrorKey);
            SessionState.EraseBool(VrcFuryPlayModeOriginalKey);
            Debug.Log(success ? "NXCLONE_REAL_AVATAR_SMOKE_OK" : "NXCLONE_REAL_AVATAR_SMOKE_FAILED: " + (error.Length == 0 ? "Play Mode runner ended early" : error));
            EditorApplication.Exit(success ? 0 : 1);
        }
    }

    static void PlayModeTick()
    {
        if (!Application.isPlaying) return;
        try
        {
            var avatar = GameObject.Find(PlayAvatarName);
            if (!avatar) throw new Exception("Wear avatar disappeared after entering Play Mode.");
            var animator = avatar.GetComponent<Animator>();
            var fx = playFx;
            if (!animator || !fx) throw new Exception("Play Mode FX Animator/controller is missing.");

            var group = avatar.transform.Find("nxclone");
            var placement = group ? group.Find("world/placement-1") : null;
            var clone = group ? group.Find("world/placement-1/clone-1") : null;
            var placementConstraint = placement ? placement.GetComponent<VRCParentConstraint>() : null;
            var primaryMask = avatar.transform.Find("__nxclone main silhouette mask");
            var wornMask = avatar.transform.Find("__nxclone worn silhouette mask 1");
            var originals = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => !renderer.transform.IsChildOf(group) && !UnderMask(renderer.transform, avatar.transform)).ToArray();
            int wearLayer = Array.FindIndex(fx.layers, layer => layer.name == "nxclone wear 1");
            int selectorLayer = Array.FindIndex(fx.layers, layer => layer.name == "nxclone wear selector");
            if (!clone || !placementConstraint || !primaryMask || !wornMask || originals.Length == 0 || wearLayer < 0 || selectorLayer < 0)
                throw new Exception("Play Mode wear fixture is incomplete.");

            int phase = SessionState.GetInt(PlayPhaseKey, 0);
            if (phase == 0)
            {
                var priorGraph = animator.playableGraph;
                string priorOutputs = priorGraph.IsValid()
                    ? string.Join(",", Enumerable.Range(0, priorGraph.GetOutputCount()).Select(index => {
                        var output = priorGraph.GetOutput(index);
                        return output.GetPlayableOutputType().Name + ":" + output.GetSourcePlayable().GetPlayableType().Name;
                    })) : "none";
                Debug.Log("WEAR_PLAYMODE_PRIOR_GRAPH outputs=" + priorOutputs + ", layers=" + fx.layers.Length + ", wear=" + wearLayer);
                if (priorGraph.IsValid()) priorGraph.Destroy();
                animator.runtimeAnimatorController = null;
                wearGraph = PlayableGraph.Create("NxClone full FX wear smoke");
                wearGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                wearController = AnimatorControllerPlayable.Create(wearGraph, fx);
                var output = AnimationPlayableOutput.Create(wearGraph, "NxClone FX", animator);
                output.SetSourcePlayable(wearController);
                wearGraph.Play();
                wearController.SetBool(Animator.StringToHash("nxclone_afterimages"), true);
                wearController.SetInteger(Animator.StringToHash("nxclone_wear"), 1);
                wearGraph.Evaluate(0.02f);
                SessionState.SetInt(PlayPhaseKey, 1);
                return;
            }
            if (!wearGraph.IsValid() || !wearController.IsValid()) throw new Exception("Test-owned FX PlayableGraph is missing.");
            wearGraph.Evaluate(0.02f);
            if (playFrames++ < 4) return;
            playFrames = 0;
            if (phase == 2)
            {
                bool selectorOff = wearController.GetCurrentAnimatorStateInfo(selectorLayer).IsName("Off");
                if (!selectorOff || wearController.GetInteger(Animator.StringToHash("nxclone_wear")) != 0 || clone.gameObject.activeSelf ||
                    originals.Any(renderer => !renderer.enabled) || !primaryMask.gameObject.activeSelf || wornMask.gameObject.activeSelf)
                    throw new Exception("Wear-off failed to restore the original avatar FX: selector=" +
                        selectorOff + ", parameter=" + wearController.GetInteger(Animator.StringToHash("nxclone_wear")) + ", clone=" + clone.gameObject.activeSelf +
                        ", hidden originals=" + originals.Count(renderer => !renderer.enabled) + ", primary=" +
                        primaryMask.gameObject.activeSelf + ", worn=" + wornMask.gameObject.activeSelf);
                var rootSource = placementConstraint.Sources.FirstOrDefault(source => source.SourceTransform == avatar.transform);
                if (!rootSource.SourceTransform || rootSource.Weight > 0.001f ||
                    !placementConstraint.Sources.Any(source => source.SourceTransform && source.SourceTransform != avatar.transform && source.Weight > 0.9f))
                    throw new Exception("Wear-off did not restore the avatar-root placement anchor: root source=" + rootSource.Weight +
                        ", sources=" + string.Join(",", placementConstraint.Sources.Select(source =>
                            (source.SourceTransform ? source.SourceTransform.name : "null") + "=" + source.Weight)));
                SessionState.SetInt(PlayPhaseKey, 3);
                EditorApplication.ExitPlaymode();
                return;
            }
            if (!wearController.GetCurrentAnimatorStateInfo(selectorLayer).IsName("Wear 1"))
                throw new Exception("Native Play Mode selector did not enter Wear 1.");
            // VRChat applies this target through VRCAnimatorLayerControl. Drive the
            // same weight on Unity's native controller playable to test composition.
            wearController.SetLayerWeight(wearLayer, 1f);
            wearGraph.Evaluate(0.02f);
            if (!clone.gameObject.activeSelf || originals.Any(renderer => renderer.enabled) ||
                primaryMask.gameObject.activeSelf || !wornMask.gameObject.activeSelf)
                throw new Exception("Runtime wear clip composition failed: clone=" + clone.gameObject.activeSelf +
                    ", visible originals=" + originals.Count(renderer => renderer.enabled) +
                    ", primary=" + primaryMask.gameObject.activeSelf + ", worn=" + wornMask.gameObject.activeSelf);
            var activeRootSource = placementConstraint.Sources.FirstOrDefault(source => source.SourceTransform == avatar.transform);
            if (!activeRootSource.SourceTransform || activeRootSource.Weight < 0.9f ||
                placementConstraint.Sources.Any(source => source.SourceTransform && source.SourceTransform != avatar.transform && source.Weight > 0.001f))
                throw new Exception("Wear-on did not transfer placement to the avatar root: sources=" + string.Join(",", placementConstraint.Sources.Select(source =>
                    (source.SourceTransform ? source.SourceTransform.name : "null") + "=" + source.Weight)));
            wearController.SetInteger(Animator.StringToHash("nxclone_wear"), 0);
            wearController.SetLayerWeight(wearLayer, 0f);
            SessionState.SetInt(PlayPhaseKey, 2);
        }
        catch (Exception exception)
        {
            SessionState.SetString(PlayErrorKey, exception.ToString());
            Debug.LogError("NXCLONE_REAL_AVATAR_PLAYMODE_FAILED: " + exception);
            EditorApplication.ExitPlaymode();
        }
    }
    static bool UnderMask(Transform target, Transform root)
    {
        for (var current = target; current && current != root; current = current.parent)
            if (current.name.StartsWith("__nxclone", StringComparison.Ordinal)) return true;
        return false;
    }
    static void Set(NxCloneWindow window, string name, object value) => typeof(NxCloneWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
}
