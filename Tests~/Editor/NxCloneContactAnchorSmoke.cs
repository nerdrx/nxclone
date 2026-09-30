using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;

public static class NxCloneContactAnchorSmoke
{
    public static void Run()
    {
        const string folder = "Assets/nxclone-contact-anchor-smoke";
        AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-contact-anchor-smoke");
        var avatar = new GameObject("contact avatar");
        var rootParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        rootParameters.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
        var fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/root.controller");
        try
        {
            var anchor = new GameObject("hand anchor").transform;
            anchor.SetParent(avatar.transform, false);
            var driver = NewDriver(avatar.transform, anchor, "clone one driver");
            var trajectory = new GameObject("recorded trajectory source").transform;
            trajectory.SetParent(avatar.transform, false);
            driver.GetComponent<VRCParentConstraint>().Sources.Add(new VRCConstraintSource(trajectory, 0.25f));
            var result = NxCloneContactAnchor.Configure(avatar.transform, driver, fx,
                rootParameters, 1, folder, "clone-one", "HandL");

            Assert(result.root && result.trackingPoint == result.root.transform.Find("Contact Tracker/Tracking Points"), "Tracker hierarchy/reference was not preserved.");
            Assert(anchor.Find("Tracker Target") && anchor.Find("Tracker Target").parent == anchor,
                "Tracker fallback target does not follow the original anchor.");
            var detachedTarget = anchor.Find("Tracker Target");
            var activeSource0s = new List<VRCConstraintSource>();
            AddActiveSource0s(result.root.GetComponentsInChildren<VRCParentConstraint>(true).Select(c => c.Sources), activeSource0s);
            AddActiveSource0s(result.root.GetComponentsInChildren<VRCPositionConstraint>(true).Select(c => c.Sources), activeSource0s);
            AddActiveSource0s(result.root.GetComponentsInChildren<VRCRotationConstraint>(true).Select(c => c.Sources), activeSource0s);
            AddActiveSource0s(result.root.GetComponentsInChildren<VRCScaleConstraint>(true).Select(c => c.Sources), activeSource0s);
            var targetSources = activeSource0s.Where(source => source.SourceTransform == detachedTarget).ToArray();
            Assert(targetSources.Length >= 4 && activeSource0s.All(source => source.SourceTransform &&
                       source.SourceTransform.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(source.SourceTransform)),
                "Every weighted tracker/backend source0 must resolve to a local scene Transform, including the detached Target.");
            var placement = driver.GetComponent<VRCParentConstraint>();
            Assert(placement.Sources.Count == 3 && placement.Sources[0].SourceTransform == anchor &&
                   placement.Sources[1].SourceTransform == trajectory && placement.Sources[1].Weight == 0.25f &&
                   placement.Sources[2].SourceTransform == result.trackingPoint && Mathf.Approximately(placement.Sources[2].Weight, 0f),
                "The contact tracking point was not appended as a disabled placement source.");
            Assert(result.parameters.Length == 8 && result.parameters.Count(p => p.networkSynced) == 1 && result.parameters.Single(p => p.networkSynced).name == result.parameterName && result.syncedParameterCost == 1,
                "Contact receiver/controller parameters should be local and cost no synced bits.");
            Assert(result.parameterName == result.remoteAttachBoolParameter && result.parameterNames.ContainsKey("ContactTracker/Control"),
                "Remote attach toggle was not namespaced and returned.");
            Assert(result.menu && result.menu.controls.Count == 1 &&
                   result.menu.controls[0].parameter.name == result.parameterName,
                "Contact menu toggle does not control the merged FX bool.");
            Assert(result.writeDefaultsOnLayers.Length == 2 && result.writeDefaultsOnLayers.All(name => fx.layers.Any(layer => layer.name == name)),
                "Required Contact Tracker Write Defaults layers were not reported.");

            var receivers = result.root.GetComponentsInChildren<Component>(true)
                .Where(c => c && c.GetType().FullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver").ToArray();
            Assert(receivers.Length == 6, "Contact Tracker must contain six receivers.");
            foreach (var receiver in receivers)
            {
                var serialized = new SerializedObject(receiver);
                Assert(serialized.FindProperty("collisionTags").GetArrayElementAtIndex(0).stringValue == "HandL", "Receiver tag was not retargeted.");
                Assert(!serialized.FindProperty("allowSelf").boolValue && serialized.FindProperty("allowOthers").boolValue,
                    "Default receiver self/other filters are wrong.");
            }

            var copiedTrackerClip = fx.layers.Where(layer => result.writeDefaultsOnLayers.Contains(layer.name))
                .SelectMany(layer => layer.stateMachine.states)
                .Select(state => state.state.motion as AnimationClip)
                .FirstOrDefault(clip => clip && AnimationUtility.GetCurveBindings(clip)
                    .Any(binding => binding.path == "clone-one contact/Contact Tracker/Tracking Points"));
            Assert(copiedTrackerClip, "Upstream controller clip was not retargeted to the generated tracker hierarchy.");

            var anchorLayer = fx.layers.Single(layer => layer.name == "nxclone contact anchor 1");
            var anchorStates = anchorLayer.stateMachine.states.ToDictionary(state => state.state.name, state => state.state);
            var driverPath = AnimationUtility.CalculateTransformPath(driver, avatar.transform);
            var liveBindings = AnimationUtility.GetCurveBindings((AnimationClip)anchorStates["Original anchor"].motion);
            var trackedBindings = AnimationUtility.GetCurveBindings((AnimationClip)anchorStates["Contact target"].motion);
            var liveClip = (AnimationClip)anchorStates["Original anchor"].motion;
            Assert(Mathf.Approximately(AnimationUtility.GetEditorCurve(liveClip,
                       EditorCurveBinding.FloatCurve(driverPath, typeof(VRCParentConstraint), "Sources.source0.Weight")).Evaluate(0), 1f) &&
                   Mathf.Approximately(AnimationUtility.GetEditorCurve(liveClip,
                       EditorCurveBinding.FloatCurve(driverPath, typeof(VRCParentConstraint), "Sources.source1.Weight")).Evaluate(0), 0.25f) &&
                   Mathf.Approximately(AnimationUtility.GetEditorCurve(liveClip,
                       EditorCurveBinding.FloatCurve(driverPath, typeof(VRCParentConstraint), "Sources.source2.Weight")).Evaluate(0), 0f),
                "Live state must restore the captured unmanaged source weights and mute the contact source.");
            Assert(trackedBindings.Any(binding => binding.path == driverPath && binding.propertyName == "Sources.source0.Weight") &&
                   trackedBindings.Any(binding => binding.path == driverPath && binding.propertyName == "Sources.source1.Weight") &&
                   trackedBindings.Any(binding => binding.path == driverPath && binding.propertyName == "Sources.source2.Weight"),
                "Contact state does not select the tracking point while muting the original anchor.");
            Assert(AnimationUtility.GetEditorCurve((AnimationClip)anchorStates["Contact target"].motion,
                       EditorCurveBinding.FloatCurve(driverPath, typeof(VRCParentConstraint), "Sources.source1.Weight"))
                       .keys.All(key => Mathf.Approximately(key.value, 0f)),
                "Contact attach did not mute the pre-existing trajectory source.");
            TestContactWeightStateGraph(avatar.transform, driver, placement, fx, result.parameterName, 1f, 0.25f);
            Assert(!liveBindings.Concat(trackedBindings).Any(binding => binding.propertyName.Contains("FreezeToWorld", StringComparison.Ordinal)),
                "Contact adapter took ownership of world-drop freeze state.");
            TestRecordingOwnedSourceWeight(folder);

            var otherAnchor = new GameObject("other anchor").transform;
            otherAnchor.SetParent(avatar.transform, false);
            var otherDriver = NewDriver(avatar.transform, otherAnchor, "clone two driver");
            var other = NxCloneContactAnchor.Configure(avatar.transform, otherDriver, fx,
                rootParameters, 2, folder, "clone-two", "Head", allowSelf: true, allowOthers: false);
            foreach (var receiver in other.root.GetComponentsInChildren<Component>(true)
                         .Where(c => c && c.GetType().FullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver"))
            {
                var serialized = new SerializedObject(receiver);
                Assert(serialized.FindProperty("collisionTags").GetArrayElementAtIndex(0).stringValue == "Head", "Second clone contact tag was not configurable.");
                Assert(serialized.FindProperty("allowSelf").boolValue && !serialized.FindProperty("allowOthers").boolValue,
                    "Self-only receiver permissions were not applied.");
            }
            Assert(other.writeDefaultsOnLayers.SelectMany(name => fx.layers.Single(layer => layer.name == name).stateMachine.states)
                    .Select(state => state.state.motion as AnimationClip).Where(clip => clip)
                    .SelectMany(clip => AnimationUtility.GetCurveBindings(clip)
                        .Where(binding => binding.type.FullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver" && binding.propertyName == "allowOthers")
                        .Select(binding => AnimationUtility.GetEditorCurve(clip, binding)))
                    .All(curve => curve.keys.All(key => Mathf.Approximately(key.value, 0f))),
                "The source controller re-enabled other-avatar contacts after a self-only setting.");

            int layersBeforeInvalid = fx.layers.Length;
            bool missingTagRejected = false;
            try
            {
                var thirdAnchor = new GameObject("third anchor").transform;
                thirdAnchor.SetParent(avatar.transform, false);
                var thirdDriver = NewDriver(avatar.transform, thirdAnchor, "clone three driver");
                NxCloneContactAnchor.Configure(avatar.transform, thirdDriver, fx, rootParameters,
                    3, folder, "clone-three", " ");
            }
            catch (ArgumentException e) when (e.Message.Contains("matching Contact Sender", StringComparison.Ordinal))
            {
                missingTagRejected = true;
            }
            Assert(missingTagRejected && fx.layers.Length == layersBeforeInvalid,
                "Missing sender tag was not rejected before changing the avatar/controller.");
            TestWindowIntegration(folder);
            Debug.Log("NXCLONE_CONTACT_ANCHOR_SMOKE_OK");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rootParameters);
            UnityEngine.Object.DestroyImmediate(avatar);
            AssetDatabase.DeleteAsset(folder);
            AssetDatabase.SaveAssets();
        }
    }

    static void TestWindowIntegration(string folder)
    {
        const string integrationFolder = "Assets/nxclone-contact-anchor-smoke/window";
        AssetDatabase.CreateFolder(folder, "window");
        var avatar = new GameObject("contact integration avatar");
        var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
        var fx = AnimatorController.CreateAnimatorControllerAtPath(integrationFolder + "/root.controller");
        descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer {
            type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, isEnabled = true, animatorController = fx
        } };
        descriptor.customizeAnimationLayers = true;
        descriptor.customExpressions = true;
        descriptor.expressionsMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        descriptor.expressionsMenu.controls = new List<VRCExpressionsMenu.Control>();
        descriptor.expressionParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        descriptor.expressionParameters.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
        var originalMenu = descriptor.expressionsMenu;
        var originalParameters = descriptor.expressionParameters;
        try
        {
            var group = new GameObject("nxclone").transform;
            group.SetParent(avatar.transform, false);
            var anchor = new GameObject("hand anchor").transform;
            anchor.SetParent(avatar.transform, false);
            var driver = NewDriver(group, anchor, "placement-1");
            var clone = new GameObject("clone-1").transform;
            clone.SetParent(driver, false);
            clone.gameObject.SetActive(false);

            var window = ScriptableObject.CreateInstance<NxCloneWindow>();
            Set(window, "avatar", descriptor);
            Set(window, "slots", new List<NxCloneSlot> { new NxCloneSlot {
                contactAnchor = true, contactTag = "HandL", contactAllowOthers = true
            } });
            Set(window, "worldDrop", true);
            Set(window, "copyFxAnimations", false);
            Set(window, "copyVisemes", false);
            Set(window, "recordingSamples", 15);
            Set(window, "recording", true);
            Assert((int)typeof(NxCloneWindow).GetMethod("EffectiveRecordingSamples", BindingFlags.Instance | BindingFlags.NonPublic)
                       .Invoke(window, null) == 14,
                "Contact-enabled recording was not capped at 14 samples.");
            Set(window, "recording", false);
            typeof(NxCloneWindow).GetMethod("InstallToggle", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { descriptor, integrationFolder, group, new List<Transform> { clone }, new List<Transform>() });

            var installedFx = (AnimatorController)descriptor.baseAnimationLayers[0].animatorController;
            var cloneMenu = descriptor.expressionsMenu.controls.Single(control => control.name == "nxclone").subMenu.controls
                .Single(control => control.name == "Clone 1").subMenu;
            var contactMenu = cloneMenu.controls.Single(control => control.name == "Contact attach").subMenu;
            string contactParameter = contactMenu.controls[0].parameter.name;
            Assert(descriptor.expressionParameters.parameters.Any(parameter => parameter.name == contactParameter &&
                       parameter.valueType == VRCExpressionParameters.ValueType.Bool && parameter.networkSynced && !parameter.saved),
                "Window integration did not merge the local, unsynced contact toggle parameter.");
            Assert(installedFx.layers.Count(layer => layer.name.Contains("Contact Tracker")) == 2 &&
                   installedFx.layers.Where(layer => layer.name.Contains("Contact Tracker"))
                       .All(layer => layer.stateMachine.states.All(state => state.state.writeDefaultValues)),
                "Window Write Defaults processing changed the Contact Tracker's required ON layers.");
            Assert(installedFx.layers.Any(layer => layer.name == "nxclone 1 placement") &&
                       AssetDatabase.LoadAssetAtPath<AnimationClip>(integrationFolder + "/clone-1-world.anim"),
                "Adding contact attachment dropped the independent world-drop control.");
            UnityEngine.Object.DestroyImmediate(window);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(originalMenu);
            UnityEngine.Object.DestroyImmediate(originalParameters);
            UnityEngine.Object.DestroyImmediate(avatar);
        }
    }

    static void TestContactWeightStateGraph(Transform avatarRoot, Transform driver,
        VRCParentConstraint placement, AnimatorController fx, string parameter,
        float expectedSource0, float expectedSource1)
    {
        var animator = avatarRoot.gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = fx;
        try
        {
            animator.Update(0.1f);
            AssertWeights(placement, expectedSource0, expectedSource1, 0f,
                "Contact-off did not restore unmanaged original weights.");
            animator.SetBool(parameter, true);
            animator.Update(0.1f);
            AssertWeights(placement, 0f, 0f, 1f,
                "Contact-on did not select the tracker while muting existing sources.");
            animator.SetBool(parameter, false);
            animator.Update(0.1f);
            AssertWeights(placement, expectedSource0, expectedSource1, 0f,
                "On-to-Off did not resume the original placement weights.");
        }
        finally { UnityEngine.Object.DestroyImmediate(animator); }
    }

    static void TestRecordingOwnedSourceWeight(string folder)
    {
        string testFolder = folder + "/recording-owned";
        AssetDatabase.CreateFolder(folder, "recording-owned");
        var avatar = new GameObject("recording contact avatar");
        var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        parameters.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
        var anchor = new GameObject("recording anchor").transform;
        anchor.SetParent(avatar.transform, false);
        var driver = NewDriver(avatar.transform, anchor, "recording driver");
        var trajectory = new GameObject("recording trajectory").transform;
        trajectory.SetParent(avatar.transform, false);
        var placement = driver.GetComponent<VRCParentConstraint>();
        placement.Sources.Add(new VRCConstraintSource(trajectory, 0.25f));
        var fx = AnimatorController.CreateAnimatorControllerAtPath(testFolder + "/root.controller");
        try
        {
            string driverPath = AnimationUtility.CalculateTransformPath(driver, avatar.transform);
            var recordedWeights = new AnimationClip { name = "recorded contact source weights", frameRate = 60f };
            AnimationUtility.SetEditorCurve(recordedWeights,
                EditorCurveBinding.FloatCurve(driverPath, typeof(VRCParentConstraint), "Sources.source1.Weight"),
                AnimationCurve.Constant(0, 1, 0.4f));
            AssetDatabase.CreateAsset(recordedWeights, testFolder + "/recorded-weights.anim");
            var machine = new AnimatorStateMachine { name = "recorded placement weights" };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var recorded = machine.AddState("recorded weights");
            recorded.motion = recordedWeights;
            machine.defaultState = recorded;
            fx.AddLayer(new AnimatorControllerLayer { name = "recorded placement weights", defaultWeight = 1f, stateMachine = machine });

            var result = NxCloneContactAnchor.Configure(avatar.transform, driver, fx, parameters,
                2, testFolder, "recording-owned-contact", "HandL");
            var layer = fx.layers.Single(item => item.name == "nxclone contact anchor 2");
            var states = layer.stateMachine.states.ToDictionary(entry => entry.state.name, entry => entry.state);
            var off = (AnimationClip)states["Original anchor"].motion;
            var offSource1 = EditorCurveBinding.FloatCurve(driverPath, typeof(VRCParentConstraint), "Sources.source1.Weight");
            Assert(AnimationUtility.GetEditorCurve(off, offSource1) == null,
                "Contact-off must leave an existing recording source-weight curve unbound.");

            var animator = avatar.AddComponent<Animator>();
            animator.runtimeAnimatorController = fx;
            animator.Update(0.1f);
            AssertWeights(placement, 1f, 0.4f, 0f,
                "Contact-off did not allow the existing recording weight to play.");
            animator.SetBool(result.parameterName, true);
            animator.Update(0.1f);
            AssertWeights(placement, 0f, 0f, 1f,
                "Contact-on did not temporarily override the recording source weights.");
            animator.SetBool(result.parameterName, false);
            animator.Update(0.1f);
            AssertWeights(placement, 1f, 0.4f, 0f,
                "Contact-off did not resume the recording layer's source weights.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(parameters);
            UnityEngine.Object.DestroyImmediate(avatar);
            AssetDatabase.DeleteAsset(testFolder);
        }
    }

    static void AssertWeights(VRCParentConstraint placement, float source0, float source1,
        float contact, string message)
    {
        Assert(Mathf.Abs(placement.Sources[0].Weight - source0) < 0.03f &&
               Mathf.Abs(placement.Sources[1].Weight - source1) < 0.03f &&
               Mathf.Abs(placement.Sources[2].Weight - contact) < 0.03f, message);
    }

    static void AddActiveSource0s(IEnumerable<VRCConstraintSourceKeyableList> constraints,
        List<VRCConstraintSource> output)
    {
        foreach (var sources in constraints)
        {
            if (sources.Count > 0 && sources[0].Weight > 0f) output.Add(sources[0]);
        }
    }

    static void Set(NxCloneWindow window, string name, object value) =>
        typeof(NxCloneWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);

    static Transform NewDriver(Transform parent, Transform anchor, string name)
    {
        var driver = new GameObject(name).transform;
        driver.SetParent(parent, false);
        driver.position = anchor.position;
        var placement = driver.gameObject.AddComponent<VRCParentConstraint>();
        placement.Sources.Add(new VRCConstraintSource(anchor, 1f));
        placement.ActivateConstraint();
        placement.ApplyConfigurationChanges();
        return driver;
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
