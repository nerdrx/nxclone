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
using VRC.SDKBase;

public static class NxCloneAfterimagesOnlySmoke
{
    const string Folder = "Assets/nxclone-afterimages-only-smoke";

    public static void Run()
    {
        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-afterimages-only-smoke");
        GameObject root = null;
        GameObject externalBone = null;
        Mesh mesh = null;
        Avatar temporaryAvatar = null;
        VRCExpressionParameters parameters = null;
        NxClonePreset preset = null;
        NxCloneWindow window = null;
        string[] originalGeneratedFolders = Array.Empty<string>();
        try
        {
            var factory = typeof(NxClonePosingSmoke).GetMethod("CreateSyntheticHumanoid", BindingFlags.NonPublic | BindingFlags.Static);
            var animator = (Animator)factory.Invoke(null, null);
            root = animator.gameObject;
            temporaryAvatar = animator.avatar;
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            mesh = new Mesh { name = "afterimages-only smoke mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 3).ToArray();
            mesh.bindposes = new[] { hips.worldToLocalMatrix * face.transform.localToWorldMatrix };
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { hips };
            renderer.rootBone = hips;

            parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.parameters = Enumerable.Range(0, 255).Select(i => new VRCExpressionParameters.Parameter {
                name = "afterimages_only_budget_" + i,
                valueType = VRCExpressionParameters.ValueType.Bool,
                saved = false,
                networkSynced = true
            }).ToArray();
            descriptor.customExpressions = true;
            descriptor.expressionParameters = parameters;

            preset = ScriptableObject.CreateInstance<NxClonePreset>();
            preset.slots = new List<NxCloneSlot>();
            preset.afterimages = true;
            AssetDatabase.CreateAsset(preset, Folder + "/empty.preset.asset");
            window = ScriptableObject.CreateInstance<NxCloneWindow>();
            Set(window, "avatar", descriptor);
            Set(window, "preset", preset);
            Set(window, "slots", new List<NxCloneSlot>());
            var loadPreset = typeof(NxCloneWindow).GetMethod("LoadPreset", BindingFlags.Instance | BindingFlags.NonPublic);
            var savePreset = typeof(NxCloneWindow).GetMethod("SavePreset", BindingFlags.Instance | BindingFlags.NonPublic);
            loadPreset.Invoke(window, null);
            Assert(GetSlots(window).Count == 0, "loading an empty preset must preserve zero clone slots");
            foreach (var option in new[] { "worldDrop", "poseFreeze", "copyVisemes", "copyFxAnimations", "independentCloneFx", "runtimeScale", "runtimePosition", "runtimeRotation", "posing", "limbIk", "limbContacts", "wear", "recording" })
                Set(window, option, true);
            Set(window, "afterimages", true);
            savePreset.Invoke(window, null);
            Assert(preset.slots != null && preset.slots.Count == 0, "saving must preserve zero clone slots");

            var preflight = typeof(NxCloneWindow).GetMethod("Preflight", BindingFlags.Static | BindingFlags.NonPublic);
            var rejected = (List<string>)preflight.Invoke(null, PreflightArguments(descriptor, GetSlots(window), false));
            Assert(rejected.Any(issue => issue.Contains("Add a clone or enable afterimages")),
                "zero clone slots without afterimages must fail preflight");
            var ready = (List<string>)preflight.Invoke(null, PreflightArguments(descriptor, GetSlots(window), true));
            Assert(ready.Count == 0,
                "afterimages-only preflight must ignore all clone-only flags and fit in the final parameter bit");
            externalBone = new GameObject("external bone");
            renderer.bones = new[] { externalBone.transform };
            var invalidRoot = (List<string>)preflight.Invoke(null, PreflightArguments(descriptor, GetSlots(window), true));
            Assert(invalidRoot.Any(issue => issue.Contains("bones reference objects outside the root avatar")),
                "afterimages-only preflight must reject root renderers with external bones");
            renderer.bones = new[] { hips };

            var setup = root.AddComponent<NxCloneSetup>();
            setup.slots = new List<NxCloneSetupSlot>();
            setup.worldDrop = setup.poseFreeze = setup.copyVisemes = setup.copyFxAnimations = setup.independentCloneFx = true;
            setup.runtimeScale = setup.runtimePosition = setup.runtimeRotation = true;
            setup.posing = setup.limbIk = setup.limbContacts = setup.wear = setup.recording = true;
            setup.afterimages = true;
            originalGeneratedFolders = AssetDatabase.IsValidFolder("Assets/nxclone-generated")
                ? AssetDatabase.GetSubFolders("Assets/nxclone-generated") : Array.Empty<string>();
            NxCloneWindow.BuildForUpload(setup);
            Assert(setup.slots.Count == 0, "upload generation must preserve the empty setup slot list");

            Assert(root.transform.Find("nxclone/world/trail-1/afterimage-1"), "afterimage visual must be built without clones");
            Assert(!root.GetComponentsInChildren<Transform>(true).Any(item => item.name.StartsWith("clone-", StringComparison.Ordinal)),
                "zero-slot generation must not create clone visuals");
            var fx = (AnimatorController)descriptor.baseAnimationLayers.Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            Assert(fx.parameters.Where(parameter => parameter.name.StartsWith("nxclone_", StringComparison.Ordinal))
                    .Select(parameter => parameter.name).SequenceEqual(new[] { "nxclone_afterimages" }),
                "afterimages-only generation must add only the afterimage parameter");
            Assert(!fx.layers.Any(layer => layer.name.StartsWith("nxclone ", StringComparison.Ordinal) &&
                    layer.name != "nxclone afterimages" && !layer.name.StartsWith("nxclone afterimage ", StringComparison.Ordinal)) &&
                   fx.layers.Count(layer => layer.name == "nxclone afterimages") == 1,
                "afterimages-only generation must not add clone FX layers");
            Assert(!fx.layers.Where(layer => layer.name.StartsWith("nxclone ", StringComparison.Ordinal))
                    .SelectMany(layer => States(layer.stateMachine)).SelectMany(state => state.behaviours)
                    .OfType<VRCAvatarParameterDriver>().Any(),
                "afterimages-only generation must not create parameter drivers");
            var controls = MenuTree(descriptor.expressionsMenu).SelectMany(menu => menu.controls ?? new List<VRCExpressionsMenu.Control>()).ToArray();
            Assert(controls.Length == 1 && controls[0].name == "Afterimages" &&
                   controls[0].type == VRCExpressionsMenu.Control.ControlType.Toggle &&
                   controls[0].parameter != null && controls[0].parameter.name == "nxclone_afterimages",
                "the only generated menu control must toggle afterimages");
            Assert(descriptor.expressionParameters.CalcTotalCost() == VRCExpressionParameters.MAX_PARAMETER_COST,
                "afterimages-only generation must consume exactly one bit at the 255-bit boundary");

            AssetDatabase.SaveAssets();
            Debug.Log("NXCLONE_AFTERIMAGES_ONLY_SMOKE_OK: zero clone slots, empty preset persistence, one-bit afterimage control");
        }
        finally
        {
            if (window) UnityEngine.Object.DestroyImmediate(window);
            if (root) UnityEngine.Object.DestroyImmediate(root);
            if (externalBone) UnityEngine.Object.DestroyImmediate(externalBone);
            if (temporaryAvatar) UnityEngine.Object.DestroyImmediate(temporaryAvatar);
            if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
            if (parameters) UnityEngine.Object.DestroyImmediate(parameters);
            if (preset) UnityEngine.Object.DestroyImmediate(preset);
            if (AssetDatabase.IsValidFolder("Assets/nxclone-generated"))
                foreach (var folder in AssetDatabase.GetSubFolders("Assets/nxclone-generated").Except(originalGeneratedFolders))
                    AssetDatabase.DeleteAsset(folder);
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.SaveAssets();
        }
    }

    static object[] PreflightArguments(VRCAvatarDescriptor descriptor, List<NxCloneSlot> slots, bool afterimages) => new object[] {
        descriptor, slots, afterimages, true, true, true, true, true, false, true, true, true, true, true,
        NxCloneAxes.All, NxCloneAxes.All
    };

    static List<NxCloneSlot> GetSlots(NxCloneWindow window) =>
        (List<NxCloneSlot>)typeof(NxCloneWindow).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);

    static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        foreach (var child in machine.states) yield return child.state;
        foreach (var nested in machine.stateMachines)
            foreach (var state in States(nested.stateMachine)) yield return state;
    }

    static IEnumerable<VRCExpressionsMenu> MenuTree(VRCExpressionsMenu root)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<VRCExpressionsMenu>();
        if (root) stack.Push(root);
        while (stack.Count > 0)
        {
            var menu = stack.Pop();
            if (!menu || !seen.Add(menu.GetInstanceID())) continue;
            yield return menu;
            foreach (var child in (menu.controls ?? new List<VRCExpressionsMenu.Control>())
                .Where(control => control.subMenu).Select(control => control.subMenu)) stack.Push(child);
        }
    }

    static void Set(NxCloneWindow window, string name, object value) =>
        typeof(NxCloneWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("nxclone afterimages-only smoke: " + message);
    }
}
