using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using nxclone;

[InitializeOnLoad]
public static class NxCloneDanceSmoke
{
    const string RunningKey = "nxclone.dance-smoke.running";
    const string AvatarName = "__nxclone dance smoke avatar";
    static Transform avatar, frame, reference, anchor, driver, motion, live;
    static VRCParentConstraint placement;
    static Animator animator;
    static NxClonePositionControls.Result positionDial;
    static NxCloneRotationControls.Result rotationDial;
    static Vector3 framePosition, originPosition, before;
    static Quaternion frameRotation, originRotation;
    static Vector3 anchorBeforeDial;
    static Quaternion rotationBeforeDial;
    static int stage, frames;
    static bool hiddenFollow, captured, walkLeft, noOrbit, jump, frozen, resumed, hiddenReset, recaptured, dials, postDialWalk;

    static NxCloneDanceSmoke() => EditorApplication.playModeStateChanged += OnMode;

    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run from Edit Mode");
        avatar = new GameObject(AvatarName).transform;
        avatar.SetPositionAndRotation(new Vector3(4f, 1f, -3f), Quaternion.Euler(0f, 35f, 0f));
        var group = Child(avatar, "nxclone");
        frame = Child(group, "world");
        NxClonePlacement.FreezeFrame(frame);
        framePosition = frame.position;
        frameRotation = frame.rotation;
        driver = NxClonePlacement.Dance(frame, avatar, "dance-1", new Vector3(0.2f, 0f, 1f), new Vector3(0f, 180f, 0f));
        anchor = NxClonePlacement.ControlAnchor(driver);
        reference = NxClonePlacement.DanceReference(driver);
        motion = driver.GetComponent<VRCParentConstraint>().Sources[0].SourceTransform;
        live = motion.GetComponent<VRCParentConstraint>().Sources[0].SourceTransform;
        if (!anchor || !reference || !motion || !live || !anchor.IsChildOf(reference) || !live.IsChildOf(reference))
            throw new Exception("Dance hierarchy or helper lookup is invalid.");
        if (motion.GetComponent<VRCParentConstraint>().Sources[0].SourceTransform != live ||
            !motion.GetComponent<VRCParentConstraint>().SolveInLocalSpace)
            throw new Exception("Dance motion must copy live proxy in local space.");
        if (reference.GetComponent<VRCParentConstraint>().FreezeToWorld)
            throw new Exception("Dance reference must follow avatar while hidden.");
        placement = driver.GetComponent<VRCParentConstraint>();
        originPosition = reference.position;
        originRotation = reference.rotation;

        const string folder = "Assets/nxclone-dance-smoke";
        if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-dance-smoke");
        var fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/fx.controller");
        fx.AddParameter("shown", AnimatorControllerParameterType.Bool);
        var hidden = new AnimationClip();
        var shown = new AnimationClip();
        string path = AnimationUtility.CalculateTransformPath(reference, avatar);
        AnimationUtility.SetEditorCurve(hidden, EditorCurveBinding.FloatCurve(path, typeof(VRCParentConstraint), "FreezeToWorld"), AnimationCurve.Constant(0, 1, 0));
        AnimationUtility.SetEditorCurve(shown, EditorCurveBinding.FloatCurve(path, typeof(VRCParentConstraint), "FreezeToWorld"), AnimationCurve.Constant(0, 1, 1));
        AssetDatabase.CreateAsset(hidden, folder + "/hidden.anim");
        AssetDatabase.CreateAsset(shown, folder + "/shown.anim");
        var sm = fx.layers[0].stateMachine;
        var off = sm.AddState("hidden"); off.motion = hidden; off.writeDefaultValues = false;
        var on = sm.AddState("shown"); on.motion = shown; on.writeDefaultValues = false;
        sm.defaultState = off;
        var enter = off.AddTransition(on); enter.hasExitTime = false; enter.duration = 0; enter.AddCondition(AnimatorConditionMode.If, 0, "shown");
        var leave = on.AddTransition(off); leave.hasExitTime = false; leave.duration = 0; leave.AddCondition(AnimatorConditionMode.IfNot, 0, "shown");
        positionDial = NxClonePositionControls.Configure(fx, avatar, anchor, folder, "dance-position", 1f, NxCloneAxes.X);
        rotationDial = NxCloneRotationControls.Configure(fx, avatar, driver, folder, "dance-rotation", NxCloneAxes.Y);
        animator = avatar.gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = fx;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        AssetDatabase.SaveAssets();
        SessionState.SetBool(RunningKey, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnMode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            avatar = GameObject.Find(AvatarName).transform;
            frame = avatar.Find("nxclone/world");
            driver = frame.Find("dance-1");
            reference = NxClonePlacement.DanceReference(driver);
            anchor = NxClonePlacement.ControlAnchor(driver);
            motion = driver.GetComponent<VRCParentConstraint>().Sources[0].SourceTransform;
            live = motion.GetComponent<VRCParentConstraint>().Sources[0].SourceTransform;
            placement = driver.GetComponent<VRCParentConstraint>();
            animator = avatar.GetComponent<Animator>();
            framePosition = frame.position; frameRotation = frame.rotation;
            originPosition = reference.position; originRotation = reference.rotation;
            stage = frames = 0;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.Exit(hiddenFollow && captured && walkLeft && noOrbit && jump && frozen && resumed && hiddenReset && recaptured && dials && postDialWalk ? 0 : 1);
        }
    }

    static void Tick()
    {
        try
        {
            frames++;
            if (stage == 0 && frames >= 12)
            {
                hiddenFollow = Near(reference.position, avatar.position) && NearRotation(reference.rotation, avatar.rotation) &&
                    !reference.GetComponent<VRCParentConstraint>().FreezeToWorld;
                if (!hiddenFollow) throw new Exception("Hidden dance reference did not follow avatar.");
                animator.SetBool("shown", true); stage++; frames = 0;
            }
            else if (stage == 1 && frames >= 12)
            {
                captured = reference.GetComponent<VRCParentConstraint>().FreezeToWorld && Near(reference.position, originPosition) &&
                    NearRotation(reference.rotation, originRotation) && Near(frame.position, framePosition) && NearRotation(frame.rotation, frameRotation);
                if (!captured) throw new Exception("Shown reference or graph frame did not hold captured world pose.");
                before = driver.position;
                avatar.position += avatar.rotation * Vector3.left * 1.25f;
                stage++; frames = 0;
            }
            else if (stage == 2 && frames >= 12)
            {
                Vector3 expectedOwnLeft = driver.rotation * Vector3.left * 1.25f;
                Vector3 actual = driver.position - before;
                walkLeft = Near(actual, expectedOwnLeft);
                if (!walkLeft) throw new Exception($"Avatar left step did not map to clone own left: actual={actual:F3} expected={expectedOwnLeft:F3}");
                before = driver.position;
                avatar.rotation = Quaternion.Euler(0f, 125f, 0f);
                stage++; frames = 0;
            }
            else if (stage == 3 && frames >= 12)
            {
                noOrbit = Near(driver.position, before) && Quaternion.Angle(driver.rotation, Quaternion.Euler(0f, 305f, 0f)) < 2f;
                if (!noOrbit) throw new Exception($"Root turn orbited clone or missed yaw: moved={(driver.position - before).magnitude:F3} yaw={driver.rotation.eulerAngles.y:F1}");
                before = driver.position;
                avatar.position += Vector3.up * 0.8f;
                stage++; frames = 0;
            }
            else if (stage == 4 && frames >= 12)
            {
                jump = Mathf.Abs((driver.position - before).y - 0.8f) < 0.08f;
                if (!jump) throw new Exception($"Vertical root motion not copied: delta={(driver.position - before).y:F3}");
                before = driver.position;
                placement.FreezeToWorld = true; placement.ApplyConfigurationChanges();
                avatar.position += Vector3.right * 0.6f;
                stage++; frames = 0;
            }
            else if (stage == 5 && frames >= 12)
            {
                frozen = Near(driver.position, before);
                if (!frozen) throw new Exception("Dropped driver did not hold world position.");
                placement.FreezeToWorld = false; placement.ApplyConfigurationChanges();
                avatar.position += Vector3.forward * 0.7f;
                stage++; frames = 0;
            }
            else if (stage == 6 && frames >= 12)
            {
                var driverSource = placement.Sources[0].SourceTransform;
                resumed = Near(driver.position, driverSource.position) && NearRotation(driver.rotation, driverSource.rotation);
                if (!resumed) throw new Exception("Driver did not resume follow after release.");
                animator.SetBool("shown", false);
                avatar.position += Vector3.right * 0.4f;
                stage++; frames = 0;
            }
            else if (stage == 7 && frames >= 12)
            {
                hiddenReset = !reference.GetComponent<VRCParentConstraint>().FreezeToWorld && Near(reference.position, avatar.position);
                if (!hiddenReset) throw new Exception("Hidden animation did not release reference to current avatar root.");
                originPosition = avatar.position; originRotation = avatar.rotation;
                animator.SetBool("shown", true); stage++; frames = 0;
            }
            else if (stage == 8 && frames >= 12)
            {
                recaptured = reference.GetComponent<VRCParentConstraint>().FreezeToWorld && Near(reference.position, originPosition) && NearRotation(reference.rotation, originRotation);
                if (!recaptured) throw new Exception("Re-enabled reference did not capture current root pose.");
                anchorBeforeDial = anchor.localPosition;
                rotationBeforeDial = driver.rotation;
                animator.SetFloat(positionDial.Parameters[0], 1f);
                animator.SetFloat(rotationDial.Parameters[0], 1f);
                stage++; frames = 0;
            }
            else if (stage == 9 && frames >= 12)
            {
                dials = Mathf.Abs(anchor.localPosition.x - anchorBeforeDial.x - 1f) < 0.08f &&
                    Mathf.Abs(Quaternion.Angle(driver.rotation, rotationBeforeDial) - 180f) < 2f;
                if (!dials) throw new Exception($"Position/rotation dials failed: anchorX={anchor.localPosition.x:F3} startX={anchorBeforeDial.x:F3} yawDelta={Quaternion.Angle(driver.rotation, rotationBeforeDial):F1}");
                before = driver.position;
                avatar.position += avatar.rotation * Vector3.left * 0.5f;
                stage++; frames = 0;
            }
            else if (stage == 10 && frames >= 12)
            {
                postDialWalk = Near(driver.position - before, driver.rotation * Vector3.left * 0.5f);
                if (!postDialWalk) throw new Exception($"Dance left mapping changed after recapture/dials: delta={driver.position - before:F3}");
                Debug.Log($"NXCLONE_DANCE_PLAYMODE_DONE hiddenFollow={hiddenFollow} capture={captured} ownLeft={walkLeft} noOrbit={noOrbit} jump={jump} dropFreeze={frozen} resume={resumed} hiddenReset={hiddenReset} recapture={recaptured} dials={dials} postDialWalk={postDialWalk} driver={driver.position:F3} reference={reference.position:F3}");
                EditorApplication.update -= Tick;
                UnityEngine.Object.Destroy(avatar.gameObject);
                EditorApplication.ExitPlaymode();
            }
        }
        catch (Exception e)
        {
            EditorApplication.update -= Tick;
            Debug.LogException(e);
            SessionState.SetBool(RunningKey, false);
            EditorApplication.Exit(1);
        }
    }

    static Transform Child(Transform parent, string path)
    {
        var child = new GameObject(path.Substring(path.LastIndexOf('/') + 1)).transform;
        child.SetParent(parent, false);
        return child;
    }
    static bool Near(Vector3 a, Vector3 b) => (a - b).magnitude < 0.08f;
    static bool NearRotation(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) < 1f;
}
