using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;
using nxclone;

public static class NxCloneWearSmoke
{
    public static void Run()
    {
        const string folder = "Assets/nxclone-wear-smoke";
        AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-wear-smoke");
        var fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/fx.controller");
        fx.AddParameter("nxclone_visible", AnimatorControllerParameterType.Bool);
        fx.AddParameter("nxclone_afterimages", AnimatorControllerParameterType.Bool);
        fx.AddParameter("nxclone_enabled_1", AnimatorControllerParameterType.Bool);
        fx.AddParameter("nxclone_enabled_2", AnimatorControllerParameterType.Bool);
        fx.AddParameter("source_fx_on", AnimatorControllerParameterType.Bool);
        var root = new GameObject("wear-smoke-avatar");
        var animator = root.AddComponent<Animator>();
        var original = GameObject.CreatePrimitive(PrimitiveType.Cube);
        original.name = "original-renderer"; original.transform.SetParent(root.transform, false);
        var untrackedOriginal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        untrackedOriginal.name = "untracked-original-renderer"; untrackedOriginal.transform.SetParent(root.transform, false);
        var sourceAnchor = new GameObject("normal-anchor").transform;
        sourceAnchor.SetParent(root.transform, false);
        var driver = new GameObject("placement-1").transform; driver.SetParent(root.transform, false);
        var parent = driver.gameObject.AddComponent<VRCParentConstraint>();
        parent.Sources.Add(new VRCConstraintSource(sourceAnchor, 1f)); parent.ActivateConstraint(); parent.ApplyConfigurationChanges();
        var scale = driver.gameObject.AddComponent<VRCScaleConstraint>();
        scale.Sources.Add(new VRCConstraintSource(sourceAnchor, 1f)); scale.ActivateConstraint(); scale.ApplyConfigurationChanges();
        var visual = new GameObject("clone-1").transform; visual.SetParent(driver, false); visual.gameObject.SetActive(false);
        var sourceAnchor2 = new GameObject("normal-anchor-2").transform; sourceAnchor2.SetParent(root.transform, false);
        var driver2 = new GameObject("placement-2").transform; driver2.SetParent(root.transform, false);
        var parent2 = driver2.gameObject.AddComponent<VRCParentConstraint>();
        parent2.Sources.Add(new VRCConstraintSource(sourceAnchor2, 1f)); parent2.ActivateConstraint(); parent2.ApplyConfigurationChanges();
        var scale2 = driver2.gameObject.AddComponent<VRCScaleConstraint>();
        scale2.Sources.Add(new VRCConstraintSource(sourceAnchor2, 1f)); scale2.ActivateConstraint(); scale2.ApplyConfigurationChanges();
        float parentWeightBefore = parent.Sources[0].Weight, parent2WeightBefore = parent2.Sources[0].Weight;
        float scaleWeightBefore = scale.Sources[0].Weight, scale2WeightBefore = scale2.Sources[0].Weight;
        bool parentFreezeBefore = parent.FreezeToWorld, parent2FreezeBefore = parent2.FreezeToWorld;
        bool scaleFreezeBefore = scale.FreezeToWorld, scale2FreezeBefore = scale2.FreezeToWorld;
        var visual2 = new GameObject("clone-2").transform; visual2.SetParent(driver2, false); visual2.gameObject.SetActive(false);
        var primaryMask = new GameObject("__nxclone main silhouette mask").transform; primaryMask.SetParent(root.transform, false);
        var wornMask = new GameObject("__nxclone worn silhouette mask 1").transform; wornMask.SetParent(root.transform, false);
        wornMask.gameObject.SetActive(false);
        var wornMask2 = new GameObject("__nxclone worn silhouette mask 2").transform; wornMask2.SetParent(root.transform, false);
        wornMask2.gameObject.SetActive(false);
        var baseline = new AnimationClip { name = "normal placement baseline" };
        AnimationUtility.SetEditorCurve(baseline, EditorCurveBinding.FloatCurve("placement-1/clone-1", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
        AnimationUtility.SetEditorCurve(baseline, EditorCurveBinding.FloatCurve("placement-2/clone-2", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
        var baseMachine = new AnimatorStateMachine { name = "normal placement" };
        AssetDatabase.AddObjectToAsset(baseMachine, fx);
        var baseState = baseMachine.AddState("Follow"); baseState.motion = baseline; baseMachine.defaultState = baseState;
        fx.AddLayer(new AnimatorControllerLayer { name = "normal placement", defaultWeight = 1, stateMachine = baseMachine });
        var sourceFx = new AnimationClip { name = "source fx enables renderer" };
        AnimationUtility.SetEditorCurve(sourceFx, EditorCurveBinding.FloatCurve("original-renderer", typeof(MeshRenderer), "m_Enabled"), AnimationCurve.Constant(0, 1, 1));
        var sourceFxOff = new AnimationClip { name = "source fx disables renderer" };
        AnimationUtility.SetEditorCurve(sourceFxOff, EditorCurveBinding.FloatCurve("original-renderer", typeof(MeshRenderer), "m_Enabled"), AnimationCurve.Constant(0, 1, 0));
        var sourceFxMachine = new AnimatorStateMachine { name = "original renderer FX" };
        AssetDatabase.AddObjectToAsset(sourceFxMachine, fx);
        var sourceOn = sourceFxMachine.AddState("Source FX on"); sourceOn.motion = sourceFx; sourceFxMachine.defaultState = sourceOn;
        var sourceOff = sourceFxMachine.AddState("Source FX off"); sourceOff.motion = sourceFxOff;
        var toSourceOff = sourceOn.AddTransition(sourceOff); toSourceOff.hasExitTime = false; toSourceOff.duration = 0;
        toSourceOff.AddCondition(AnimatorConditionMode.IfNot, 0, "source_fx_on");
        var toSourceOn = sourceOff.AddTransition(sourceOn); toSourceOn.hasExitTime = false; toSourceOn.duration = 0;
        toSourceOn.AddCondition(AnimatorConditionMode.If, 0, "source_fx_on");
        fx.AddLayer(new AnimatorControllerLayer { name = "original renderer FX", defaultWeight = 1, stateMachine = sourceFxMachine });
        var maskClip = new AnimationClip { name = "primary afterimage mask" };
        AnimationUtility.SetEditorCurve(maskClip, EditorCurveBinding.FloatCurve("__nxclone main silhouette mask", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
        AnimationUtility.SetEditorCurve(maskClip, EditorCurveBinding.FloatCurve("__nxclone worn silhouette mask 1", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 0));
        AnimationUtility.SetEditorCurve(maskClip, EditorCurveBinding.FloatCurve("__nxclone worn silhouette mask 2", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 0));
        var maskOffClip = new AnimationClip { name = "primary mask off" };
        AnimationUtility.SetEditorCurve(maskOffClip, EditorCurveBinding.FloatCurve("__nxclone main silhouette mask", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 0));
        var maskMachine = new AnimatorStateMachine { name = "afterimages" };
        AssetDatabase.AddObjectToAsset(maskMachine, fx);
        var maskOn = maskMachine.AddState("Afterimages on"); maskOn.motion = maskClip; maskMachine.defaultState = maskOn;
        var maskOff = maskMachine.AddState("Afterimages off"); maskOff.motion = maskOffClip;
        var toMaskOff = maskOn.AddTransition(maskOff); toMaskOff.hasExitTime = false; toMaskOff.duration = 0;
        toMaskOff.AddCondition(AnimatorConditionMode.IfNot, 0, "nxclone_afterimages");
        var toMaskOn = maskOff.AddTransition(maskOn); toMaskOn.hasExitTime = false; toMaskOn.duration = 0;
        toMaskOn.AddCondition(AnimatorConditionMode.If, 0, "nxclone_afterimages");
        fx.AddLayer(new AnimatorControllerLayer { name = "afterimage mask", defaultWeight = 1, stateMachine = maskMachine });
        var sourceBindingsBefore = AnimationUtility.GetCurveBindings(baseline).ToArray();
        var result = NxCloneWear.Configure(fx, root.transform, new[] { original.GetComponent<Renderer>(), untrackedOriginal.GetComponent<Renderer>() }, new[] {
            new NxCloneWear.Slot { Name = "test 1", Driver = driver, Visual = visual, VisibleParameter = "nxclone_enabled_1" },
            new NxCloneWear.Slot { Name = "test 2", Driver = driver2, Visual = visual2, VisibleParameter = "nxclone_enabled_2" }
        }, "nxclone_wear_selected", "nxclone_visible", "nxclone_afterimages", primaryMask, new[] { wornMask, wornMask2 }, folder);
        if (result.Menu.controls.Count != 2 || result.Menu.controls.Any(control => control.type != VRCExpressionsMenu.Control.ControlType.Toggle || control.parameter.name != result.ParameterName) ||
            !result.Menu.controls.Any(control => control.value == 1) || !result.Menu.controls.Any(control => control.value == 2))
            throw new Exception("Wear menu does not expose exclusive single-index choices.");
        var wearBindings = result.Clips.SelectMany(AnimationUtility.GetCurveBindings).ToArray();
        if (!wearBindings.Any(binding => binding.path == "original-renderer" && binding.type == typeof(MeshRenderer) && binding.propertyName == "m_Enabled"))
            throw new Exception("Wear mode did not use the native animated renderer enabled binding; clips=" + string.Join("|", result.Clips.Select(clip =>
                clip.name + "=" + string.Join(",", AnimationUtility.GetCurveBindings(clip).Select(binding => binding.path + ":" + binding.type.Name + ":" + binding.propertyName)))));
        if (result.Clips.Any(clip => clip.name.StartsWith("wear-off-")))
            throw new Exception("Wear controller must leave original renderer tracks to the zero-weight override layer, not a baseline clip.");
        if (result.Clips.Where(clip => clip.name.StartsWith("wear-off-"))
            .SelectMany(AnimationUtility.GetCurveBindings).Any(binding => binding.type == typeof(MeshRenderer) && binding.propertyName == "m_Enabled"))
            throw new Exception("Wear-off must let original FX renderer enabled/GameObject tracks resume.");
        var baselineBindings = result.BaselineClip ? AnimationUtility.GetCurveBindings(result.BaselineClip) : Array.Empty<EditorCurveBinding>();
        if (string.IsNullOrEmpty(result.BaselineLayerName) || baselineBindings.Length != 15 ||
            !baselineBindings.Any(binding => binding.path == "untracked-original-renderer" && binding.propertyName == "m_Enabled") ||
            baselineBindings.Any(binding => binding.path == "original-renderer" && binding.propertyName == "m_Enabled") ||
            !baselineBindings.Any(binding => binding.path == "__nxclone worn silhouette mask 1" && binding.propertyName == "m_IsActive") ||
            !baselineBindings.Any(binding => binding.path == "__nxclone worn silhouette mask 2" && binding.propertyName == "m_IsActive") ||
            !baselineBindings.Any(binding => binding.path == "placement-1" && binding.propertyName == "Sources.source1.Weight") ||
            !baselineBindings.Any(binding => binding.path == "placement-2" && binding.propertyName == "Sources.source1.Weight"))
            throw new Exception("Wear baseline must restore untracked renderers, placement source weights, and clear worn masks without overriding source FX.");
        var baselineLayer = fx.layers.Single(layer => layer.name == result.BaselineLayerName);
        if (baselineLayer.defaultWeight != 1f || baselineLayer.stateMachine.states.Single().state.writeDefaultValues)
            throw new Exception("Renderer restoration baseline must stay active with Write Defaults off.");
        if (parent.Sources.Count != 2 || scale.Sources.Count != 2 || parent.Sources[1].SourceTransform != root.transform || scale.Sources[1].SourceTransform != root.transform ||
            parent2.Sources.Count != 2 || scale2.Sources.Count != 2 || parent2.Sources[1].SourceTransform != root.transform || scale2.Sources[1].SourceTransform != root.transform)
            throw new Exception("Avatar-root native wear sources were not appended.");
        if (!sourceBindingsBefore.SequenceEqual(AnimationUtility.GetCurveBindings(baseline)))
            throw new Exception("Normal placement animation was mutated.");
        AssetDatabase.SaveAssets();
        animator.runtimeAnimatorController = fx; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var wearLayers = result.VisualLayerNames.Select(name => Array.FindIndex(fx.layers, layer => layer.name == name)).ToArray();
        int selectorLayer = Array.FindIndex(fx.layers, layer => layer.name == result.SelectorLayerName);
        var selectorMachine = fx.layers[selectorLayer].stateMachine;
        var offDrivers = selectorMachine.states.Single(entry => entry.state.name == "Off").state.behaviours.OfType<VRCAnimatorLayerControl>().ToArray();
        var on1Drivers = selectorMachine.states.Single(entry => entry.state.name == "Wear 1").state.behaviours.OfType<VRCAnimatorLayerControl>().ToArray();
        var on2Drivers = selectorMachine.states.Single(entry => entry.state.name == "Wear 2").state.behaviours.OfType<VRCAnimatorLayerControl>().ToArray();
        var wear1State = selectorMachine.states.Single(entry => entry.state.name == "Wear 1").state;
        var wear2State = selectorMachine.states.Single(entry => entry.state.name == "Wear 2").state;
        if (offDrivers.Length != 2 || offDrivers.Any(control => control.playable != VRC_AnimatorLayerControl.BlendableLayer.FX || control.goalWeight != 0f) ||
            on1Drivers.Length != 2 || on1Drivers.Single(control => control.layer == wearLayers[0]).goalWeight != 1f || on1Drivers.Single(control => control.layer == wearLayers[1]).goalWeight != 0f ||
            on2Drivers.Length != 2 || on2Drivers.Single(control => control.layer == wearLayers[0]).goalWeight != 0f || on2Drivers.Single(control => control.layer == wearLayers[1]).goalWeight != 1f ||
            !wear1State.transitions.Any(transition => transition.destinationState == wear2State) ||
            !wear2State.transitions.Any(transition => transition.destinationState == wear1State))
            throw new Exception("Wear selector does not use supported native FX AnimatorLayerControl states.");
        animator.runtimeAnimatorController = null;
        var graph = PlayableGraph.Create("NxClone wear schema smoke");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimatorControllerPlayable.Create(graph, fx);
        var output = AnimationPlayableOutput.Create(graph, "Wear smoke", animator);
        output.SetSourcePlayable(playable);
        graph.Play();
        playable.SetBool(Animator.StringToHash("nxclone_visible"), true);
        playable.SetBool(Animator.StringToHash("nxclone_enabled_1"), true);
        playable.SetBool(Animator.StringToHash("nxclone_enabled_2"), true);
        playable.SetBool(Animator.StringToHash("nxclone_afterimages"), true);
        playable.SetBool(Animator.StringToHash("source_fx_on"), true);
        playable.SetInteger(Animator.StringToHash(result.ParameterName), 1);
        playable.SetLayerWeight(wearLayers[0], 1f);
        graph.Evaluate(0.1f); graph.Evaluate(0.1f);
        if (original.GetComponent<Renderer>().enabled || !visual.gameObject.activeSelf ||
            !playable.GetCurrentAnimatorStateInfo(wearLayers[0]).IsName("Wear mask on"))
            throw new Exception("Native wear state mismatch: original=" + original.GetComponent<Renderer>().enabled +
                ", untracked=" + untrackedOriginal.GetComponent<Renderer>().enabled + ", clone=" + visual.gameObject.activeSelf +
                ", state=" + playable.GetCurrentAnimatorStateInfo(wearLayers[0]).IsName("Wear mask on") +
                ", layerWeight=" + playable.GetLayerWeight(wearLayers[0]));
        if (primaryMask.gameObject.activeSelf || !wornMask.gameObject.activeSelf || !result.AfterimageMaskIntegrated)
            throw new Exception("Wear state did not transfer the active afterimage silhouette mask to the selected clone.");
        playable.SetBool(Animator.StringToHash("nxclone_afterimages"), false); graph.Evaluate(0.1f); graph.Evaluate(0.1f);
        if (wornMask.gameObject.activeSelf) throw new Exception("Worn clone mask remained active with afterimages off.");
        playable.SetBool(Animator.StringToHash("nxclone_afterimages"), true); graph.Evaluate(0.1f); graph.Evaluate(0.1f);
        if (!wornMask.gameObject.activeSelf) throw new Exception("Worn clone mask did not follow the afterimages toggle.");
        playable.SetInteger(Animator.StringToHash(result.ParameterName), 2); playable.SetLayerWeight(wearLayers[0], 0f); playable.SetLayerWeight(wearLayers[1], 1f);
        graph.Evaluate(0.1f); graph.Evaluate(0.1f);
        if (!playable.GetCurrentAnimatorStateInfo(wearLayers[1]).IsName("Wear mask on") || !visual2.gameObject.activeSelf ||
            !wornMask2.gameObject.activeSelf || wornMask.gameObject.activeSelf)
            throw new Exception("Selecting Wear 2 did not exclusively transfer clone and afterimage-mask ownership.");
        if (Mathf.Abs(parent.Sources[0].Weight - parentWeightBefore) > 0.001f || Mathf.Abs(parent.Sources[1].Weight) > 0.001f || parent.FreezeToWorld != parentFreezeBefore)
            throw new Exception("Changing selected clone did not restore previous placement: source0=" + parent.Sources[0].Weight +
                ", source1=" + parent.Sources[1].Weight + ", frozen=" + parent.FreezeToWorld +
                ", weights=" + playable.GetLayerWeight(wearLayers[0]) + "/" + playable.GetLayerWeight(wearLayers[1]));
        if (Mathf.Abs(parent2.Sources[0].Weight) > 0.001f || Mathf.Abs(parent2.Sources[1].Weight - 1f) > 0.001f || parent2.FreezeToWorld)
            throw new Exception("Native wear state did not temporarily move placement to avatar root.");
        playable.SetBool(Animator.StringToHash("source_fx_on"), false); graph.Evaluate(0.1f); graph.Evaluate(0.1f);
        playable.SetInteger(Animator.StringToHash(result.ParameterName), 0); graph.Evaluate(0.1f); graph.Evaluate(0.1f);
        playable.SetLayerWeight(wearLayers[0], 0f); playable.SetLayerWeight(wearLayers[1], 0f);
        graph.Evaluate(0.1f); graph.Evaluate(0.1f);
        if (original.GetComponent<Renderer>().enabled || !visual.gameObject.activeSelf)
            throw new Exception("Wear-off did not restore original shadows and normal clone visibility controls.");
        if (!primaryMask.gameObject.activeSelf || wornMask.gameObject.activeSelf || wornMask2.gameObject.activeSelf)
            throw new Exception("Wear-off mask mismatch: primary=" + primaryMask.gameObject.activeSelf +
                ", worn1=" + wornMask.gameObject.activeSelf + ", worn2=" + wornMask2.gameObject.activeSelf);
        if (!untrackedOriginal.GetComponent<Renderer>().enabled)
            throw new Exception("Wear-off baseline did not restore an original renderer without an FX enabled binding.");
        if (Mathf.Abs(parent.Sources[0].Weight - parentWeightBefore) > 0.001f || Mathf.Abs(parent.Sources[1].Weight) > 0.001f || parent.FreezeToWorld != parentFreezeBefore ||
            Mathf.Abs(parent2.Sources[0].Weight - parent2WeightBefore) > 0.001f || Mathf.Abs(parent2.Sources[1].Weight) > 0.001f || parent2.FreezeToWorld != parent2FreezeBefore ||
            Mathf.Abs(scale.Sources[0].Weight - scaleWeightBefore) > 0.001f || Mathf.Abs(scale.Sources[1].Weight) > 0.001f || scale.FreezeToWorld != scaleFreezeBefore ||
            Mathf.Abs(scale2.Sources[0].Weight - scale2WeightBefore) > 0.001f || Mathf.Abs(scale2.Sources[1].Weight) > 0.001f || scale2.FreezeToWorld != scale2FreezeBefore)
            throw new Exception("Wear-off did not return native placement to the underlying normal layer.");
        playable.SetBool(Animator.StringToHash("source_fx_on"), true); graph.Evaluate(0.1f); graph.Evaluate(0.1f);
        if (!original.GetComponent<Renderer>().enabled) throw new Exception("Original renderer FX did not resume after wear layer weight returned to zero.");
        graph.Destroy();
        Debug.Log("NXCLONE_NATIVE_WEAR_SMOKE_OK: exclusive Int selection, SDK layer-weight selector, original FX restoration, forced clone visibility, root placement, afterimage mask transfer.");
    }
}
