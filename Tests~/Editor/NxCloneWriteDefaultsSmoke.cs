using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

public static class NxCloneWriteDefaultsSmoke
{
    const string Folder = "Assets/nxclone-write-defaults-smoke";

    public static void Run()
    {
        try { RunTest(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void RunTest()
    {
        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-write-defaults-smoke");
        GameObject root = null;
        Avatar temporaryAvatar = null;
        NxCloneWindow window = null;
        try
        {
            var factory = typeof(NxClonePosingSmoke).GetMethod("CreateSyntheticHumanoid", BindingFlags.NonPublic | BindingFlags.Static);
            if (factory == null) throw new Exception("Synthetic humanoid factory missing.");
            var animator = (Animator)factory.Invoke(null, null);
            root = animator.gameObject;
            temporaryAvatar = animator.avatar;
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();

            var feature = new GameObject("Clothing");
            feature.transform.SetParent(root.transform, false);
            var light = feature.AddComponent<Light>();
            light.intensity = 0.25f;

            var fx = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/source.controller");
            fx.AddParameter("feature_toggle", AnimatorControllerParameterType.Bool);
            var machine = fx.layers[0].stateMachine;
            var off = machine.AddState("Off");
            off.writeDefaultValues = true;
            var on = machine.AddState("On");
            on.writeDefaultValues = true;
            var onClip = new AnimationClip { name = "feature on" };
            AnimationUtility.SetEditorCurve(onClip,
                EditorCurveBinding.FloatCurve("Clothing", typeof(Light), "m_Intensity"),
                AnimationCurve.Constant(0f, 1f, 4f));
            AssetDatabase.CreateAsset(onClip, Folder + "/feature-on.anim");
            on.motion = onClip;
            machine.defaultState = off;
            AddBoolTransition(off, on, "feature_toggle", true);
            AddBoolTransition(on, off, "feature_toggle", false);
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer {
                type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = fx,
                isDefault = false, isEnabled = true
            } };

            var expressions = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            expressions.parameters = new[] { new VRCExpressionParameters.Parameter {
                name = "feature_toggle", valueType = VRCExpressionParameters.ValueType.Bool,
                defaultValue = 0f, saved = false, networkSynced = true
            } };
            AssetDatabase.CreateAsset(expressions, Folder + "/source-parameters.asset");
            descriptor.customExpressions = true;
            descriptor.expressionParameters = expressions;
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls.Add(new VRCExpressionsMenu.Control {
                name = "Clothing", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "feature_toggle" }, value = 1f
            });
            AssetDatabase.CreateAsset(menu, Folder + "/source-menu.asset");
            descriptor.expressionsMenu = menu;

            window = ScriptableObject.CreateInstance<NxCloneWindow>();
            Set(window, "avatar", descriptor);
            Set(window, "slots", new List<NxCloneSlot> { new NxCloneSlot() });
            Set(window, "copyFxAnimations", true);
            Set(window, "independentCloneFx", true);
            Set(window, "copyVisemes", false);
            Set(window, "runtimePosition", true);
            Set(window, "runtimeRotation", true);
            Set(window, "positionAxes", NxCloneAxes.Z);
            Set(window, "rotationAxes", NxCloneAxes.Y);
            var build = typeof(NxCloneWindow).GetMethod("BuildVisuals", BindingFlags.Instance | BindingFlags.NonPublic);
            if (build == null) throw new Exception("BuildVisuals method missing.");
            build.Invoke(window, new object[] { descriptor, Folder, true });
            AssetDatabase.SaveAssets();

            var generatedFx = (AnimatorController)descriptor.baseAnimationLayers
                .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            var states = generatedFx.layers.SelectMany(layer => States(layer.stateMachine)).ToArray();
            Assert(states.Length > 0 && states.All(state => state.writeDefaultValues),
                "Auto must keep every generated and source FX state Write Defaults On when the input FX is all On; Off layers were: " +
                string.Join(", ", generatedFx.layers.SelectMany(layer => States(layer.stateMachine))
                    .Where(state => !state.writeDefaultValues).Select(state => state.name)));
            Assert(generatedFx.layers.Any(layer => layer.name.StartsWith("nxclone expression input copy ", StringComparison.Ordinal)) &&
                   generatedFx.layers.Any(layer => layer.name.EndsWith(" position", StringComparison.Ordinal)) &&
                   generatedFx.layers.Any(layer => layer.name.Contains(" rotation ", StringComparison.Ordinal)),
                "Fixture did not produce expression-copy, position, and rotation layers.");

            string featureAlias = generatedFx.parameters.Single(parameter =>
                parameter.name.StartsWith("nxclone_clone1_", StringComparison.Ordinal) &&
                parameter.name.EndsWith("_feature_toggle", StringComparison.Ordinal)).name;
            string master = generatedFx.parameters.Single(parameter => parameter.name.StartsWith("nxclone_visible", StringComparison.Ordinal)).name;
            animator.runtimeAnimatorController = generatedFx;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            animator.SetBool(master, true);
            Step(animator);
            var clone = root.transform.Find("nxclone/world/placement-1/clone-1");
            Assert(clone && clone.gameObject.activeInHierarchy, "Master visibility did not activate the clone fixture.");
            var cloneLight = clone.Find("Clothing").GetComponent<Light>();
            float rootDefault = light.intensity;
            float cloneDefault = cloneLight.intensity;

            animator.SetBool("feature_toggle", true);
            animator.SetBool(featureAlias, true);
            Step(animator);
            Assert(Mathf.Abs(light.intensity - 4f) < 0.01f && Mathf.Abs(cloneLight.intensity - 4f) < 0.01f,
                "Root and clone feature toggles did not activate their Light curves.");

            animator.SetBool("feature_toggle", false);
            Step(animator);
            Assert(Mathf.Abs(light.intensity - rootDefault) < 0.01f && Mathf.Abs(cloneLight.intensity - 4f) < 0.01f,
                "Root toggle Off did not restore its implicit Write Defaults value independently.");
            animator.SetBool(featureAlias, false);
            Step(animator);
            Assert(Mathf.Abs(cloneLight.intensity - cloneDefault) < 0.01f,
                "Clone toggle Off did not restore its implicit Write Defaults value.");

            for (int cycle = 0; cycle < 2; cycle++)
            {
                animator.SetBool("feature_toggle", true);
                animator.SetBool(featureAlias, true);
                Step(animator);
                Assert(Mathf.Abs(light.intensity - 4f) < 0.01f && Mathf.Abs(cloneLight.intensity - 4f) < 0.01f,
                    "Feature re-enable failed at cycle " + (cycle + 1) + ".");
                animator.SetBool("feature_toggle", false);
                animator.SetBool(featureAlias, false);
                Step(animator);
                Assert(Mathf.Abs(light.intensity - rootDefault) < 0.01f && Mathf.Abs(cloneLight.intensity - cloneDefault) < 0.01f,
                    "Feature reset failed at cycle " + (cycle + 1) + ".");
            }
            Debug.Log("NXCLONE_WRITE_DEFAULTS_SMOKE_OK: original and clone clothing reset through repeated native Animator toggles with Auto Write Defaults.");
        }
        finally
        {
            if (window) UnityEngine.Object.DestroyImmediate(window);
            if (root) UnityEngine.Object.DestroyImmediate(root);
            if (temporaryAvatar && !AssetDatabase.Contains(temporaryAvatar)) UnityEngine.Object.DestroyImmediate(temporaryAvatar);
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.SaveAssets();
        }
    }

    static void AddBoolTransition(AnimatorState source, AnimatorState destination, string parameter, bool value)
    {
        var transition = source.AddTransition(destination);
        transition.hasExitTime = false;
        transition.duration = 0f;
        transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
    }

    static void Step(Animator animator)
    {
        animator.Update(0.1f);
        animator.Update(0.1f);
    }

    static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        if (!machine) yield break;
        foreach (var child in machine.states) yield return child.state;
        foreach (var child in machine.stateMachines)
            foreach (var state in States(child.stateMachine)) yield return state;
    }

    static void Set(object target, string field, object value) => typeof(NxCloneWindow)
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
