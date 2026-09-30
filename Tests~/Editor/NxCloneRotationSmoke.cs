using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using nxclone;

[InitializeOnLoad]
public static class NxCloneRotationSmoke
{
    const string RunningKey = "nxclone.rotation-smoke.running";
    const string Folder = "Assets/nxclone-rotation-smoke";
    const string Prefix = "nxclone_rotation_smoke";
    const string PositionPrefix = "nxclone_rotation_smoke_position";
    static readonly string[] Parameters = { Prefix + "_x", Prefix + "_y", Prefix + "_z" };
    static readonly string[] PositionParameters = { PositionPrefix + "_x", PositionPrefix + "_y", PositionPrefix + "_z" };
    static int stage, resumeFrames;
    static Transform avatar, anchor, driver, finalCarrier;
    static Animator animator;
    static VRCParentConstraint placement;
    static Vector3 basePosition, dialPosition, frozenPosition;
    static Quaternion baseRotation, frozenRotation;
    static NxCloneRotationControls.Result controls;
    static NxClonePositionControls.Result positionControls;

    static NxCloneRotationSmoke() { EditorApplication.playModeStateChanged += OnMode; }

    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new Exception("Run from Edit Mode.");
        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-rotation-smoke");
        var parent = new GameObject("nxclone rotation parent probe").transform;
        parent.rotation = Quaternion.Euler(9f, 57f, -4f);
        avatar = new GameObject("nxclone rotation probe avatar").transform;
        avatar.SetParent(parent, false);
        avatar.localRotation = Quaternion.Euler(-11f, 31f, 7f);
        anchor = new GameObject("nxclone rotation custom anchor").transform;
        anchor.SetParent(avatar, false);
        anchor.localPosition = new Vector3(0.25f, 1.1f, -0.4f);
        anchor.localRotation = Quaternion.Euler(18f, 180f, -12f);
        basePosition = anchor.localPosition;
        baseRotation = anchor.rotation;
        driver = new GameObject("nxclone_rotation_smoke_driver").transform;
        driver.SetParent(avatar, false);
        driver.SetPositionAndRotation(anchor.position, anchor.rotation);
        placement = driver.gameObject.AddComponent<VRCParentConstraint>();
        placement.Sources.Add(new VRCConstraintSource(anchor, 1f));
        placement.ActivateConstraint(); placement.ApplyConfigurationChanges();

        var fx = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/fx.controller");
        positionControls = NxClonePositionControls.Configure(fx, avatar, anchor, Folder, PositionPrefix, 0.5f);
        controls = NxCloneRotationControls.Configure(fx, avatar, driver, Folder, Prefix);
        fx.AddParameter("drop", AnimatorControllerParameterType.Bool);
        var path = AnimationUtility.CalculateTransformPath(driver, avatar);
        var follow = new AnimationClip { name = "follow" };
        var frozen = new AnimationClip { name = "frozen" };
        foreach (var clip in new[] { follow, frozen })
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path,
                typeof(VRCParentConstraint), "FreezeToWorld"),
                AnimationCurve.Constant(0f, 1f, clip == frozen ? 1f : 0f));
        AssetDatabase.CreateAsset(follow, Folder + "/follow.anim");
        AssetDatabase.CreateAsset(frozen, Folder + "/frozen.anim");
        var machine = new AnimatorStateMachine { name = "world drop" };
        AssetDatabase.AddObjectToAsset(machine, fx);
        var following = machine.AddState("Follow"); following.motion = follow; following.writeDefaultValues = false;
        var dropped = machine.AddState("Dropped"); dropped.motion = frozen; dropped.writeDefaultValues = false;
        machine.defaultState = following;
        var drop = following.AddTransition(dropped); drop.hasExitTime = false; drop.duration = 0;
        drop.AddCondition(AnimatorConditionMode.If, 0f, "drop");
        var resume = dropped.AddTransition(following); resume.hasExitTime = false; resume.duration = 0;
        resume.AddCondition(AnimatorConditionMode.IfNot, 0f, "drop");
        fx.AddLayer(new AnimatorControllerLayer { name = "nxclone rotation smoke world drop", defaultWeight = 1f, stateMachine = machine });
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
            avatar = GameObject.Find("nxclone rotation probe avatar").transform;
            anchor = avatar.Find("nxclone rotation custom anchor");
            driver = avatar.Find("nxclone_rotation_smoke_driver");
            placement = driver.GetComponent<VRCParentConstraint>();
            animator = avatar.GetComponent<Animator>();
            var fx = (AnimatorController)animator.runtimeAnimatorController;
            finalCarrier = placement.Sources[0].SourceTransform;
            controls = new NxCloneRotationControls.Result
            {
                Parameters = Parameters,
                LayerNames = new[] { "nxclone " + Prefix + " rotation pitch", "nxclone " + Prefix + " rotation yaw", "nxclone " + Prefix + " rotation roll" },
                Menu = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(fx)).OfType<VRCExpressionsMenu>()
                    .Single(menu => menu.name == "nxclone rotation dials")
            };
            stage = 0;
            EditorApplication.update += Tick;
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
            var fx = (AnimatorController)animator.runtimeAnimatorController;
            if (stage == 0)
            {
                Assert(controls.Parameters.Length == 3 && controls.LayerNames.Length == 3 && controls.Menu.controls.Count == 3,
                    "Rotation dial outputs are incomplete.");
                Assert(finalCarrier != anchor && finalCarrier.IsChildOf(anchor), "Driver source 0 was not replaced by the carrier chain.");
                for (int i = 0; i < 3; i++)
                {
                    var parameter = animator.parameters.Single(item => item.name == Parameters[i]);
                    Assert(parameter.type == AnimatorControllerParameterType.Float && Mathf.Approximately(parameter.defaultFloat, 0.5f),
                        "Rotation dial parameter is not a default-0.5 float.");
                    Assert(fx.layers.Single(layer => layer.name == controls.LayerNames[i]).defaultWeight == 1f,
                        "Rotation dial layer is not enabled.");
                    Assert(controls.Menu.controls[i].type == VRCExpressionsMenu.Control.ControlType.RadialPuppet &&
                           controls.Menu.controls[i].subParameters[0].name == Parameters[i], "Radial menu does not target its dial.");
                }
                animator.Update(0.1f);
                Assert(Quaternion.Angle(anchor.rotation, baseRotation) < 0.2f, "Neutral dials changed anchor base rotation (including yaw 180).");
                animator.SetFloat(PositionParameters[0], 1f);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 1)
            {
                dialPosition = anchor.localPosition;
                Assert(!Near(dialPosition, basePosition), "Position dial fixture did not move its anchor.");
                animator.SetFloat(Parameters[0], 0.25f); // -90 degrees
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 2)
            {
                AssertRotation(Quaternion.AngleAxis(-90f, Vector3.right), "Pitch intermediate has the wrong sign.");
                Assert(Near(anchor.localPosition, dialPosition), "Rotation dial changed position-controlled anchor translation.");
                animator.SetFloat(Parameters[1], 0.75f); // +90 degrees
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 3)
            {
                AssertRotation(Quaternion.AngleAxis(-90f, Vector3.right) * Quaternion.AngleAxis(90f, Vector3.up),
                    "Combined pitch/yaw dials did not compose on separate carriers.");
                animator.SetFloat(Parameters[2], 0.25f); // -90 degrees
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 4)
            {
                var combined = Quaternion.AngleAxis(-90f, Vector3.right) * Quaternion.AngleAxis(90f, Vector3.up) *
                    Quaternion.AngleAxis(-90f, Vector3.forward);
                AssertRotation(combined, "Combined pitch/yaw/roll dials did not compose.");
                animator.SetFloat(Parameters[0], 0.5f);
                animator.SetFloat(Parameters[1], 0.5f);
                animator.SetFloat(Parameters[2], 0.5f);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 5)
            {
                AssertRotation(Quaternion.identity, "0.5 rotation dials did not preserve the anchor base orientation.");
                frozenPosition = driver.position;
                frozenRotation = driver.rotation;
                animator.SetBool("drop", true);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 6)
            {
                animator.SetFloat(Parameters[0], 0.75f);
                animator.SetFloat(Parameters[1], 0.25f);
                animator.SetFloat(Parameters[2], 0.75f);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 7)
            {
                var droppedDialPose = anchor.rotation * Quaternion.AngleAxis(90f, Vector3.right) *
                    Quaternion.AngleAxis(-90f, Vector3.up) * Quaternion.AngleAxis(90f, Vector3.forward);
                Assert(Quaternion.Angle(finalCarrier.rotation, droppedDialPose) < 0.8f,
                    "Dials failed to update their placement carrier while dropped.");
                Assert(Near(driver.position, frozenPosition) && Quaternion.Angle(driver.rotation, frozenRotation) < 0.3f,
                    "World drop did not hold driver position and orientation during dial edits.");
                animator.SetBool("drop", false);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 8)
            {
                var latest = anchor.rotation * Quaternion.AngleAxis(90f, Vector3.right) *
                    Quaternion.AngleAxis(-90f, Vector3.up) * Quaternion.AngleAxis(90f, Vector3.forward);
                if (Quaternion.Angle(driver.rotation, latest) < 0.5f && Near(driver.position, anchor.position)) stage++;
                else if (++resumeFrames > 10)
                    throw new Exception($"Resuming follow did not adopt current dial orientation: driver={driver.rotation}, expected={latest}, freeze={placement.FreezeToWorld}.");
            }
            else
            {
                Debug.Log("NXCLONE_ROTATION_PLAYMODE_OK");
                EditorApplication.update -= Tick;
                UnityEngine.Object.Destroy(avatar.parent.gameObject);
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

    static void AssertRotation(Quaternion offset, string message)
    {
        Quaternion expected = anchor.rotation * offset;
        Assert(Quaternion.Angle(driver.rotation, expected) < 0.8f,
            $"{message} driver={driver.rotation}, expected={expected}, anchor={anchor.rotation}.");
    }

    static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.0016f;
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
