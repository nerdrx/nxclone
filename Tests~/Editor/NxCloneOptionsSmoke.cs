using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;

public static class NxCloneOptionsSmoke
{
    const string Folder = "Assets/nxclone-options-smoke";
    static readonly string[] Builtins = { "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight", "Viseme", "Voice" };
    static readonly string[] ReservedBuiltins = Builtins.Concat(new[] { "IsLocal" }).ToArray();

    public static void Run()
    {
        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-options-smoke");
        GameObject root = null;
        GameObject face = null;
        Mesh mesh = null;
        Avatar temporaryAvatar = null;
        VRCExpressionParameters parameters = null;
        VRCExpressionsMenu menu = null;
        NxCloneWindow window = null;
        try
        {
            var factory = typeof(NxClonePosingSmoke).GetMethod("CreateSyntheticHumanoid", BindingFlags.NonPublic | BindingFlags.Static);
            var animator = (Animator)factory.Invoke(null, null);
            root = animator.gameObject;
            temporaryAvatar = animator.avatar;
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            mesh = new Mesh { name = "options smoke face" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.boneWeights = new[] {
                new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                new BoneWeight { boneIndex0 = 0, weight0 = 1 }
            };
            mesh.bindposes = new[] { hips.worldToLocalMatrix * face.transform.localToWorldMatrix };
            mesh.AddBlendShapeFrame("Smile", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { hips };
            renderer.rootBone = hips;
            var baselineRendererObject = new GameObject("BaselineBody");
            baselineRendererObject.transform.SetParent(root.transform, false);
            var baselineRenderer = baselineRendererObject.AddComponent<SkinnedMeshRenderer>();
            baselineRenderer.sharedMesh = mesh;
            baselineRenderer.bones = new[] { hips };
            baselineRenderer.rootBone = hips;

            var sourceFx = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/source.controller");
            sourceFx.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
            sourceFx.AddParameter("IsLocal", AnimatorControllerParameterType.Float);
            parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var sourceParams = new List<VRCExpressionParameters.Parameter>();
            for (int i = 0; i < 8; i++)
            {
                string name = "root_menu_" + i;
                sourceFx.AddParameter(name, AnimatorControllerParameterType.Bool);
                sourceParams.Add(new VRCExpressionParameters.Parameter {
                    name = name, valueType = VRCExpressionParameters.ValueType.Bool,
                    saved = false, networkSynced = true
                });
            }
            parameters.parameters = sourceParams.ToArray();
            var sourceMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            sourceMenu.controls = sourceParams.Select(parameter => new VRCExpressionsMenu.Control {
                name = parameter.name, type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter.name }, value = 1
            }).ToList();
            var clip = new AnimationClip { name = "source blendshape and renderer FX" };
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.Smile"),
                AnimationCurve.Constant(0, 1, 100));
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "m_Enabled"),
                AnimationCurve.Constant(0, 1, 1));
            AssetDatabase.AddObjectToAsset(clip, sourceFx);
            var sourceMachine = new AnimatorStateMachine { name = "source renderer FX" };
            AssetDatabase.AddObjectToAsset(sourceMachine, sourceFx);
            var sourceState = sourceMachine.AddState("source FX");
            sourceState.motion = clip;
            sourceMachine.defaultState = sourceState;
            sourceFx.AddLayer(new AnimatorControllerLayer { name = "source renderer FX", stateMachine = sourceMachine });
            var sourceBindings = AnimationUtility.GetCurveBindings(clip).ToArray();

            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer {
                type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = sourceFx,
                isDefault = false, isEnabled = true
            } };
            descriptor.customExpressions = true;
            descriptor.expressionParameters = parameters;
            descriptor.expressionsMenu = sourceMenu;
            menu = sourceMenu;

            window = ScriptableObject.CreateInstance<NxCloneWindow>();
            Set(window, "avatar", descriptor);
            Set(window, "slots", new List<NxCloneSlot> {
                new NxCloneSlot { contactAnchor = true, contactTag = "HandL", contactAllowOthers = true },
                new NxCloneSlot()
            });
            foreach (var option in new[] { "worldDrop", "poseFreeze", "posing", "recording", "limbIk", "limbContacts", "wear", "afterimages", "runtimeScale", "independentCloneFx" })
                Set(window, option, true);
            Set(window, "writeDefaults", NxCloneWriteDefaults.On);
            Set(window, "afterimageCount", 2);
            Set(window, "recordingSamples", 15);
            Set(window, "recordingDuration", 5f);
            var sampleLimit = (int)typeof(NxCloneWindow).GetMethod("EffectiveRecordingSamples", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            Assert(sampleLimit == 13, "one contact anchor plus wear should cap a 15-sample request at 13");
            var build = typeof(NxCloneWindow).GetMethod("BuildVisuals", BindingFlags.Instance | BindingFlags.NonPublic);
            build.Invoke(window, new object[] { descriptor, Folder, true });

            var fx = (AnimatorController)descriptor.baseAnimationLayers.Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            var expressionParameters = descriptor.expressionParameters.parameters;
            Assert(expressionParameters.Select(parameter => parameter.name).Distinct(StringComparer.Ordinal).Count() == expressionParameters.Length,
                "generated expression parameter names must be unique");
            Assert(!expressionParameters.Any(parameter => ReservedBuiltins.Contains(parameter.name, StringComparer.Ordinal)),
                "VRChat built-ins must not be redeclared in expression parameters");
            Assert(descriptor.expressionParameters.CalcTotalCost() <= VRCExpressionParameters.MAX_PARAMETER_COST,
                "the integrated option set must stay within the 256-bit parameter budget");
            Assert(MenuTree(descriptor.expressionsMenu).All(item => item.controls == null || item.controls.Count <= 8),
                "root and nested menus must be paginated to eight controls or fewer");

            var clones = new[] {
                root.transform.Find("nxclone/world/placement-1/clone-1"),
                root.transform.Find("nxclone/world/placement-2/clone-2")
            };
            Assert(clones.All(clone => clone), "two linked clone visuals should be built");
            var layers = fx.layers;
            for (int i = 1; i <= 2; i++)
            {
                Assert(layers.Any(layer => layer.name == $"nxclone_pose_{i} pose recording") &&
                       layers.Any(layer => layer.name == $"nxclone_expression_{i} expression capture") &&
                       layers.Any(layer => layer.name == $"nxclone_expression_{i} expression playback"),
                    $"clone {i} should have body and expression capture/playback layers");
                var expressionCapture = layers.Single(layer => layer.name == $"nxclone_expression_{i} expression capture").stateMachine;
                Assert(expressionCapture.states.Count(entry => entry.state.name.StartsWith("sample ", StringComparison.Ordinal)) == sampleLimit,
                    $"clone {i} expression recording should use the capped sample count");
                var recordParameter = expressionParameters.SingleOrDefault(parameter => parameter.name == $"nxclone_pose_{i}_record");
                var takeParameter = expressionParameters.SingleOrDefault(parameter => parameter.name == $"nxclone_pose_{i}_take");
                var playParameter = expressionParameters.SingleOrDefault(parameter => parameter.name == $"nxclone_pose_{i}_play");
                var speedParameter = expressionParameters.SingleOrDefault(parameter => parameter.name == $"nxclone_pose_{i}_speed");
                Assert(playParameter != null && speedParameter != null,
                    $"clone {i} Play and speed parameters must be present");
                Assert(recordParameter != null && recordParameter.valueType == VRCExpressionParameters.ValueType.Bool &&
                       !recordParameter.networkSynced && !recordParameter.saved,
                    $"clone {i} Record command must be an unsaved, unsynced Bool");
                Assert(takeParameter != null && takeParameter.valueType == VRCExpressionParameters.ValueType.Bool &&
                       takeParameter.networkSynced && !takeParameter.saved,
                    $"clone {i} Take state must be an unsaved, synced Bool");
                var recordingCost = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                recordingCost.parameters = new[] { recordParameter, takeParameter, playParameter, speedParameter };
                int cloneRecordingCost;
                try { cloneRecordingCost = recordingCost.CalcTotalCost(); }
                finally { UnityEngine.Object.DestroyImmediate(recordingCost); }
                Assert(playParameter.valueType == VRCExpressionParameters.ValueType.Bool && playParameter.networkSynced &&
                       speedParameter.valueType == VRCExpressionParameters.ValueType.Float && speedParameter.networkSynced &&
                       cloneRecordingCost == 10,
                    $"clone {i} Take/Play/speed must preserve the existing 10-bit synced recording cost");
                var driver = clones[i - 1].parent.GetComponent<VRCParentConstraint>();
                Assert(driver && driver.Sources.Count <= 16, $"clone {i} sample/contact sources must stay at or below 16");
                Assert(fx.parameters.Any(parameter => parameter.name == $"nxclone_clone{i}_GestureLeft" && parameter.type == AnimatorControllerParameterType.Int),
                    $"clone {i} needs a correctly typed GestureLeft alias");
                Assert(!fx.parameters.Any(parameter => parameter.name == $"nxclone_clone{i}_IsLocal"),
                    $"clone {i} must not alias the non-expression IsLocal built-in");

                var inputCopy = layers.Single(layer => layer.name == "nxclone expression input copy nxclone_clone" + i + "_GestureLeft");
                var drivers = inputCopy.stateMachine.states.SelectMany(entry => entry.state.behaviours).OfType<VRCAvatarParameterDriver>().ToArray();
                var copies = drivers.SelectMany(driver => driver.parameters).Where(parameter => Builtins.Contains(parameter.source, StringComparer.Ordinal)).ToArray();
                Assert(Builtins.All(name => copies.Any(parameter => parameter.source == name)),
                    $"clone {i} should snapshot all six live expression inputs");
                Assert(copies.All(parameter => parameter.type == VRC_AvatarParameterDriver.ChangeType.Copy &&
                    parameter.name.StartsWith("nxclone_clone" + i + "_", StringComparison.Ordinal)) &&
                    !drivers.SelectMany(driver => driver.parameters).Any(parameter => ReservedBuiltins.Contains(parameter.name, StringComparer.Ordinal)),
                    $"clone {i} expression snapshots must only copy into private aliases");
            }
            Assert(sourceFx.layers.Single(layer => layer.name == "source renderer FX").stateMachine.states.Single().state.motion == clip &&
                   sourceBindings.SequenceEqual(AnimationUtility.GetCurveBindings(clip)),
                "independent clone FX must leave the original source clip and its blendshape/renderer bindings unchanged");

            var visibleContactToggles = MenuTree(descriptor.expressionsMenu).SelectMany(item => item.controls ?? new List<VRCExpressionsMenu.Control>())
                .Where(control => control.name == "Contact attach" && control.parameter != null)
                .Select(control => control.parameter.name).Where(name => !string.IsNullOrEmpty(name) && name.EndsWith("/Control", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).ToArray();
            var contactToggleNames = expressionParameters.Select(parameter => parameter.name)
                .Where(name => !string.IsNullOrEmpty(name) && name.EndsWith("/Control", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
            Assert(visibleContactToggles.Length == 9 && contactToggleNames.Length == 9 &&
                   contactToggleNames.All(visibleContactToggles.Contains),
                "two clone sets of four limb trackers plus the root contact tracker should each have a wired toggle");
            foreach (string name in contactToggleNames)
            {
                var parameter = expressionParameters.SingleOrDefault(entry => entry.name == name);
                Assert(parameter != null && parameter.valueType == VRCExpressionParameters.ValueType.Bool &&
                       parameter.networkSynced && !parameter.saved,
                    $"contact tracker toggle {name} must be one unsaved synced Bool bit");
            }
            var receiverType = typeof(VRCContactReceiver);
            Assert(root.GetComponentsInChildren(receiverType, true).Length == contactToggleNames.Length * 6,
                "each of the nine bundled contact trackers should retain its six receiver components");
            var contactTrackers = root.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.name == "Contact Tracker").ToArray();
            Assert(contactTrackers.Length == contactToggleNames.Length,
                "every generated contact control should have one instantiated Contact Tracker backend");
            foreach (var tracker in contactTrackers)
            {
                var constraints = new List<VRCConstraintSourceKeyableList>();
                constraints.AddRange(tracker.GetComponentsInChildren<VRCParentConstraint>(true).Select(component => component.Sources));
                constraints.AddRange(tracker.GetComponentsInChildren<VRCPositionConstraint>(true).Select(component => component.Sources));
                constraints.AddRange(tracker.GetComponentsInChildren<VRCRotationConstraint>(true).Select(component => component.Sources));
                constraints.AddRange(tracker.GetComponentsInChildren<VRCScaleConstraint>(true).Select(component => component.Sources));
                foreach (var sources in constraints)
                    for (int index = 0; index < sources.Count; index++)
                        if (sources[index].Weight > 0f)
                            Assert(sources[index].SourceTransform,
                                $"Contact Tracker backend {tracker.name} has a null positive-weight source at index {index}");
            }

            var ikType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("RootMotion.FinalIK.LimbIK", false)).First(type => type != null);
            var solvers = clones.SelectMany(clone => clone.GetComponentsInChildren(ikType, true)).Cast<Component>().ToArray();
            var frame = root.transform.Find("nxclone/world");
            var contactTargets = frame.GetComponentsInChildren<Transform>(true).Where(transform => transform.name.Contains("IK target")).ToHashSet();
            Assert(solvers.Length == 8 && solvers.All(solver => {
                var target = new SerializedObject(solver).FindProperty("solver.target");
                return target != null && contactTargets.Contains(target.objectReferenceValue as Transform);
            }), "all eight IK solvers should target their generated limb-contact drivers");

            var masks = root.GetComponentsInChildren<Transform>(true).Where(transform =>
                transform.name == "__nxclone main silhouette mask" || transform.name.StartsWith("__nxclone worn silhouette mask ", StringComparison.Ordinal)).ToArray();
            Assert(masks.Length == 3 && masks.All(mask => !mask.gameObject.activeSelf),
                "two afterimage wear masks and the main mask should be generated hidden");
            Assert(root.transform.Find("nxclone/world/trail-1/afterimage-1") && root.transform.Find("nxclone/world/trail-2/afterimage-2"),
                "both requested afterimages should be built");

            var wearSelector = layers.Single(layer => layer.name == "nxclone wear selector");
            var wearBaseline = layers.Single(layer => layer.name == "nxclone wear baseline");
            var baselineState = wearBaseline.stateMachine.defaultState;
            var baselineClip = baselineState.motion as AnimationClip;
            var baselineBindings = baselineClip ? AnimationUtility.GetCurveBindings(baselineClip)
                .Where(binding => binding.path == "BaselineBody" && binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName == "m_Enabled")
                .ToArray() : Array.Empty<EditorCurveBinding>();
            Assert(wearBaseline.defaultWeight == 1f && baselineState && !baselineState.writeDefaultValues &&
                   baselineBindings.Length == 1 && Mathf.Approximately(AnimationUtility.GetEditorCurve(baselineClip, baselineBindings[0]).Evaluate(0f), 1f),
                "missing renderer animation needs an always-active baseline layer with Write Defaults Off even when selected policy is On");
            var wearLayers = new[] { Array.FindIndex(layers, layer => layer.name == "nxclone wear 1"), Array.FindIndex(layers, layer => layer.name == "nxclone wear 2") };
            Assert(wearLayers.All(index => index >= 0 && Mathf.Approximately(layers[index].defaultWeight, 0f)),
                "wear should add one initially muted FX layer per clone");
            foreach (var state in wearSelector.stateMachine.states)
            {
                var controls = state.state.behaviours.OfType<VRCAnimatorLayerControl>().ToArray();
                Assert(controls.Length == 2 && controls.All(control => control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX && wearLayers.Contains(control.layer)),
                    "every wear selector state should address the final FX indexes for both wear layers");
                float expected1 = state.state.name == "Wear 1" ? 1f : 0f;
                float expected2 = state.state.name == "Wear 2" ? 1f : 0f;
                Assert(Mathf.Approximately(controls.Single(control => control.layer == wearLayers[0]).goalWeight, expected1) &&
                       Mathf.Approximately(controls.Single(control => control.layer == wearLayers[1]).goalWeight, expected2),
                    "wear selector states should weight only the selected clone layer");
            }
            for (int i = 1; i <= 2; i++)
            {
                string contentName = $"nxclone {i} native limb IK";
                var contentIndex = Array.FindIndex(layers, layer => layer.name == contentName);
                var selector = layers.Single(layer => layer.name == contentName + " selector");
                Assert(contentIndex >= 0, $"clone {i} should have an IK content layer");
                foreach (var state in selector.stateMachine.states)
                {
                    var control = state.state.behaviours.OfType<VRCAnimatorLayerControl>().Single();
                    float expected = state.state.name == "On" ? 1f : 0f;
                    Assert(control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX && control.layer == contentIndex &&
                           Mathf.Approximately(control.goalWeight, expected),
                        $"clone {i} IK selector must reference its final FX layer index");
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"NXCLONE_OPTIONS_SMOKE_OK: 2 clones, {expressionParameters.Length} unique parameters, {contactToggleNames.Length} tracker bits, {sampleLimit} samples, {solvers.Length} IK targets");
        }
        finally
        {
            if (window) UnityEngine.Object.DestroyImmediate(window);
            if (root) UnityEngine.Object.DestroyImmediate(root);
            if (temporaryAvatar) UnityEngine.Object.DestroyImmediate(temporaryAvatar);
            if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
            if (parameters) UnityEngine.Object.DestroyImmediate(parameters);
            if (menu) UnityEngine.Object.DestroyImmediate(menu);
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.SaveAssets();
        }
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
        if (!condition) throw new Exception("nxclone options smoke: " + message);
    }
}
