using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using nxclone;

[InitializeOnLoad]
public static class NxClonePlacementSmoke
{
    const string RunningKey = "nxclone.placement-smoke.running";
    static int stage;
    static float holdStarted;
    static Transform avatar, anchor, frame, driver, rootDriver, visual, trail, trailFrame;
    static VRCParentConstraint placement;
    static Animator animator;
    static Vector3 beforeFreeze, beforeFreezeScale;
    static Quaternion beforeFreezeRotation;
    static float trailPrevious;
    static bool offsetOk, rotationOffsetOk, rootOffsetOk, hiddenOk, frameOk, frameRotOk, scaleOk, freezeOk, unfreezeOk, trailLag, trailProgress = true;

    static NxClonePlacementSmoke() { EditorApplication.playModeStateChanged += OnMode; }

    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new Exception("Run this check from Edit Mode");
        avatar = new GameObject("nxclone probe avatar").transform;
        avatar.position = new Vector3(5, 0, 0);
        avatar.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        anchor = new GameObject("probe hand").transform;
        anchor.SetParent(avatar, false); anchor.localPosition = new Vector3(0, 1, 0);
        var group = new GameObject("nxclone").transform;
        group.SetParent(avatar, false);
        frame = new GameObject("world").transform;
        frame.SetParent(group, false);
        NxClonePlacement.FreezeFrame(frame);
        driver = NxClonePlacement.Follow(frame, anchor, "placement-1", Vector3.right * 2, new Vector3(0, 25, 0));
        rootDriver = NxClonePlacement.Follow(frame, avatar, "placement-root", Vector3.forward, Vector3.zero);
        placement = driver.GetComponent<VRCParentConstraint>();
        visual = new GameObject("clone-1").transform; visual.SetParent(driver, false); visual.gameObject.SetActive(false);
        trailFrame = frame;
        trail = NxClonePlacement.Delay(frame, avatar, "trail-1", 0.25f);
        const string folder = "Assets/nxclone-placement-smoke";
        if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-placement-smoke");
        var fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/fx.controller");
        fx.AddParameter("drop", AnimatorControllerParameterType.Bool);
        var off = new AnimationClip(); var on = new AnimationClip();
        string path = AnimationUtility.CalculateTransformPath(driver, avatar);
        foreach (var type in new[] { typeof(VRCParentConstraint), typeof(VRCScaleConstraint) })
        {
            AnimationUtility.SetEditorCurve(off, EditorCurveBinding.FloatCurve(path, type, "FreezeToWorld"), AnimationCurve.Constant(0, 1, 0));
            AnimationUtility.SetEditorCurve(on, EditorCurveBinding.FloatCurve(path, type, "FreezeToWorld"), AnimationCurve.Constant(0, 1, 1));
        }
        AssetDatabase.CreateAsset(off, folder + "/off.anim"); AssetDatabase.CreateAsset(on, folder + "/on.anim");
        var sm = fx.layers[0].stateMachine;
        var follow = sm.AddState("follow"); follow.motion = off; follow.writeDefaultValues = false;
        var drop = sm.AddState("drop"); drop.motion = on; drop.writeDefaultValues = false;
        sm.defaultState = follow;
        var enter = follow.AddTransition(drop); enter.hasExitTime = false; enter.duration = 0; enter.AddCondition(AnimatorConditionMode.If, 0, "drop");
        var leave = drop.AddTransition(follow); leave.hasExitTime = false; leave.duration = 0; leave.AddCondition(AnimatorConditionMode.IfNot, 0, "drop");
        animator = avatar.gameObject.AddComponent<Animator>(); animator.runtimeAnimatorController = fx;
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
            avatar = GameObject.Find("nxclone probe avatar").transform;
            anchor = avatar.Find("probe hand"); frame = avatar.Find("nxclone/world");
            driver = frame.Find("placement-1"); rootDriver = frame.Find("placement-root"); visual = driver.Find("clone-1"); trail = GameObject.Find("trail-1").transform;
            trailFrame = frame;
            placement = driver.GetComponent<VRCParentConstraint>();
            animator = avatar.GetComponent<Animator>();
            stage = 0; EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.Exit(0);
        }
    }

    static void Tick()
    {
        try
        {
            if (stage == 0)
            {
                var expected = anchor.Find("nxclone anchor placement-1").position;
                offsetOk = Near(driver.position, expected);
                rotationOffsetOk = NearRotation(driver.rotation, anchor.Find("nxclone anchor placement-1").rotation);
                rootOffsetOk = Near(rootDriver.position, avatar.Find("nxclone anchor placement-root").position);
                hiddenOk = !visual.gameObject.activeSelf && driver.gameObject.activeInHierarchy;
                scaleOk = Near(driver.lossyScale, anchor.Find("nxclone anchor placement-1").lossyScale);
                beforeFreeze = driver.position;
                Debug.Log($"PLAY follow anchor={anchor.position:F3} target={expected:F3} driver={driver.position:F3} driverScale={driver.lossyScale:F3} targetScale={anchor.Find("nxclone anchor placement-1").lossyScale:F3} visualActive={visual.gameObject.activeSelf}");
                avatar.position += Vector3.right * 3; stage++;
            }
            else if (stage == 1)
            {
                var expected = anchor.Find("nxclone anchor placement-1").position;
                frameOk = Near(frame.position, new Vector3(5, 0, 0));
                frameRotOk = Quaternion.Angle(frame.rotation, Quaternion.identity) < 0.1f;
                offsetOk &= Near(driver.position, expected);
                rotationOffsetOk &= NearRotation(driver.rotation, anchor.Find("nxclone anchor placement-1").rotation);
                rootOffsetOk &= Near(rootDriver.position, avatar.Find("nxclone anchor placement-root").position);
                Debug.Log($"PLAY moved anchor={anchor.position:F3} driver={driver.position:F3} expected={expected:F3} frame={frame.position:F3}");
                beforeFreeze = driver.position;
                beforeFreezeRotation = driver.rotation;
                beforeFreezeScale = driver.lossyScale;
                animator.SetBool("drop", true);
                holdStarted = Time.time;
                stage++;
            }
            else if (stage == 2)
            {
                avatar.position += Vector3.right * 0.2f;
                avatar.rotation = Quaternion.Euler(0, 80, 0);
                avatar.localScale = new Vector3(1.75f, 1.75f, 1.75f);
                if (Time.time - holdStarted >= 2f) stage++;
            }
            else if (stage == 3)
            {
                frameOk &= Near(frame.position, new Vector3(5, 0, 0));
                frameRotOk &= Quaternion.Angle(frame.rotation, Quaternion.identity) < 0.1f;
                freezeOk = Near(driver.position, beforeFreeze) && NearRotation(driver.rotation, beforeFreezeRotation) && Near(driver.lossyScale, beforeFreezeScale);
                Debug.Log($"PLAY frozen expected={beforeFreeze:F3} actual={driver.position:F3} scale={driver.lossyScale:F3} avatar={avatar.position:F3} flag={placement.FreezeToWorld}");
                animator.SetBool("drop", false); stage++;

            }
            else if (stage == 4)
            {
                avatar.position += Vector3.right * 3; stage++;
            }
            else if (stage == 5)
            {
                var expected = anchor.Find("nxclone anchor placement-1").position;
                var expectedRotation = anchor.Find("nxclone anchor placement-1").rotation;
                var expectedScale = anchor.Find("nxclone anchor placement-1").lossyScale;
                unfreezeOk = Near(driver.position, expected) && NearRotation(driver.rotation, expectedRotation) && Near(driver.lossyScale, expectedScale);
                Debug.Log($"PLAY unfrozen expected={expected:F3} actual={driver.position:F3} scale={driver.lossyScale:F3} rotation={driver.rotation.eulerAngles:F2}");
                trailPrevious = trail.position.x;
                stage++;
            }
            else if (stage < 12)
            {
                avatar.position += Vector3.right * 2;
                stage++;
                Debug.Log($"PLAY delay step={stage} trail={trail.position:F3} source={avatar.position:F3}");
                if (trail.position.x <= trailPrevious + 0.01f) trailProgress = false;
                trailPrevious = trail.position.x;
                if (Mathf.Abs(trail.position.x - avatar.position.x) > 0.02f) trailLag = true;
            }
            else
            {
                EditorApplication.update -= Tick;
                Debug.Log($"PLAY result rootOffset={rootOffsetOk} handOffset={offsetOk} handRotation={rotationOffsetOk} hiddenDriver={hiddenOk} frameFreeze={frameOk} frameRotation={frameRotOk} driverScale={scaleOk} dropFreeze={freezeOk} unfreeze={unfreezeOk} delayLag={trailLag} delayProgress={trailProgress}");
                if (!rootOffsetOk || !offsetOk || !rotationOffsetOk || !hiddenOk || !frameOk || !frameRotOk || !scaleOk || !freezeOk || !unfreezeOk || !trailLag || !trailProgress)
                    throw new Exception("Placement regression assertion failed; inspect preceding PLAY measurements");
                Debug.Log("NXCLONE_PLACEMENT_PLAYMODE_DONE");
                UnityEngine.Object.Destroy(avatar.gameObject);
                EditorApplication.ExitPlaymode();
            }
        }
        catch (Exception e) { EditorApplication.update -= Tick; Debug.LogException(e); SessionState.SetBool(RunningKey, false); EditorApplication.Exit(1); }
    }

    static bool Near(Vector3 a, Vector3 b) => (a - b).magnitude < 0.04f;
    static bool NearRotation(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) < 0.5f;
}
