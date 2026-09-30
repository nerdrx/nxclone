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
public static class NxCloneAxisSelectionSmoke
{
    const string RunningKey = "nxclone.axis-selection-smoke.running";
    const string Folder = "Assets/nxclone-axis-selection-smoke";
    const string PositionPrefix = "nxclone_axis_position";
    const string RotationPrefix = "nxclone_axis_rotation";
    static int stage, resumeFrames, waitFrames;
    static Transform avatar, anchor, driver, carrier;
    static Animator animator;
    static VRCParentConstraint placement;
    static Vector3 basePosition, frozenPosition;
    static Quaternion frozenRotation;
    static string positionParameter, rotationParameter;

    static NxCloneAxisSelectionSmoke() { EditorApplication.playModeStateChanged += OnMode; }

    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new Exception("Run from Edit Mode.");
        TestDefaults();
        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-axis-selection-smoke");
        TestAllMasks();
        BuildNativeFixture();
        AssetDatabase.SaveAssets();
        SessionState.SetBool(RunningKey, true);
        EditorApplication.EnterPlaymode();
    }

    static void TestDefaults()
    {
        var setupObject = new GameObject("nxclone axis defaults probe");
        var setup = setupObject.AddComponent<NxCloneSetup>();
        var preset = ScriptableObject.CreateInstance<NxClonePreset>();
        try
        {
            Assert(setup.positionAxes == NxCloneAxes.Z && setup.rotationAxes == NxCloneAxes.Y,
                "Setup defaults are not Distance + Yaw.");
            Assert(preset.positionAxes == NxCloneAxes.Z && preset.rotationAxes == NxCloneAxes.Y,
                "Preset defaults are not Distance + Yaw.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(preset);
            UnityEngine.Object.DestroyImmediate(setupObject);
        }
    }

    static void TestAllMasks()
    {
        const string graphFolder = Folder + "/graphs";
        AssetDatabase.CreateFolder(Folder, "graphs");
        for (int mask = 0; mask <= (int)NxCloneAxes.All; mask++)
        {
            var axes = (NxCloneAxes)mask;
            string suffix = mask.ToString("00");
            var root = new GameObject("axis graph avatar " + suffix).transform;
            var anchor = new GameObject("axis graph anchor " + suffix).transform;
            anchor.SetParent(root, false);
            anchor.localPosition = new Vector3(0.35f, 1.2f, -0.6f);
            var driver = new GameObject("axis graph driver " + suffix).transform;
            driver.SetParent(root, false);
            var constraint = driver.gameObject.AddComponent<VRCParentConstraint>();
            constraint.Sources.Add(new VRCConstraintSource(anchor, 1f));
            constraint.ActivateConstraint();
            constraint.ApplyConfigurationChanges();

            string assetPath = graphFolder + "/mask-" + suffix + ".controller";
            var fx = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
            int parametersBefore = fx.parameters.Length;
            int layersBefore = fx.layers.Length;
            var position = NxClonePositionControls.Configure(fx, root, anchor, graphFolder,
                "axis_position_" + suffix, 0.5f, axes);
            var rotation = NxCloneRotationControls.Configure(fx, root, driver, graphFolder,
                "axis_rotation_" + suffix, axes);
            int[] indices = Enumerable.Range(0, 3).Where(index => (mask & (1 << index)) != 0).ToArray();
            string[] axisNames = { "x", "y", "z" };
            string[] positionLabels = { "Left / Right", "Down / Up", "Distance" };
            string[] rotationLabels = { "Pitch", "Yaw", "Roll" };
            var expectedPosition = indices.Select(index => "axis_position_" + suffix + "_" + axisNames[index]).ToArray();
            var expectedRotation = indices.Select(index => "axis_rotation_" + suffix + "_" + axisNames[index]).ToArray();

            Assert(position.Parameters.SequenceEqual(expectedPosition), "Position parameters lost original axis identity for mask " + suffix + ".");
            Assert(rotation.Parameters.SequenceEqual(expectedRotation), "Rotation parameters lost original axis identity for mask " + suffix + ".");
            if (indices.Length == 0)
            {
                Assert(position.Menu == null && rotation.Menu == null, "None mask created a menu.");
                Assert(fx.parameters.Length == parametersBefore && fx.layers.Length == layersBefore,
                    "None mask added controller parameters or layers.");
                Assert(anchor.childCount == 0 && constraint.Sources[0].SourceTransform == anchor,
                    "None mask created rotation carriers or changed placement source.");
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                continue;
            }

            Assert(position.Parameters.Length == indices.Length && rotation.Parameters.Length == indices.Length,
                "Selected mask emitted an unexpected parameter count.");
            Assert(position.Menu && rotation.Menu && position.Menu.controls.Count == indices.Length &&
                   rotation.Menu.controls.Count == indices.Length, "Selected mask emitted an incomplete menu.");
            Assert(fx.parameters.Length == parametersBefore + indices.Length * 2,
                "Selected mask emitted parameters for disabled axes.");
            Assert(position.LayerNames.Length == 1 && fx.layers.Any(layer => layer.name == position.LayerNames[0]) &&
                   fx.layers.Count(layer => layer.name.StartsWith("nxclone axis_rotation_" + suffix + " rotation ", StringComparison.Ordinal)) == indices.Length,
                "Selected mask emitted an unexpected layer set.");
            for (int i = 0; i < indices.Length; i++)
            {
                Assert(position.Menu.controls[i].name == positionLabels[indices[i]] &&
                       position.Menu.controls[i].subParameters[0].name == expectedPosition[i],
                    "Position menu label or parameter is wrong for mask " + suffix + ".");
                Assert(rotation.Menu.controls[i].name == rotationLabels[indices[i]] &&
                       rotation.Menu.controls[i].subParameters[0].name == expectedRotation[i],
                    "Rotation menu label or parameter is wrong for mask " + suffix + ".");
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().ToArray();
            foreach (int disabledAxis in Enumerable.Range(0, 3).Except(indices))
            {
                string property = "m_LocalPosition." + axisNames[disabledAxis];
                foreach (var clip in clips.Where(clip => clip.name.StartsWith("nxclone position ", StringComparison.Ordinal)))
                {
                    var binding = AnimationUtility.GetCurveBindings(clip).Single(item => item.propertyName == property);
                    Assert(Mathf.Abs(AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f) - anchor.localPosition[disabledAxis]) < 0.0001f,
                        "Position clip changed disabled coordinate " + axisNames[disabledAxis] + " for mask " + suffix + ".");
                }
            }

            var expectedCarrierNames = indices.Select(index => "__nxclone rotation axis_rotation_" + suffix + " " + axisNames[index]).ToArray();
            var actualCarrierNames = anchor.GetComponentsInChildren<Transform>(true).Where(item => item != anchor)
                .Select(item => item.name).ToArray();
            Assert(actualCarrierNames.Length == indices.Length && actualCarrierNames.SequenceEqual(expectedCarrierNames),
                "Rotation carrier set or x/y/z order is wrong for mask " + suffix + ".");
            var finalCarrier = anchor.GetComponentsInChildren<Transform>(true).Single(item => item.name == expectedCarrierNames[expectedCarrierNames.Length - 1]);
            Assert(constraint.Sources[0].SourceTransform == finalCarrier,
                "Placement source does not use the last selected carrier for mask " + suffix + ".");
            UnityEngine.Object.DestroyImmediate(root.gameObject);
        }
        AssetDatabase.SaveAssets();
    }

    static void BuildNativeFixture()
    {
        var parent = new GameObject("nxclone axis parent probe").transform;
        parent.rotation = Quaternion.Euler(8f, 43f, -6f);
        avatar = new GameObject("nxclone axis probe avatar").transform;
        avatar.SetParent(parent, false);
        avatar.localRotation = Quaternion.Euler(-13f, 29f, 9f);
        anchor = new GameObject("nxclone axis custom anchor").transform;
        anchor.SetParent(avatar, false);
        anchor.localPosition = new Vector3(0.35f, 1.2f, -0.6f);
        anchor.localRotation = Quaternion.Euler(19f, 173f, -14f);
        basePosition = anchor.localPosition;
        driver = new GameObject("nxclone_axis_selection_driver").transform;
        driver.SetParent(avatar, false);
        driver.SetPositionAndRotation(anchor.position, anchor.rotation);
        placement = driver.gameObject.AddComponent<VRCParentConstraint>();
        placement.Sources.Add(new VRCConstraintSource(anchor, 1f));
        placement.ActivateConstraint();
        placement.ApplyConfigurationChanges();

        var fx = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/fx.controller");
        var position = NxClonePositionControls.Configure(fx, avatar, anchor, Folder, PositionPrefix, 0.5f, NxCloneAxes.Z);
        var rotation = NxCloneRotationControls.Configure(fx, avatar, driver, Folder, RotationPrefix, NxCloneAxes.Y);
        positionParameter = PositionPrefix + "_z";
        rotationParameter = RotationPrefix + "_y";
        Assert(position.Parameters.SequenceEqual(new[] { positionParameter }) && position.Menu.controls[0].name == "Distance",
            "Native position selection did not create the Distance dial.");
        Assert(rotation.Parameters.SequenceEqual(new[] { rotationParameter }) && rotation.Menu.controls[0].name == "Yaw",
            "Native rotation selection did not create the Yaw dial.");
        carrier = placement.Sources[0].SourceTransform;
        Assert(carrier.name == "__nxclone rotation " + RotationPrefix + " y" && carrier.parent == anchor,
            "Yaw selection did not create exactly one Y carrier.");

        fx.AddParameter("drop", AnimatorControllerParameterType.Bool);
        string path = AnimationUtility.CalculateTransformPath(driver, avatar);
        var follow = new AnimationClip { name = "follow" };
        var frozen = new AnimationClip { name = "frozen" };
        foreach (var clip in new[] { follow, frozen })
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path,
                typeof(VRCParentConstraint), "FreezeToWorld"),
                AnimationCurve.Constant(0f, 1f, clip == frozen ? 1f : 0f));
        AssetDatabase.CreateAsset(follow, Folder + "/follow.anim");
        AssetDatabase.CreateAsset(frozen, Folder + "/frozen.anim");
        var machine = new AnimatorStateMachine { name = "axis selection world drop" };
        AssetDatabase.AddObjectToAsset(machine, fx);
        var following = machine.AddState("Follow"); following.motion = follow; following.writeDefaultValues = false;
        var dropped = machine.AddState("Dropped"); dropped.motion = frozen; dropped.writeDefaultValues = false;
        machine.defaultState = following;
        var drop = following.AddTransition(dropped); drop.hasExitTime = false; drop.duration = 0;
        drop.AddCondition(AnimatorConditionMode.If, 0f, "drop");
        var resume = dropped.AddTransition(following); resume.hasExitTime = false; resume.duration = 0;
        resume.AddCondition(AnimatorConditionMode.IfNot, 0f, "drop");
        fx.AddLayer(new AnimatorControllerLayer { name = "nxclone axis selection world drop", defaultWeight = 1f, stateMachine = machine });
        animator = avatar.gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = fx;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    static void OnMode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            avatar = GameObject.Find("nxclone axis probe avatar").transform;
            anchor = avatar.Find("nxclone axis custom anchor");
            driver = avatar.Find("nxclone_axis_selection_driver");
            carrier = anchor.Find("__nxclone rotation " + RotationPrefix + " y");
            placement = driver.GetComponent<VRCParentConstraint>();
            animator = avatar.GetComponent<Animator>();
            stage = 0;
            waitFrames = 0;
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
                Assert(animator.parameters.Any(parameter => parameter.name == positionParameter) &&
                       animator.parameters.Any(parameter => parameter.name == rotationParameter), "Native selected dial parameters are missing.");
                animator.Update(0.1f);
                Assert(Near(anchor.localPosition, basePosition), "0.5 selected dials did not preserve base X/Y/Z.");
                AssertRotation(0f, "0.5 yaw dial changed base pitch/yaw/roll.");
                animator.SetFloat(positionParameter, 0.25f);
                animator.SetFloat(rotationParameter, 0.25f);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 1)
            {
                if (WaitFrames()) return;
                Assert(Near(anchor.localPosition, basePosition + new Vector3(0f, 0f, -0.25f)),
                    $"Distance {animator.GetFloat(positionParameter):0.00} produced {anchor.localPosition}; expected only Z at {basePosition + new Vector3(0f, 0f, -0.25f)}.");
                AssertRotation(-90f, "Yaw 0.25 has the wrong sign or changed base pitch/roll.");
                animator.SetFloat(positionParameter, 0.75f);
                animator.SetFloat(rotationParameter, 0.75f);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 2)
            {
                if (WaitFrames()) return;
                Assert(Near(anchor.localPosition, basePosition + new Vector3(0f, 0f, 0.25f)),
                    "Distance 0.75 did not move only Z by +0.25 m.");
                AssertRotation(90f, "Yaw 0.75 has the wrong sign or changed base pitch/roll.");
                frozenPosition = driver.position;
                frozenRotation = driver.rotation;
                animator.SetBool("drop", true);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 3)
            {
                if (WaitFrames()) return;
                animator.SetFloat(positionParameter, 0.25f);
                animator.SetFloat(rotationParameter, 0.25f);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 4)
            {
                if (WaitFrames()) return;
                Assert(Near(anchor.localPosition, basePosition + new Vector3(0f, 0f, -0.25f)),
                    "Selected dials did not keep updating the source during world drop.");
                AssertCarrierRotation(-90f, "Selected yaw dial stopped updating its source during world drop.");
                Assert(Near(driver.position, frozenPosition) && Quaternion.Angle(driver.rotation, frozenRotation) < 0.3f,
                    "World drop did not hold both driver position and orientation during selected dial edits.");
                animator.SetBool("drop", false);
                animator.Update(0.1f);
                stage++;
            }
            else if (stage == 5)
            {
                if (WaitFrames()) return;
                if (Near(driver.position, anchor.position) && Quaternion.Angle(driver.rotation, carrier.rotation) < 0.5f)
                    stage++;
                else if (++resumeFrames > 10)
                    throw new Exception("World drop did not resume following latest selected position and yaw.");
            }
            else
            {
                Debug.Log("NXCLONE_AXIS_SELECTION_PLAYMODE_OK");
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

    static void AssertRotation(float yaw, string message)
    {
        Quaternion expected = anchor.rotation * Quaternion.AngleAxis(yaw, Vector3.up);
        Assert(Quaternion.Angle(driver.rotation, expected) < 0.8f, message + " Expected " + expected + ", got " + driver.rotation + ".");
    }

    static void AssertCarrierRotation(float yaw, string message)
    {
        Quaternion expected = anchor.rotation * Quaternion.AngleAxis(yaw, Vector3.up);
        Assert(Quaternion.Angle(carrier.rotation, expected) < 0.8f,
            message + " Expected " + expected + ", got " + carrier.rotation + ".");
    }

    static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.0016f;
    static bool WaitFrames(int count = 2)
    {
        if (waitFrames++ < count) return true;
        waitFrames = 0;
        return false;
    }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
