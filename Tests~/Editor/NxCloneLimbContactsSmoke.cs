using System;
using System.Linq;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;

public static class NxCloneLimbContactsSmoke
{
    public static void Run()
    {
        const string folder = "Assets/nxclone-limb-contacts-smoke";
        AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-limb-contacts-smoke");
        var avatarRoot = new GameObject("nxclone limb contact avatar");
        var rootParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        rootParameters.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
        Animator sourceAnimator = null;
        Avatar temporaryAvatar = null;
        Transform clone = null;
        AnimatorController fx = null;
        try
        {
            sourceAnimator = CreateSyntheticHumanoid();
            temporaryAvatar = sourceAnimator.avatar;
            var frame = new GameObject("frozen placement frame").transform;
            frame.SetParent(avatarRoot.transform, false);
            NxClonePlacement.FreezeFrame(frame);
            var originalAnchor = new GameObject("original clone root anchor").transform;
            originalAnchor.SetParent(avatarRoot.transform, false);
            var placementDriver = new GameObject("clone placement driver").transform;
            placementDriver.SetParent(frame, false);
            placementDriver.position = originalAnchor.position;
            var placement = placementDriver.gameObject.AddComponent<VRCParentConstraint>();
            placement.Sources.Add(new VRCConstraintSource(originalAnchor, 1f));
            placement.ActivateConstraint();
            placement.ApplyConfigurationChanges();
            var scale = placementDriver.gameObject.AddComponent<VRCScaleConstraint>();
            scale.Sources.Add(new VRCConstraintSource(originalAnchor, 1f));
            scale.ActivateConstraint();
            scale.ApplyConfigurationChanges();
            clone = UnityEngine.Object.Instantiate(sourceAnimator.gameObject, placementDriver).transform;
            clone.name = "clone visual";
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
                if (component && !(component is Transform)) UnityEngine.Object.DestroyImmediate(component);

            fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/root.controller");
            var ik = NxCloneLimbIk.Configure(sourceAnimator, sourceAnimator.transform, clone, placementDriver,
                avatarRoot.transform, folder, 1, "nxclone_ik_1");
            var originalTargets = ik.GoalHandles.Select(handle => handle.transform.Find("target")).ToArray();
            var originalLocalPositions = originalTargets.Select(target => target.localPosition).ToArray();
            var result = NxCloneLimbContacts.Configure(avatarRoot.transform, placementDriver, ik, fx,
                rootParameters, 1, folder, "clone_one");

            Assert(result.Contacts.Length == 4 && result.TargetDrivers.Length == 4,
                "Limb contact setup must create four trackers and target drivers.");
            Assert(result.Contacts.Select(contact => contact.parameterName).Distinct().Count() == 4,
                "Each limb contact toggle must have a unique namespaced parameter.");
            Assert(frame.GetComponent<VRCParentConstraint>().FreezeToWorld && frame.GetComponent<VRCScaleConstraint>().FreezeToWorld,
                "Limb contact setup changed frozen frame state.");

            var tags = new[] { "HandL", "HandR", "FootL", "FootR" };
            for (int i = 0; i < tags.Length; i++)
            {
                var endpoint = originalTargets[i];
                var driver = result.TargetDrivers[i];
                Assert(endpoint && endpoint.parent == ik.GoalHandles[i].transform && driver && driver.parent == frame,
                    "Contact target driver or original IK endpoint has the wrong hierarchy.");
                Assert(endpoint.localPosition == originalLocalPositions[i] && Vector3.Distance(driver.position, endpoint.position) < 1e-4f,
                    "Contact setup moved the original IK endpoint or failed to initialize at its hand/foot position.");
                var solverTarget = new SerializedObject(ik.Solvers[i]).FindProperty("solver.target");
                Assert(solverTarget != null && solverTarget.objectReferenceValue == driver,
                    "IK solver was not rewired to its contact-follow driver.");

                var driverConstraint = driver.GetComponent<VRCParentConstraint>();
                Assert(driverConstraint && driverConstraint.Sources.Count == 2 && driverConstraint.Sources[0].Weight == 1f &&
                       driverConstraint.Sources[1].SourceTransform == result.Contacts[i].trackingPoint &&
                       Mathf.Approximately(driverConstraint.Sources[1].Weight, 0f),
                    "Target driver must follow its original endpoint by default and append a disabled contact source.");
                var followAnchor = driverConstraint.Sources[0].SourceTransform;
                Assert(followAnchor && followAnchor.parent == endpoint && followAnchor.localPosition == Vector3.zero,
                    "Default endpoint-follow source is not the zero-offset child of the original IK goal target.");
                var driverScale = driver.GetComponent<VRCScaleConstraint>();
                Assert(driverScale && !driverScale.FreezeToWorld,
                    "Contact target driver unexpectedly owns world-freeze state.");

                var receiverTags = result.Contacts[i].root.GetComponentsInChildren<Component>(true)
                    .Where(component => component && component.GetType().FullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver")
                    .Select(component => new SerializedObject(component).FindProperty("collisionTags").GetArrayElementAtIndex(0).stringValue);
                Assert(receiverTags.All(tag => tag == tags[i]), "A limb tracker receiver has the wrong built-in sender tag.");

                var namespaceIndex = 10 + i + 1;
                var anchorLayer = fx.layers.Single(layer => layer.name == "nxclone contact anchor " + namespaceIndex);
                var states = anchorLayer.stateMachine.states.ToDictionary(entry => entry.state.name, entry => entry.state);
                string driverPath = AnimationUtility.CalculateTransformPath(driver, avatarRoot.transform);
                var off = (AnimationClip)states["Original anchor"].motion;
                var on = (AnimationClip)states["Contact target"].motion;
                var originalWeight = EditorCurveBinding.FloatCurve(driverPath, typeof(VRCParentConstraint), "Sources.source0.Weight");
                var contactWeight = EditorCurveBinding.FloatCurve(driverPath, typeof(VRCParentConstraint), "Sources.source1.Weight");
                Assert(Mathf.Approximately(AnimationUtility.GetEditorCurve(off, originalWeight).Evaluate(0), 1f) &&
                       Mathf.Approximately(AnimationUtility.GetEditorCurve(off, contactWeight).Evaluate(0), 0f) &&
                       Mathf.Approximately(AnimationUtility.GetEditorCurve(on, originalWeight).Evaluate(0), 0f) &&
                       Mathf.Approximately(AnimationUtility.GetEditorCurve(on, contactWeight).Evaluate(0), 1f) &&
                       !AnimationUtility.GetCurveBindings(off).Any(binding => binding.propertyName.Contains("FreezeToWorld", StringComparison.Ordinal)) &&
                       !AnimationUtility.GetCurveBindings(on).Any(binding => binding.propertyName.Contains("FreezeToWorld", StringComparison.Ordinal)),
                    "Contact-off must restore endpoint follow, Contact-on must select the tracker, and contact layers must leave FreezeToWorld alone.");
            }

            Debug.Log("NXCLONE_LIMB_CONTACTS_SCHEMA_SMOKE_OK (native constraints and contact schema only; SDK contact/constraint execution is not simulated)");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rootParameters);
            UnityEngine.Object.DestroyImmediate(avatarRoot);
            if (sourceAnimator) UnityEngine.Object.DestroyImmediate(sourceAnimator.gameObject);
            if (temporaryAvatar && !AssetDatabase.Contains(temporaryAvatar)) UnityEngine.Object.DestroyImmediate(temporaryAvatar);
            AssetDatabase.DeleteAsset(folder);
            AssetDatabase.Refresh();
        }
    }

    static Animator CreateSyntheticHumanoid()
    {
        var source = new GameObject("nxclone limb contact source");
        var hips = Bone(source.transform, "Hips", new Vector3(0, 1, 0));
        var spine = Bone(hips, "Spine", new Vector3(0, 0.2f, 0));
        var chest = Bone(spine, "Chest", new Vector3(0, 0.2f, 0));
        var neck = Bone(chest, "Neck", new Vector3(0, 0.2f, 0));
        var head = Bone(neck, "Head", new Vector3(0, 0.2f, 0));
        var leftShoulder = Bone(chest, "LeftShoulder", new Vector3(-0.12f, 0.08f, 0));
        var leftArm = Bone(leftShoulder, "LeftUpperArm", new Vector3(-0.2f, 0, 0));
        var leftForearm = Bone(leftArm, "LeftLowerArm", new Vector3(-0.22f, 0, 0));
        var leftHand = Bone(leftForearm, "LeftHand", new Vector3(-0.18f, 0, 0));
        var rightShoulder = Bone(chest, "RightShoulder", new Vector3(0.12f, 0.08f, 0));
        var rightArm = Bone(rightShoulder, "RightUpperArm", new Vector3(0.2f, 0, 0));
        var rightForearm = Bone(rightArm, "RightLowerArm", new Vector3(0.22f, 0, 0));
        var rightHand = Bone(rightForearm, "RightHand", new Vector3(0.18f, 0, 0));
        var leftLeg = Bone(hips, "LeftUpperLeg", new Vector3(-0.1f, -0.15f, 0));
        var leftShin = Bone(leftLeg, "LeftLowerLeg", new Vector3(0, -0.42f, 0));
        var leftFoot = Bone(leftShin, "LeftFoot", new Vector3(0, -0.4f, 0.08f));
        var rightLeg = Bone(hips, "RightUpperLeg", new Vector3(0.1f, -0.15f, 0));
        var rightShin = Bone(rightLeg, "RightLowerLeg", new Vector3(0, -0.42f, 0));
        var rightFoot = Bone(rightShin, "RightFoot", new Vector3(0, -0.4f, 0.08f));
        var bones = new[] {
            ("Hips", hips), ("Spine", spine), ("Chest", chest), ("Neck", neck), ("Head", head),
            ("LeftShoulder", leftShoulder), ("LeftUpperArm", leftArm), ("LeftLowerArm", leftForearm), ("LeftHand", leftHand),
            ("RightShoulder", rightShoulder), ("RightUpperArm", rightArm), ("RightLowerArm", rightForearm), ("RightHand", rightHand),
            ("LeftUpperLeg", leftLeg), ("LeftLowerLeg", leftShin), ("LeftFoot", leftFoot),
            ("RightUpperLeg", rightLeg), ("RightLowerLeg", rightShin), ("RightFoot", rightFoot)
        };
        var human = bones.Select(pair => new HumanBone {
            boneName = pair.Item2.name, humanName = pair.Item1,
            limit = new HumanLimit { useDefaultValues = true }
        }).ToArray();
        var skeleton = source.GetComponentsInChildren<Transform>(true).Select(transform => new SkeletonBone {
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

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
