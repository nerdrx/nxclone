using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nxclone;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

public static class NxClonePosingSmoke
{
    const string RunKey = "nxclone.posing.playmode.run";
    const string PhaseKey = "nxclone.posing.playmode.phase";
    const string SourceKey = "nxclone posing smoke source";
    const string SourceAnimatorEnabledKey = "nxclone.posing.playmode.sourceanim.enabled";
    const string ErrorKey = "nxclone.posing.playmode.error";
    static int playFrames;
    static Quaternion sourceBase;
    static Quaternion chestBase;
    static Quaternion clonePose;
    const string CloneKey = "nxclone posing smoke clone";
    const string RootKey = "nxclone posing smoke animator root";

    [InitializeOnLoadMethod]
    static void InstallPlayModeRunner()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static void Run()
    {
        var sourceAnimator = CreateSyntheticHumanoid();
        var sourceRoot = sourceAnimator.transform;
        int before = sourceRoot.GetComponentsInChildren<Component>(true).Length;
        var testRoot = new GameObject(RootKey);
        var clone = UnityEngine.Object.Instantiate(sourceRoot.gameObject, testRoot.transform);
        clone.name = CloneKey;
        foreach (var component in clone.GetComponentsInChildren<Component>(true))
            if (component && !(component is Transform)) UnityEngine.Object.DestroyImmediate(component);

        const string folder = "Assets/nxclone-posing-smoke";
        bool keepForPlayMode = false;
        if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-posing-smoke");
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone", false))
                .FirstOrDefault(value => value != null);
            if (type == null) throw new Exception("VRChat SDK VRCPhysBone type missing from smoke project.");

            foreach (var (rootBone, middleBone, endBone) in new[] {
                (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
                (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot) })
            {
                var sourceTop = sourceAnimator.GetBoneTransform(rootBone);
                var sourceMid = sourceAnimator.GetBoneTransform(middleBone);
                var sourceEnd = sourceAnimator.GetBoneTransform(endBone);
                var top = clone.transform.Find(AnimationUtility.CalculateTransformPath(sourceTop, sourceRoot));
                var mid = clone.transform.Find(AnimationUtility.CalculateTransformPath(sourceMid, sourceRoot));
                var end = clone.transform.Find(AnimationUtility.CalculateTransformPath(sourceEnd, sourceRoot));
                foreach (var bone in PathNodes(top, mid).Concat(PathNodes(mid, end).Skip(1)))
                {
                    var sourceBone = sourceRoot.Find(AnimationUtility.CalculateTransformPath(bone, clone.transform));
                    var rotation = bone.gameObject.AddComponent<VRCRotationConstraint>();
                    rotation.Sources.Add(new VRCConstraintSource(sourceBone, 1f));
                    rotation.SolveInLocalSpace = true;
                    rotation.ActivateConstraint();
                    rotation.ApplyConfigurationChanges();
                    SetConstraintActive(rotation, true);
                    var position = bone.gameObject.AddComponent<VRCPositionConstraint>();
                    position.Sources.Add(new VRCConstraintSource(sourceBone, 1f));
                    position.SolveInLocalSpace = true;
                    position.ActivateConstraint();
                    position.ApplyConfigurationChanges();
                    SetConstraintActive(position, true);
                }
            }

            var sourceChest = sourceAnimator.GetBoneTransform(HumanBodyBones.Chest);
            var cloneChest = clone.transform.Find(AnimationUtility.CalculateTransformPath(sourceChest, sourceRoot));
            var chestRotation = cloneChest.gameObject.AddComponent<VRCRotationConstraint>();
            chestRotation.Sources.Add(new VRCConstraintSource(sourceChest, 1f));
            chestRotation.SolveInLocalSpace = true;
            chestRotation.ActivateConstraint();
            chestRotation.ApplyConfigurationChanges();
            SetConstraintActive(chestRotation, true);
            var chestPosition = cloneChest.gameObject.AddComponent<VRCPositionConstraint>();
            chestPosition.Sources.Add(new VRCConstraintSource(sourceChest, 1f));
            chestPosition.SolveInLocalSpace = true;
            chestPosition.ActivateConstraint();
            chestPosition.ApplyConfigurationChanges();
            SetConstraintActive(chestPosition, true);

            var result = NxClonePosing.Configure(sourceAnimator, sourceRoot, clone.transform, folder, 1, "nxclone_pose_1", testRoot.transform);
            if (result.PhysBoneCount != 4 || result.ConstraintHostCount != 4)
                throw new Exception("Expected one native PhysBone and one SDK constraint host per arm/leg chain.");
            var bones = clone.GetComponentsInChildren(type, true);
            if (bones.Length != 4) throw new Exception("Expected exactly four native PhysBones on cloned upper limbs.");
            var rotationConstraints = clone.GetComponentsInChildren<VRCRotationConstraint>(true);
            var positionConstraints = clone.GetComponentsInChildren<VRCPositionConstraint>(true);
            if (rotationConstraints.Length != 17 || positionConstraints.Length != 17 ||
                rotationConstraints.Any(component => component.TargetTransform && !component.TargetTransform.IsChildOf(clone.transform)) ||
                positionConstraints.Any(component => component.TargetTransform && !component.TargetTransform.IsChildOf(clone.transform)))
                throw new Exception("Moved constraint count/targets invalid: rot=" + rotationConstraints.Length + ", pos=" + positionConstraints.Length + ", nullRot=" + rotationConstraints.Count(component => !component.TargetTransform) + ", nullPos=" + positionConstraints.Count(component => !component.TargetTransform) + ", outsideRot=" + string.Join(",", rotationConstraints.Where(component => component.TargetTransform && !component.TargetTransform.IsChildOf(clone.transform)).Select(component => component.TargetTransform.name)) + ", outsidePos=" + string.Join(",", positionConstraints.Where(component => component.TargetTransform && !component.TargetTransform.IsChildOf(clone.transform)).Select(component => component.TargetTransform.name)));
            var limbBones = new[] {
                (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
                (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot)
            }.SelectMany(chain => {
                var top = clone.transform.Find(AnimationUtility.CalculateTransformPath(sourceAnimator.GetBoneTransform(chain.Item1), sourceRoot));
                var middle = clone.transform.Find(AnimationUtility.CalculateTransformPath(sourceAnimator.GetBoneTransform(chain.Item2), sourceRoot));
                var end = clone.transform.Find(AnimationUtility.CalculateTransformPath(sourceAnimator.GetBoneTransform(chain.Item3), sourceRoot));
                return PathNodes(top, middle).Concat(PathNodes(middle, end).Skip(1));
            }).ToArray();
            if (limbBones.Any(bone => bone.GetComponent<VRCRotationConstraint>() || bone.GetComponent<VRCPositionConstraint>()))
                throw new Exception("Limb constraints must be owned by switchable native holders, not bone objects.");
            var freeze = NxClonePosing.ConfigureFreeze(clone.transform, testRoot.transform, folder, 1);
            if (freeze.ConstraintCount != 2 || !freeze.ControlHost || !freeze.ControlHost.activeSelf)
                throw new Exception("Whole-body freeze must move remaining skeletal constraints to an active native host.");
            rotationConstraints = clone.GetComponentsInChildren<VRCRotationConstraint>(true);
            positionConstraints = clone.GetComponentsInChildren<VRCPositionConstraint>(true);
            if (rotationConstraints.Any(component => !component.TargetTransform || !component.TargetTransform.IsChildOf(clone.transform)) ||
                positionConstraints.Any(component => !component.TargetTransform || !component.TargetTransform.IsChildOf(clone.transform)) ||
                rotationConstraints.Select(component => AnimationUtility.CalculateTransformPath(component.transform, testRoot.transform)).Distinct().Count() != rotationConstraints.Length)
                throw new Exception("Moved native constraints must all keep clone-local targets and unique same-type animation paths.");
            foreach (var component in bones)
            {
                var serialized = new SerializedObject(component);
                var root = serialized.FindProperty("rootTransform");
                var ignore = serialized.FindProperty("ignoreTransforms");
                var grabbing = serialized.FindProperty("allowGrabbing");
                var posing = serialized.FindProperty("allowPosing");
                if (root == null || !(root.objectReferenceValue is Transform mappedRoot) || !mappedRoot.IsChildOf(clone.transform) ||
                    ignore == null || !ignore.isArray)
                    throw new Exception("PhysBone is missing its mapped root or isolated limb branch list.");
                if (grabbing == null || grabbing.enumDisplayNames[grabbing.enumValueIndex] != "True" ||
                    posing == null || posing.enumDisplayNames[posing.enumValueIndex] != "True")
                    throw new Exception("PhysBone grab/pose must be enabled using the installed SDK's explicit True enum value.");
                if (serialized.FindProperty("grabFilter.allowSelf").boolValue != true ||
                    serialized.FindProperty("grabFilter.allowOthers").boolValue != true ||
                    serialized.FindProperty("poseFilter.allowSelf").boolValue != true ||
                    serialized.FindProperty("poseFilter.allowOthers").boolValue != true)
                    throw new Exception("PhysBone permissions must allow self and other users.");
                if (component.gameObject.activeSelf) throw new Exception("Posing PhysBone holder objects must start inactive.");
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/pose.controller");
            controller.AddParameter("nxclone_pose_1", AnimatorControllerParameterType.Bool);
            controller.AddParameter("nxclone_enabled_1", AnimatorControllerParameterType.Bool);
            controller.AddParameter("nxclone_freeze_1", AnimatorControllerParameterType.Bool);
            NxClonePosing.AddFreezeLayer(controller, freeze, "nxclone_freeze_1", false);
            NxClonePosing.AddLayer(controller, result, "nxclone_freeze_1", false, "nxclone_enabled_1");
            // Add the evaluating Animator only after all constraints, clips, and controller
            // bindings exist so Unity's initial binding cache includes the native SDK fields.
            var animator = testRoot.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.SetLayerWeight(controller.layers.Length - 1, 1f);
            animator.Update(0.1f);
            animator.SetBool("nxclone_enabled_1", true);
            animator.Update(0.1f); animator.Update(0.1f);
            if (!animator.GetCurrentAnimatorStateInfo(controller.layers.Length - 1).IsName("Live"))
                throw new Exception("Visible, unposed clone did not enter the live state.");
            result.Live.SampleAnimation(testRoot, 0.01f);
            freeze.Live.SampleAnimation(testRoot, 0.01f);
            if (ConstraintHosts(clone).Any(host => !host.activeSelf))
                throw new Exception("Live state failed to enable native VRChat constraint holders.");
            animator.SetBool("nxclone_pose_1", true);
            animator.Update(0.1f); animator.Update(0.1f);
            if (!animator.GetCurrentAnimatorStateInfo(controller.layers.Length - 1).IsName("Posing"))
                throw new Exception("Posing bool did not transition the FX layer to Posing.");
            result.PoseOn.SampleAnimation(testRoot, 0.01f);
            if (bones.Any(component => !component.gameObject.activeSelf) ||
                PosingConstraintHosts(clone).Any(host => host.activeSelf) || WholeFreezeHosts(clone).Any(host => !host.activeSelf))
            {
                var info = animator.GetCurrentAnimatorStateInfo(controller.layers.Length - 1);
                throw new Exception("Posing state failed: state=" + info.shortNameHash + "/pose=" + info.IsName("Posing") + "/live=" + info.IsName("Live") +
                    "/hidden=" + info.IsName("Hidden") + "/frozen=" + info.IsName("Frozen") + "/layerCount=" + animator.layerCount +
                    " pose=" + animator.GetBool("nxclone_pose_1") + " visible=" + animator.GetBool("nxclone_enabled_1") +
                    " pbActive=" + string.Join(",", bones.Select(component => component.gameObject.activeSelf)) +
                    " bindings=" + string.Join(";", AnimationUtility.GetCurveBindings(result.PoseOn).Where(binding => binding.type == typeof(GameObject)).Select(binding => binding.path + "/" + binding.propertyName + ":" + AnimationUtility.GetEditorCurve(result.PoseOn, binding).Evaluate(0.01f))) +
                    " actual=" + string.Join(";", bones.Select(component => AnimationUtility.CalculateTransformPath(component.transform, sourceRoot))) +
                    " constraintHosts=" + string.Join(",", ConstraintHosts(clone).Select(host => host.activeSelf)));
            }
            animator.SetBool("nxclone_pose_1", false);
            animator.SetBool("nxclone_freeze_1", true);
            animator.Update(0.1f); animator.Update(0.1f);
            if (!animator.GetCurrentAnimatorStateInfo(controller.layers.Length - 1).IsName("Frozen"))
                throw new Exception("Freeze bool did not transition the FX layer to Frozen.");
            result.Frozen.SampleAnimation(testRoot, 0.01f);
            freeze.Frozen.SampleAnimation(testRoot, 0.01f);
            if (bones.Any(component => component.gameObject.activeSelf) ||
                PosingConstraintHosts(clone).Any(host => host.activeSelf) || WholeFreezeHosts(clone).Any(host => host.activeSelf))
                throw new Exception("Frozen state must keep PhysBones and limb constraints disabled.");
            animator.SetBool("nxclone_freeze_1", false);
            animator.Update(0.1f); animator.Update(0.1f);
            if (!animator.GetCurrentAnimatorStateInfo(controller.layers.Length - 1).IsName("Live"))
                throw new Exception("Clearing freeze did not restore the live FX state.");
            result.Live.SampleAnimation(testRoot, 0.01f);
            freeze.Live.SampleAnimation(testRoot, 0.01f);
            if (bones.Any(component => component.gameObject.activeSelf) ||
                ConstraintHosts(clone).Any(host => !host.activeSelf))
                throw new Exception("Live state must restore source-follow constraints and disable PhysBones.");
            var layer = controller.layers.Last();
            if (layer.defaultWeight != 1f || layer.stateMachine.states.Length != 4)
                throw new Exception("Poser layer weight=" + layer.defaultWeight + " states=" + layer.stateMachine.states.Length + " names=" +
                    string.Join(",", layer.stateMachine.states.Select(item => item.state.name)) + ".");
            var states = layer.stateMachine.states.Select(item => item.state).ToArray();
            if (!states.Any(state => state.motion == result.PoseOn) || !states.Any(state => state.motion == result.Live) ||
                !states.Any(state => state.motion == result.Frozen) || !states.Any(state => state.motion == result.Hidden))
                throw new Exception("Poser layer did not bind all four ownership clips.");
            var poseBindings = AnimationUtility.GetCurveBindings(result.PoseOn);
            var liveBindings = AnimationUtility.GetCurveBindings(result.Live);
            var frozenBindings = AnimationUtility.GetCurveBindings(result.Frozen);
            if (!poseBindings.Any(binding => binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") ||
                !liveBindings.Any(binding => binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") ||
                !frozenBindings.Any(binding => binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive"))
                throw new Exception("Poser clips must switch native PhysBones on/off.");

            // Play Mode checks the actual native VRChat constraint solver, not only clip sampling.
            // Keep this isolated clone scene alive across the mode transition; the async runner
            // cleans it and its generated assets after the Play Mode assertions complete.
            if (sourceRoot.GetComponentsInChildren<Component>(true).Length != before)
                throw new Exception("Posing configuration changed the source avatar.");
            SessionState.SetBool(RunKey, true);
            SessionState.SetInt(PhaseKey, 0);
            SessionState.SetString(SourceKey, SourceKey);
            SessionState.SetBool(SourceAnimatorEnabledKey, sourceAnimator.enabled);
            sourceAnimator.enabled = false;
            keepForPlayMode = true;
            EditorApplication.EnterPlaymode();
            clone = null;
            testRoot = null;
            Debug.Log("NXCLONE_NATIVE_POSING_EDITMODE_OK: generated native PhysBones, controller clips, and SDK ownership fields; starting runtime solver checks.");
        }
        finally
        {
            if (!keepForPlayMode)
            {
                if (clone) UnityEngine.Object.DestroyImmediate(clone);
                if (testRoot) UnityEngine.Object.DestroyImmediate(testRoot);
                var temporaryAvatar = sourceAnimator ? sourceAnimator.avatar : null;
                if (sourceRoot) UnityEngine.Object.DestroyImmediate(sourceRoot.gameObject);
                if (temporaryAvatar && !AssetDatabase.Contains(temporaryAvatar)) UnityEngine.Object.DestroyImmediate(temporaryAvatar);
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            playFrames = 0;
            EditorApplication.update -= PlayModeTick;
            EditorApplication.update += PlayModeTick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= PlayModeTick;
            FinishPlayModeRun();
        }
    }

    static void PlayModeTick()
    {
        if (!Application.isPlaying) return;
        try
        {
            var sourceRoot = GameObject.Find(SessionState.GetString(SourceKey, ""));
            if (!sourceRoot) throw new Exception("Source avatar disappeared on Play Mode transition.");
            var sourceAnimator = sourceRoot.GetComponent<Animator>();
            var sourceMid = sourceAnimator ? sourceAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm) : null;
            var sourceChest = sourceAnimator ? sourceAnimator.GetBoneTransform(HumanBodyBones.Chest) : null;
            var avatarRoot = GameObject.Find(RootKey);
            var clone = avatarRoot ? avatarRoot.transform.Find(CloneKey) : null;
            if (!sourceMid || !sourceChest || !clone) throw new Exception("Play Mode fixture source or clone arm/chest is missing.");
            var cloneMid = clone.Find(AnimationUtility.CalculateTransformPath(sourceMid, sourceRoot.transform));
            var cloneChest = clone.Find(AnimationUtility.CalculateTransformPath(sourceChest, sourceRoot.transform));
            if (!cloneMid || !cloneChest) throw new Exception("Clone lower-arm/chest path failed to map in Play Mode.");
            var animator = avatarRoot.GetComponent<Animator>();
            if (!animator || !animator.runtimeAnimatorController) throw new Exception("Play Mode FX animator/controller missing.");
            var layer = animator.runtimeAnimatorController is AnimatorController controller ? controller.layers.Length - 1 : -1;
            if (layer < 0) throw new Exception("Play Mode controller is not an AnimatorController.");
            int phase = SessionState.GetInt(PhaseKey, 0);
            if (playFrames++ < 4) return;
            playFrames = 0;
            var armConstraint = clone.GetComponentsInChildren<VRCRotationConstraint>(true)
                .FirstOrDefault(component => component && component.TargetTransform == cloneMid);
            if (!armConstraint) throw new Exception("Clone lower-arm native rotation constraint is missing.");
            var chestConstraint = clone.GetComponentsInChildren<VRCRotationConstraint>(true)
                .FirstOrDefault(component => component && component.TargetTransform == cloneChest);
            if (!chestConstraint) throw new Exception("Clone chest native rotation constraint is missing.");

            switch (phase)
            {
                case 0:
                    animator.SetBool("nxclone_enabled_1", true);
                    animator.SetBool("nxclone_pose_1", false);
                    animator.SetBool("nxclone_freeze_1", false);
                    sourceBase = sourceMid.localRotation;
                    chestBase = sourceChest.localRotation;
                    sourceMid.localRotation = sourceBase * Quaternion.Euler(0, 0, 35);
                    sourceChest.localRotation = chestBase * Quaternion.Euler(0, 0, 20);
                    SessionState.SetInt(PhaseKey, 1);
                    return;
                case 1:
                    if (!animator.GetCurrentAnimatorStateInfo(layer).IsName("Live")) throw new Exception("Actual Play Mode FX animator did not enter Live.");
                    if (!armConstraint.gameObject.activeInHierarchy) throw new Exception("SDK arm constraint host is inactive in Live.");
                    if (Quaternion.Angle(cloneMid.localRotation, sourceMid.localRotation) > 5f)
                        throw new Exception("Live SDK constraint failed to move clone with the source arm; angle=" + Quaternion.Angle(cloneMid.localRotation, sourceMid.localRotation));
                    if (!chestConstraint.gameObject.activeInHierarchy || Quaternion.Angle(cloneChest.localRotation, sourceChest.localRotation) > 5f)
                        throw new Exception("Whole-body live SDK constraint failed to move clone chest with source.");
                    animator.SetBool("nxclone_pose_1", true);
                    SessionState.SetInt(PhaseKey, 2);
                    return;
                case 2:
                    if (!animator.GetCurrentAnimatorStateInfo(layer).IsName("Posing")) throw new Exception("Actual Play Mode FX animator did not enter Posing.");
                    if (armConstraint.gameObject.activeInHierarchy) throw new Exception("SDK arm constraint host remained active in Posing.");
                    if (!clone.GetComponentsInChildren<Component>(true).Any(component => component && component.GetType().FullName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone" && component.gameObject.activeInHierarchy))
                        throw new Exception("Native PhysBone holder did not activate in Posing.");
                    // The editor cannot synthesize a VRChat hand grab. Disable only the four
                    // active PhysBone solvers after asserting activation, so this measures
                    // constraint ownership and does not mistake ungrabbed physics drift for a grab.
                    foreach (var physBone in clone.GetComponentsInChildren<Component>(true)
                        .Where(component => component && component.GetType().FullName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone"))
                        ((Behaviour)physBone).enabled = false;
                    clonePose = sourceBase * Quaternion.Euler(0, 0, -55);
                    cloneMid.localRotation = clonePose;
                    sourceMid.localRotation = sourceBase * Quaternion.Euler(0, 0, 40);
                    sourceChest.localRotation = chestBase * Quaternion.Euler(0, 0, 25);
                    SessionState.SetInt(PhaseKey, 3);
                    return;
                case 3:
                    if (Quaternion.Angle(cloneMid.localRotation, clonePose) > 8f)
                        throw new Exception("Clone continued following the moving source while posed; angle from posed rotation=" + Quaternion.Angle(cloneMid.localRotation, clonePose));
                    if (!chestConstraint.gameObject.activeInHierarchy || Quaternion.Angle(cloneChest.localRotation, sourceChest.localRotation) > 5f)
                        throw new Exception("Chest should continue following source while only limb posing is enabled.");
                    animator.SetBool("nxclone_pose_1", false);
                    animator.SetBool("nxclone_freeze_1", true);
                    SessionState.SetInt(PhaseKey, 4);
                    return;
                case 4:
                    if (!animator.GetCurrentAnimatorStateInfo(layer).IsName("Frozen")) throw new Exception("Actual Play Mode FX animator did not enter Frozen.");
                    if (armConstraint.gameObject.activeInHierarchy) throw new Exception("SDK arm constraint host remained active in Frozen.");
                    var frozen = cloneMid.localRotation;
                    if (chestConstraint.gameObject.activeInHierarchy) throw new Exception("Whole-body freeze left chest constraint host active.");
                    var frozenChest = cloneChest.localRotation;
                    sourceMid.localRotation = sourceBase * Quaternion.Euler(0, 0, 65);
                    sourceChest.localRotation = chestBase * Quaternion.Euler(0, 0, 65);
                    SessionState.SetInt(PhaseKey, 5);
                    SessionState.SetString("nxclone.posing.frozen", Pack(frozen) + ";" + Pack(frozenChest));
                    return;
                case 5:
                    var frozenParts = SessionState.GetString("nxclone.posing.frozen", "").Split(';');
                    var frozenPose = Unpack(frozenParts[0]);
                    var frozenChestPose = Unpack(frozenParts[1]);
                    if (Quaternion.Angle(cloneMid.localRotation, frozenPose) > 8f)
                        throw new Exception("Clone continued following source while frozen; angle=" + Quaternion.Angle(cloneMid.localRotation, frozenPose));
                    if (Quaternion.Angle(cloneChest.localRotation, frozenChestPose) > 8f)
                        throw new Exception("Clone chest continued following source during whole-body freeze.");
                    animator.SetBool("nxclone_freeze_1", false);
                    SessionState.SetInt(PhaseKey, 6);
                    return;
                case 6:
                    if (!animator.GetCurrentAnimatorStateInfo(layer).IsName("Live")) throw new Exception("Unfreeze did not restore Live in Play Mode.");
                    if (!armConstraint.gameObject.activeInHierarchy) throw new Exception("SDK arm constraint host did not restore in Live.");
                    sourceMid.localRotation = sourceBase * Quaternion.Euler(0, 0, -25);
                    sourceChest.localRotation = chestBase * Quaternion.Euler(0, 0, -20);
                    SessionState.SetInt(PhaseKey, 7);
                    return;
                case 7:
                    if (Quaternion.Angle(cloneMid.localRotation, sourceMid.localRotation) > 5f)
                        throw new Exception("Clone failed to resume following source after unfreeze; angle=" + Quaternion.Angle(cloneMid.localRotation, sourceMid.localRotation));
                    if (!chestConstraint.gameObject.activeInHierarchy || Quaternion.Angle(cloneChest.localRotation, sourceChest.localRotation) > 5f)
                        throw new Exception("Whole-body unfreeze did not restore source-follow on chest.");
                    Debug.Log("NXCLONE_NATIVE_POSING_PLAYMODE_OK: limb SDK constraints transfer ownership in Posing; whole-body native freeze holds and resumes the chest pose.");
                    SessionState.SetInt(PhaseKey, 8);
                    EditorApplication.ExitPlaymode();
                    return;
            }
        }
        catch (Exception exception)
        {
            SessionState.SetString(ErrorKey, exception.ToString());
            Debug.LogError("NXCLONE_NATIVE_POSING_PLAYMODE_FAILED: " + exception);
            EditorApplication.ExitPlaymode();
        }
    }

    static void FinishPlayModeRun()
    {
        if (!SessionState.GetBool(RunKey, false)) return;
        EditorApplication.update -= PlayModeTick;
        var source = GameObject.Find(SessionState.GetString(SourceKey, ""));
        var sourceAnimator = source ? source.GetComponent<Animator>() : null;
        var temporaryAvatar = sourceAnimator ? sourceAnimator.avatar : null;
        if (sourceAnimator) sourceAnimator.enabled = SessionState.GetBool(SourceAnimatorEnabledKey, true);
        var root = GameObject.Find(RootKey);
        if (root) UnityEngine.Object.DestroyImmediate(root);
        if (source) UnityEngine.Object.DestroyImmediate(source);
        if (temporaryAvatar && !AssetDatabase.Contains(temporaryAvatar)) UnityEngine.Object.DestroyImmediate(temporaryAvatar);
        AssetDatabase.DeleteAsset("Assets/nxclone-posing-smoke");
        AssetDatabase.Refresh();
        var error = SessionState.GetString(ErrorKey, "");
        var success = SessionState.GetInt(PhaseKey, 0) == 8 && string.IsNullOrEmpty(error);
        SessionState.SetBool(RunKey, false);
        SessionState.EraseString(ErrorKey);
        SessionState.EraseString(SourceKey);
        SessionState.EraseString("nxclone.posing.frozen");
        Debug.Log(success ? "NXCLONE_NATIVE_POSING_SMOKE_OK" : "NXCLONE_NATIVE_POSING_SMOKE_FAILED: " + (error.Length == 0 ? "runner ended before assertions completed" : error));
        EditorApplication.Exit(success ? 0 : 1);
    }

    static Animator CreateSyntheticHumanoid()
    {
        var source = new GameObject(SourceKey);
        var hips = Bone(source.transform, "Hips", new Vector3(0, 1f, 0));
        var spine = Bone(hips, "Spine", new Vector3(0, 0.2f, 0));
        var chest = Bone(spine, "Chest", new Vector3(0, 0.2f, 0));
        var neck = Bone(chest, "Neck", new Vector3(0, 0.2f, 0));
        var head = Bone(neck, "Head", new Vector3(0, 0.2f, 0));
        var leftShoulder = Bone(chest, "LeftShoulder", new Vector3(-0.12f, 0.08f, 0));
        var leftUpperArm = Bone(leftShoulder, "LeftUpperArm", new Vector3(-0.2f, 0, 0));
        var leftForearmTwist = Bone(leftUpperArm, "LeftForearmTwist", new Vector3(-0.11f, 0, 0));
        var leftLowerArm = Bone(leftForearmTwist, "LeftLowerArm", new Vector3(-0.11f, 0, 0));
        var leftHand = Bone(leftLowerArm, "LeftHand", new Vector3(-0.18f, 0, 0));
        var rightShoulder = Bone(chest, "RightShoulder", new Vector3(0.12f, 0.08f, 0));
        var rightUpperArm = Bone(rightShoulder, "RightUpperArm", new Vector3(0.2f, 0, 0));
        var rightForearmTwist = Bone(rightUpperArm, "RightForearmTwist", new Vector3(0.11f, 0, 0));
        var rightLowerArm = Bone(rightForearmTwist, "RightLowerArm", new Vector3(0.11f, 0, 0));
        var rightHand = Bone(rightLowerArm, "RightHand", new Vector3(0.18f, 0, 0));
        var leftUpperLeg = Bone(hips, "LeftUpperLeg", new Vector3(-0.1f, -0.15f, 0));
        var leftThighTwist = Bone(leftUpperLeg, "LeftThighTwist", new Vector3(0, -0.2f, 0));
        var leftLowerLeg = Bone(leftThighTwist, "LeftLowerLeg", new Vector3(0, -0.22f, 0));
        var leftFoot = Bone(leftLowerLeg, "LeftFoot", new Vector3(0, -0.4f, 0.08f));
        var leftToes = Bone(leftFoot, "LeftToes", new Vector3(0, 0, 0.1f));
        var rightUpperLeg = Bone(hips, "RightUpperLeg", new Vector3(0.1f, -0.15f, 0));
        var rightThighTwist = Bone(rightUpperLeg, "RightThighTwist", new Vector3(0, -0.2f, 0));
        var rightLowerLeg = Bone(rightThighTwist, "RightLowerLeg", new Vector3(0, -0.22f, 0));
        var rightFoot = Bone(rightLowerLeg, "RightFoot", new Vector3(0, -0.4f, 0.08f));
        var rightToes = Bone(rightFoot, "RightToes", new Vector3(0, 0, 0.1f));
        var map = new[] {
            ("Hips", hips), ("Spine", spine), ("Chest", chest), ("Neck", neck), ("Head", head),
            ("LeftShoulder", leftShoulder), ("LeftUpperArm", leftUpperArm), ("LeftLowerArm", leftLowerArm), ("LeftHand", leftHand),
            ("RightShoulder", rightShoulder), ("RightUpperArm", rightUpperArm), ("RightLowerArm", rightLowerArm), ("RightHand", rightHand),
            ("LeftUpperLeg", leftUpperLeg), ("LeftLowerLeg", leftLowerLeg), ("LeftFoot", leftFoot), ("LeftToes", leftToes),
            ("RightUpperLeg", rightUpperLeg), ("RightLowerLeg", rightLowerLeg), ("RightFoot", rightFoot), ("RightToes", rightToes)
        };
        var human = map.Select(pair => new HumanBone {
            boneName = pair.Item2.name,
            humanName = pair.Item1,
            limit = new HumanLimit { useDefaultValues = true }
        }).ToArray();
        var skeleton = source.GetComponentsInChildren<Transform>(true).Select(transform => new SkeletonBone {
            name = transform.name,
            position = transform.localPosition,
            rotation = transform.localRotation,
            scale = transform.localScale
        }).ToArray();
        var description = new HumanDescription {
            human = human,
            skeleton = skeleton,
            armStretch = 0.05f,
            legStretch = 0.05f,
            upperArmTwist = 0.5f,
            lowerArmTwist = 0.5f,
            upperLegTwist = 0.5f,
            lowerLegTwist = 0.5f,
            feetSpacing = 0,
            hasTranslationDoF = false
        };
        var avatar = AvatarBuilder.BuildHumanAvatar(source, description);
        if (!avatar || !avatar.isValid || !avatar.isHuman)
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            throw new Exception("Unity failed to construct the synthetic humanoid rig for native poser tests.");
        }
        var animator = source.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return animator;
    }

    static Transform Bone(Transform parent, string name, Vector3 localPosition)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = localPosition;
        return child;
    }

    static Transform[] PathNodes(Transform from, Transform to)
    {
        var path = new System.Collections.Generic.List<Transform> { to };
        for (var current = to; current && current != from; current = current.parent)
            if (current.parent && current.parent != from) path.Add(current.parent);
        path.Add(from); path.Reverse(); return path.ToArray();
    }

    static GameObject[] ConstraintHosts(GameObject clone) => clone.GetComponentsInChildren<VRCRotationConstraint>(true).Cast<Component>()
        .Concat(clone.GetComponentsInChildren<VRCPositionConstraint>(true).Cast<Component>())
        .Select(component => FindConstraintOwner(component.transform, clone.transform))
        .Where(host => host)
        .Distinct().ToArray();

    static GameObject FindConstraintOwner(Transform item, Transform root)
    {
        for (var current = item; current && current != root; current = current.parent)
            if (current.name.StartsWith("nxclone constraints ", StringComparison.Ordinal) ||
                current.name.StartsWith("nxclone freeze constraints ", StringComparison.Ordinal)) return current.gameObject;
        return null;
    }

    static GameObject[] PosingConstraintHosts(GameObject clone) => ConstraintHosts(clone)
        .Where(host => host.name.StartsWith("nxclone constraints ", StringComparison.Ordinal)).ToArray();

    static GameObject[] WholeFreezeHosts(GameObject clone) => ConstraintHosts(clone)
        .Where(host => host.name.StartsWith("nxclone freeze constraints ", StringComparison.Ordinal)).ToArray();

    static string Pack(Quaternion value) => value.x + "," + value.y + "," + value.z + "," + value.w;
    static Quaternion Unpack(string value)
    {
        var parts = value.Split(',').Select(float.Parse).ToArray();
        return new Quaternion(parts[0], parts[1], parts[2], parts[3]);
    }

    static void SetConstraintActive(Component component, bool value)
    {
        var data = new SerializedObject(component);
        var property = data.FindProperty("IsActive");
        if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            throw new Exception(component.GetType().Name + " is missing the SDK IsActive ownership field.");
        property.boolValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
