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
public static class NxClonePositionSmoke
{
    const string RunningKey = "nxclone.position-smoke.running";
    static int stage;
    static Transform avatar, anchor, driver;
    static VRCParentConstraint placement;
    static Animator animator;
    static NxClonePositionControls.Result controls;
    static Vector3 frozenPosition, origin;
    static int resumeFrames;
    static readonly string[] Parameters = { "nxclone_pos1_x", "nxclone_pos1_y", "nxclone_pos1_z" };

    static NxClonePositionSmoke() { EditorApplication.playModeStateChanged += OnMode; }

    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new Exception("Run from Edit Mode.");
        const string folder = "Assets/nxclone-position-smoke";
        if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-position-smoke");
        avatar = new GameObject("nxclone position probe avatar").transform;
        avatar.rotation = Quaternion.Euler(0f, 90f, 0f);
        anchor = new GameObject("nxclone anchor placement-1").transform;
        anchor.SetParent(avatar, false);
        anchor.localPosition = new Vector3(0.25f, 1.1f, -0.4f);
        origin = anchor.localPosition;
        driver = new GameObject("placement-1").transform;
        driver.SetParent(avatar, false);
        driver.SetPositionAndRotation(anchor.position, anchor.rotation);
        var target = driver.gameObject.AddComponent<VRCParentConstraint>();
        target.Sources.Add(new VRCConstraintSource(anchor, 1f));
        target.ActivateConstraint(); target.ApplyConfigurationChanges();
        placement = target;
        var fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/fx.controller");
        controls = NxClonePositionControls.Configure(fx, avatar, anchor, folder, "nxclone_pos1", 2f);
        fx.AddParameter("drop", AnimatorControllerParameterType.Bool);
        var path = AnimationUtility.CalculateTransformPath(driver, avatar);
        var follow = new AnimationClip { name = "follow" };
        var frozen = new AnimationClip { name = "frozen" };
        foreach (var clip in new[] { follow, frozen })
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path,
                typeof(VRCParentConstraint), "FreezeToWorld"),
                AnimationCurve.Constant(0f, 1f, clip == frozen ? 1f : 0f));
        AssetDatabase.CreateAsset(follow, folder + "/follow.anim");
        AssetDatabase.CreateAsset(frozen, folder + "/frozen.anim");
        var machine = new AnimatorStateMachine { name = "world drop" };
        AssetDatabase.AddObjectToAsset(machine, fx);
        var following = machine.AddState("Follow"); following.motion = follow; following.writeDefaultValues = false;
        var dropped = machine.AddState("Dropped"); dropped.motion = frozen; dropped.writeDefaultValues = false;
        machine.defaultState = following;
        var drop = following.AddTransition(dropped); drop.hasExitTime = false; drop.duration = 0;
        drop.AddCondition(AnimatorConditionMode.If, 0f, "drop");
        var resume = dropped.AddTransition(following); resume.hasExitTime = false; resume.duration = 0;
        resume.AddCondition(AnimatorConditionMode.IfNot, 0f, "drop");
        fx.AddLayer(new AnimatorControllerLayer { name = "nxclone world drop", defaultWeight = 1f, stateMachine = machine });
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
            avatar = GameObject.Find("nxclone position probe avatar").transform;
            anchor = avatar.Find("nxclone anchor placement-1");
            driver = avatar.Find("placement-1");
            placement = driver.GetComponent<VRCParentConstraint>();
            animator = avatar.GetComponent<Animator>();
            origin = anchor.localPosition;
            var fx = (AnimatorController)animator.runtimeAnimatorController;
            controls = new NxClonePositionControls.Result
            {
                Parameters = Parameters,
                LayerNames = new[] { "nxclone nxclone_pos1 position" },
                Menu = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(fx)).OfType<VRCExpressionsMenu>().Single()
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
            if (stage == 0)
            {
                Assert(controls.Parameters.Length == 3 && controls.LayerNames.Length == 1 && controls.Menu.controls.Count == 3,
                    "Position dial outputs are incomplete.");
                for (int i = 0; i < 3; i++)
                {
                    var parameter = animator.parameters.Single(item => item.name == controls.Parameters[i]);
                    Assert(parameter.type == AnimatorControllerParameterType.Float && Mathf.Approximately(parameter.defaultFloat, 0.5f),
                        "Dial parameter is not a default-0.5 float.");
                    Assert(controls.Menu.controls[i].type == VRCExpressionsMenu.Control.ControlType.RadialPuppet &&
                           controls.Menu.controls[i].subParameters[0].name == controls.Parameters[i], "Radial menu does not target its dial.");
                }
                animator.Update(0.1f);
                Assert(Near(anchor.localPosition, origin), "0.5 dial defaults did not preserve anchor localPosition.");
                animator.SetFloat(controls.Parameters[0], 0f);
                animator.SetFloat(controls.Parameters[1], 1f);
                animator.SetFloat(controls.Parameters[2], 0f);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 1)
            {
                Assert(Near(anchor.localPosition, origin + new Vector3(-2f, 2f, -2f)),
                    $"Endpoint dials produced {anchor.localPosition}, expected {origin + new Vector3(-2f, 2f, -2f)} under avatar rotation.");
                frozenPosition = driver.position;
                animator.SetBool("drop", true);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 2)
            {
                animator.SetFloat(controls.Parameters[0], 1f);
                animator.SetFloat(controls.Parameters[1], 0f);
                animator.SetFloat(controls.Parameters[2], 1f);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 3)
            {
                Assert(Near(anchor.localPosition, origin + new Vector3(2f, -2f, 2f)), "Dials did not continue updating while dropped.");
                Assert(Near(driver.position, frozenPosition), "World drop did not hold the driver while dials changed.");
                animator.SetBool("drop", false);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 4)
            {
                if (Near(driver.position, anchor.position)) stage++;
                else if (++resumeFrames > 10)
                    throw new Exception($"Resuming follow placed driver at {driver.position}, anchor is {anchor.position}, freeze={placement.FreezeToWorld}.");
            }
            else
            {
                Debug.Log("NXCLONE_POSITION_PLAYMODE_OK");
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

    static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.0016f;
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
