using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nxclone;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;

public static class NxCloneLimbIkSmoke
{
    [MenuItem("Tools/nxclone/Run Limb IK serialization smoke")]
    public static void Run()
    {
        const string folder = "Assets/nxclone-limbik-smoke";
        if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-limbik-smoke");
        GameObject testRoot = null;
        GameObject sourceRootObject = null;
        Avatar temporaryAvatar = null;
        try
        {
            var sourceAnimator = CreateSyntheticHumanoid();
            sourceRootObject = sourceAnimator.gameObject;
            temporaryAvatar = sourceAnimator.avatar;
            var sourceRoot = sourceAnimator.transform;
            testRoot = new GameObject("nxclone LimbIK smoke root");
            var frame = new GameObject("frozen placement frame").transform;
            frame.SetParent(testRoot.transform, false);
            NxClonePlacement.FreezeFrame(frame);
            var driver = new GameObject("clone placement driver").transform;
            driver.SetParent(frame, false);
            var clone = UnityEngine.Object.Instantiate(sourceRoot.gameObject, driver);
            clone.name = "clone visual";
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
                if (component && !(component is Transform)) UnityEngine.Object.DestroyImmediate(component);
            var fkGate = new GameObject("nxclone constraints left arm");
            fkGate.transform.SetParent(clone.transform, false);
            var posingGate = new GameObject("nxclone pose left arm");
            posingGate.transform.SetParent(clone.transform, false);
            var freezeGate = new GameObject("nxclone freeze constraints 1");
            freezeGate.transform.SetParent(clone.transform, false);

            var result = NxCloneLimbIk.Configure(sourceAnimator, sourceRoot, clone.transform, driver,
                testRoot.transform, folder, 1, "nxclone_ik_1");
            if (result.Solvers.Length != 4 || result.GoalHandles.Length != 4 || result.BendHints.Length != 4)
                throw new Exception("Expected four dormant solvers, grab handles, and bend hints.");
            for (int i = 0; i < result.Solvers.Length; i++)
            {
                if (!result.Solvers[i] || result.Solvers[i].gameObject.activeSelf ||
                    !result.Solvers[i].transform.IsChildOf(clone.transform) ||
                    result.GoalHandles[i].activeSelf || result.BendHints[i].activeSelf)
                    throw new Exception("Limb IK and its handles must be dormant by default.");
                var ik = new SerializedObject(result.Solvers[i]);
                var b1 = ik.FindProperty("solver.bone1.transform");
                var b2 = ik.FindProperty("solver.bone2.transform");
                var b3 = ik.FindProperty("solver.bone3.transform");
                var target = ik.FindProperty("solver.target");
                var bend = ik.FindProperty("solver.bendGoal");
                var goal = ik.FindProperty("solver.goal");
                if (b1 == null || b2 == null || b3 == null || target == null || bend == null || goal == null ||
                    !((Transform)b1.objectReferenceValue).IsChildOf(clone.transform) ||
                    !((Transform)b2.objectReferenceValue).IsChildOf(clone.transform) ||
                    !((Transform)b3.objectReferenceValue).IsChildOf(clone.transform) ||
                    !((Transform)b2.objectReferenceValue).IsChildOf((Transform)b1.objectReferenceValue) ||
                    !((Transform)b3.objectReferenceValue).IsChildOf((Transform)b2.objectReferenceValue) ||
                    !((Transform)target.objectReferenceValue).IsChildOf(driver) ||
                    ((Transform)target.objectReferenceValue).parent != result.GoalHandles[i].transform ||
                    !((Transform)bend.objectReferenceValue).IsChildOf(driver))
                    throw new Exception("A serialized solver bone chain, goal, or pole is invalid or references the source avatar.");
                var mappedEnd = (Transform)b3.objectReferenceValue;
                var targetTransform = (Transform)target.objectReferenceValue;
                if (Vector3.Distance(targetTransform.position, mappedEnd.position) > 1e-4f ||
                    targetTransform.localPosition.sqrMagnitude < 1e-8f)
                    throw new Exception("Goal child must start at its mapped hand/foot endpoint with meaningful reach for PhysBone grabbing.");

                var physBone = result.GoalHandles[i].GetComponent("VRCPhysBone");
                if (!physBone) throw new Exception("Goal handle is missing its SDK PhysBone.");
                var pb = new SerializedObject(physBone);
                var root = pb.FindProperty("rootTransform");
                var grabbing = pb.FindProperty("allowGrabbing");
                if (root == null || root.objectReferenceValue != result.GoalHandles[i].transform ||
                    grabbing == null || grabbing.enumDisplayNames[grabbing.enumValueIndex] != "True")
                    throw new Exception("Goal PhysBone root or grabbing setting was not serialized.");
                if (result.GoalHandles[i].transform.parent != clone.transform || result.BendHints[i].transform.parent != clone.transform)
                    throw new Exception("Goal and bend controls must be under the scaled clone, itself beneath the frozen placement driver.");
                var anchor = result.GoalHandles[i].GetComponent<VRCPositionConstraint>();
                if (!anchor || anchor.Sources.Count != 1 || anchor.Sources[0].SourceTransform != (Transform)b1.objectReferenceValue)
                    throw new Exception("Goal root must follow its mapped upper arm/leg anchor using native position constraint.");
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/limb-ik.controller");
            controller.AddParameter("nxclone_ik_1", AnimatorControllerParameterType.Bool);
            controller.AddParameter("nxclone_freeze_1", AnimatorControllerParameterType.Bool);
            controller.AddParameter("nxclone_enabled_1", AnimatorControllerParameterType.Bool);
            controller.AddParameter("nxclone_play_1", AnimatorControllerParameterType.Bool);
            controller.AddParameter("nxclone_visible_1", AnimatorControllerParameterType.Bool);
            NxCloneLimbIk.AddLayer(controller, result, false, "nxclone_freeze_1", "nxclone_enabled_1",
                "nxclone_play_1", "nxclone_visible_1");
            var layer = controller.layers.Single(item => item.name == result.LayerName);
            if (layer.stateMachine.defaultState.motion != result.Off)
                throw new Exception("Limb IK toggle must start in the Off state.");
            var selectorLayer = controller.layers.Single(item => item.name == result.SelectorLayerName);
            if (!Mathf.Approximately(layer.defaultWeight, 0) || !Mathf.Approximately(selectorLayer.defaultWeight, 1))
                throw new Exception("IK content must use a zero-weight layer controlled by the SDK selector.");
            var selectorStates = selectorLayer.stateMachine.states.Select(entry => entry.state).ToArray();
            var selectorOff = selectorStates.Single(candidate => candidate.name == "Off");
            var selectorOn = selectorStates.Single(candidate => candidate.name == "On");
            foreach (var (stateName, expectedWeight) in new[] { ("Off", 0f), ("On", 1f) })
            {
                var state = selectorStates.Single(candidate => candidate.name == stateName);
                var control = state.behaviours.OfType<VRCAnimatorLayerControl>().Single();
                int contentLayerIndex = Array.FindIndex(controller.layers, candidate => candidate.name == result.LayerName);
                if (control.layer != contentLayerIndex || control.playable != VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.FX ||
                    !Mathf.Approximately(control.goalWeight, expectedWeight) || !Mathf.Approximately(control.blendDuration, 0))
                    throw new Exception("SDK selector does not switch the content layer weight correctly.");
            }
            var enabledTransition = selectorOff.transitions.Single(transition => transition.destinationState == selectorOn);
            var disabledTransitions = selectorOn.transitions.Where(transition => transition.destinationState == selectorOff).ToArray();
            var expectedGuards = new[] { "nxclone_ik_1", "nxclone_freeze_1", "nxclone_enabled_1", "nxclone_play_1", "nxclone_visible_1" };
            if (enabledTransition.conditions.Length != expectedGuards.Length || disabledTransitions.Length != expectedGuards.Length ||
                !expectedGuards.All(name => enabledTransition.conditions.Any(condition => condition.parameter == name)) ||
                disabledTransitions.Any(transition => transition.conditions.Length != 1) ||
                !expectedGuards.All(name => disabledTransitions.Any(transition => transition.conditions[0].parameter == name)))
                throw new Exception("IK selector must require every visibility/live/not-playing/not-frozen guard, then turn off when any guard fails.");
            var liveVisible = new Dictionary<string, bool> {
                ["nxclone_ik_1"] = true, ["nxclone_freeze_1"] = false, ["nxclone_enabled_1"] = true,
                ["nxclone_play_1"] = false, ["nxclone_visible_1"] = true
            };
            if (!Matches(enabledTransition, liveVisible) ||
                disabledTransitions.Any(transition => Matches(transition, liveVisible)))
                throw new Exception("Manual SDK condition mirror rejects the fully enabled IK state.");
            foreach (var guard in expectedGuards)
            {
                var blocked = new Dictionary<string, bool>(liveVisible) { [guard] = !liveVisible[guard] };
                if (Matches(enabledTransition, blocked) || !disabledTransitions.Any(transition => Matches(transition, blocked)))
                    throw new Exception("Manual SDK condition mirror failed to disable IK when guard " + guard + " blocks it.");
            }
            var expectedGoals = new[] { "LeftHand", "RightHand", "LeftFoot", "RightFoot" };
            for (int i = 0; i < result.Solvers.Length; i++)
            {
                var goal = new SerializedObject(result.Solvers[i]).FindProperty("solver.goal");
                if (goal == null || goal.enumNames[goal.enumValueIndex] != expectedGoals[i])
                    throw new Exception("FinalIK solver goal does not match its hand/foot endpoint.");
            }
            var fkPath = AnimationUtility.CalculateTransformPath(fkGate.transform, testRoot.transform);
            var gate = EditorCurveBinding.FloatCurve(fkPath, typeof(GameObject), "m_IsActive");
            var posingPath = AnimationUtility.CalculateTransformPath(posingGate.transform, testRoot.transform);
            var freezePath = AnimationUtility.CalculateTransformPath(freezeGate.transform, testRoot.transform);
            var posingBinding = EditorCurveBinding.FloatCurve(posingPath, typeof(GameObject), "m_IsActive");
            var freezeBinding = EditorCurveBinding.FloatCurve(freezePath, typeof(GameObject), "m_IsActive");
            if (!IsDisabled(result.On, gate) || !IsDisabled(result.On, posingBinding) ||
                !IsDisabled(result.On, freezeBinding) ||
                AnimationUtility.GetEditorCurve(result.Off, gate) != null ||
                AnimationUtility.GetEditorCurve(result.Off, posingBinding) != null ||
                AnimationUtility.GetEditorCurve(result.Off, freezeBinding) != null ||
                layer.stateMachine.states.Any(state => state.state.writeDefaultValues))
                throw new Exception("IK On must take FK/posing/freeze ownership and Off must yield every gate back to lower layers with Write Defaults disabled.");
            foreach (var clip in new[] { result.On, result.Off })
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                if (!bindings.Any(binding => binding.propertyName == "m_IsActive"))
                    throw new Exception("IK toggle clips are missing native GameObject active curves.");
            }
            Debug.Log("NXCLONE_LIMBIK_SERIALIZATION_SMOKE_OK (setup only; SDK stub does not execute FinalIK)");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
        finally
        {
            if (testRoot) UnityEngine.Object.DestroyImmediate(testRoot);
            if (sourceRootObject) UnityEngine.Object.DestroyImmediate(sourceRootObject);
            if (temporaryAvatar && !AssetDatabase.Contains(temporaryAvatar)) UnityEngine.Object.DestroyImmediate(temporaryAvatar);
            AssetDatabase.DeleteAsset(folder);
            AssetDatabase.Refresh();
        }
    }

    static Animator CreateSyntheticHumanoid()
    {
        var source = new GameObject("nxclone limb IK source");
        var hips = Bone(source.transform, "Hips", new Vector3(0, 1, 0));
        var spine = Bone(hips, "Spine", new Vector3(0, 0.2f, 0));
        var chest = Bone(spine, "Chest", new Vector3(0, 0.2f, 0));
        var head = Bone(Bone(chest, "Neck", new Vector3(0, 0.2f, 0)), "Head", new Vector3(0, 0.2f, 0));
        var leftShoulder = Bone(chest, "LeftShoulder", new Vector3(-0.12f, 0.08f, 0));
        var leftArm = Bone(leftShoulder, "LeftUpperArm", new Vector3(-0.2f, 0, 0));
        var leftForearm = Bone(leftArm, "LeftLowerArm", new Vector3(-0.22f, 0, 0));
        Bone(leftForearm, "LeftHand", new Vector3(-0.18f, 0, 0));
        var rightShoulder = Bone(chest, "RightShoulder", new Vector3(0.12f, 0.08f, 0));
        var rightArm = Bone(rightShoulder, "RightUpperArm", new Vector3(0.2f, 0, 0));
        var rightForearm = Bone(rightArm, "RightLowerArm", new Vector3(0.22f, 0, 0));
        Bone(rightForearm, "RightHand", new Vector3(0.18f, 0, 0));
        var leftLeg = Bone(hips, "LeftUpperLeg", new Vector3(-0.1f, -0.15f, 0));
        var leftShin = Bone(leftLeg, "LeftLowerLeg", new Vector3(0, -0.42f, 0));
        Bone(leftShin, "LeftFoot", new Vector3(0, -0.4f, 0.08f));
        var rightLeg = Bone(hips, "RightUpperLeg", new Vector3(0.1f, -0.15f, 0));
        var rightShin = Bone(rightLeg, "RightLowerLeg", new Vector3(0, -0.42f, 0));
        Bone(rightShin, "RightFoot", new Vector3(0, -0.4f, 0.08f));
        var bones = source.GetComponentsInChildren<Transform>(true);
        var human = new[] {
            ("Hips", hips), ("Spine", spine), ("Chest", chest), ("Neck", chest.Find("Neck")), ("Head", head),
            ("LeftShoulder", leftShoulder), ("LeftUpperArm", leftArm), ("LeftLowerArm", leftForearm), ("LeftHand", leftForearm.Find("LeftHand")),
            ("RightShoulder", rightShoulder), ("RightUpperArm", rightArm), ("RightLowerArm", rightForearm), ("RightHand", rightForearm.Find("RightHand")),
            ("LeftUpperLeg", leftLeg), ("LeftLowerLeg", leftShin), ("LeftFoot", leftShin.Find("LeftFoot")),
            ("RightUpperLeg", rightLeg), ("RightLowerLeg", rightShin), ("RightFoot", rightShin.Find("RightFoot"))
        }.Select(pair => new HumanBone { boneName = pair.Item2.name, humanName = pair.Item1, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        var skeleton = bones.Select(transform => new SkeletonBone {
            name = transform.name, position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale
        }).ToArray();
        var avatar = AvatarBuilder.BuildHumanAvatar(source, new HumanDescription {
            human = human, skeleton = skeleton, armStretch = 0.05f, legStretch = 0.05f,
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f,
            lowerLegTwist = 0.5f, feetSpacing = 0, hasTranslationDoF = false
        });
        if (!avatar || !avatar.isValid || !avatar.isHuman)
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            throw new Exception("Unity failed to build the synthetic humanoid rig.");
        }
        var animator = source.AddComponent<Animator>();
        animator.avatar = avatar;
        return animator;
    }

    static Transform Bone(Transform parent, string name, Vector3 position)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = position;
        return child;
    }

    static bool IsDisabled(AnimationClip clip, EditorCurveBinding binding)
    {
        var curve = AnimationUtility.GetEditorCurve(clip, binding);
        return curve != null && Mathf.Approximately(curve.Evaluate(0), 0);
    }

    static bool Matches(AnimatorStateTransition transition, System.Collections.Generic.IDictionary<string, bool> parameters) =>
        transition.conditions.All(condition => parameters.TryGetValue(condition.parameter, out var value) &&
            (condition.mode == AnimatorConditionMode.If ? value : condition.mode == AnimatorConditionMode.IfNot && !value));
}
