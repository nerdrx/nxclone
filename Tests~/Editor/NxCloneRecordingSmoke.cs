using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;

namespace nxclone
{
    [InitializeOnLoad]
    public static class NxCloneRecordingSmoke
    {
        const string Folder = "Assets/nxclone-recording-smoke";
        const string NativeFolder = "Assets/nxclone-recording-native-smoke";
        const string NativeRunKey = "nxclone.recording.native.running";
        const string NativeCleanupKey = "nxclone.recording.native.cleanup";
        const string NativeExitCodeKey = "nxclone.recording.native.exit";
        const float Duration = 2f;
        const int Samples = 3;
        static int nativeStage, nextSample, pendingSampleFrames;
        static bool pendingSample, recordHoldChecked, nativeRecapture;
        static float nativeStart, captureStart;
        static Transform nativeRoot, nativeTarget, nativeFrame, nativeDriver;
        static Animator nativeAnimator;
        static VRCParentConstraint[] nativeMarkers;
        static VRCParentConstraint[] nativePositionMarkers;
        static VRCParentConstraint[] nativeRootMarkers;
        static VRCParentConstraint nativePlacement;
        static VRCRotationConstraint nativeOutputRotation;
        static VRCPositionConstraint nativeOutputPosition;
        static Quaternion[] nativeRotations;
        static Vector3[] nativePositions, nativeTrajectories;
        static string recordParameter, takeParameter, playParameter;
        static int nativeLayer, nativePlaybackLayer;
        static Vector3 initialFramePosition, expectedPlaybackRoot;
        static Quaternion firstCapturedRotation;
        static Vector3 firstCapturedTrajectory;

        static NxCloneRecordingSmoke()
        {
            EditorApplication.playModeStateChanged += OnPlayModeState;
        }

        public static void RunNativePlayMode()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run from Edit Mode.");
            SessionState.SetBool("nxclone.recording.native.reload", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt("nxclone.recording.native.options", (int)EditorSettings.enterPlayModeOptions);
            EditorSettings.enterPlayModeOptionsEnabled = false;
            AssetDatabase.DeleteAsset(NativeFolder);
            AssetDatabase.CreateFolder("Assets", "nxclone-recording-native-smoke");
            var fx = AnimatorController.CreateAnimatorControllerAtPath(NativeFolder + "/fx.controller");
            var root = new GameObject("nxclone recording native avatar").transform;
            root.position = new Vector3(5f, 0f, 0f);
            var sourceParent = new GameObject("source parent").transform;
            sourceParent.SetParent(root, false);
            var target = new GameObject("source bone").transform;
            target.SetParent(sourceParent, false);
            target.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
            target.localRotation = Quaternion.Euler(2f, 4f, 6f);
            var group = new GameObject("nxclone").transform;
            group.SetParent(root, false);
            var frame = new GameObject("world").transform;
            frame.SetParent(group, false);
            NxClonePlacement.FreezeFrame(frame);
            var driver = NxClonePlacement.Follow(frame, root, "placement-native", Vector3.zero, Vector3.zero);
            var visual = new GameObject("visual-native").transform;
            visual.SetParent(driver, false);
            var cloneParent = new GameObject("source parent").transform;
            cloneParent.SetParent(visual, false);
            var cloneBone = new GameObject("source bone").transform;
            cloneBone.SetParent(cloneParent, false);
            cloneBone.localPosition = target.localPosition;
            cloneBone.localRotation = target.localRotation;
            var rotation = cloneBone.gameObject.AddComponent<VRCRotationConstraint>();
            rotation.Sources.Add(new VRCConstraintSource(target, 1f));
            rotation.SolveInLocalSpace = true;
            rotation.ActivateConstraint();
            rotation.ApplyConfigurationChanges();
            NxCloneRecording.Configure(fx, root, visual, NativeFolder, "native", Samples, Duration, target);
            // Register native constraints with the SDK before the Animator animation graph exists.
            var animator = root.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = fx;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            AssetDatabase.SaveAssets();
            SessionState.SetBool(NativeRunKey, true);
            SessionState.SetBool(NativeCleanupKey, false);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(NativeRunKey, false))
            {
                try { StartNativeRun(); }
                catch (Exception ex) { FinishNativeRun(ex); }
            }
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(NativeCleanupKey, false))
            {
                EditorApplication.update -= TickNativeRun;
                AssetDatabase.DeleteAsset(NativeFolder);
                AssetDatabase.SaveAssets();
                EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt("nxclone.recording.native.options", 0);
                EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool("nxclone.recording.native.reload", false);
                int exitCode = SessionState.GetInt(NativeExitCodeKey, 1);
                SessionState.SetBool(NativeCleanupKey, false);
                SessionState.SetBool(NativeRunKey, false);
                EditorApplication.Exit(exitCode);
            }
        }

        static void StartNativeRun()
        {
            nativeRoot = GameObject.Find("nxclone recording native avatar").transform;
            nativeTarget = nativeRoot.Find("source parent/source bone");
            nativeFrame = nativeRoot.Find("nxclone/world");
            nativeDriver = nativeFrame.Find("placement-native");
            var visual = nativeDriver.Find("visual-native");
            nativeAnimator = nativeRoot.GetComponent<Animator>();
            nativePlacement = nativeDriver.GetComponent<VRCParentConstraint>();
            var outputTarget = visual.Find("source parent/source bone");
            nativeOutputRotation = visual.GetComponentsInChildren<VRCRotationConstraint>(true)
                .Single(c => c.Sources.Any(s => s.SourceTransform == nativeTarget));
            nativeOutputPosition = outputTarget.GetComponent<VRCPositionConstraint>();
            var bank = nativeFrame.Find("__nxclone pose samples native");
            nativeMarkers = bank.GetComponentsInChildren<VRCParentConstraint>(true)
                .Where(c => c.transform.name.StartsWith("bone-0000-sample-", StringComparison.Ordinal))
                .OrderBy(c => c.transform.name).ToArray();
            nativePositionMarkers = nativeMarkers;
            nativeRootMarkers = nativeFrame.Find("__nxclone pose trajectory native").GetComponentsInChildren<VRCParentConstraint>(true)
                .Where(c => c.transform.name.StartsWith("sample-", StringComparison.Ordinal))
                .OrderBy(c => c.transform.name).ToArray();
            Assert(nativeMarkers.Length == Samples && nativePositionMarkers.Length == Samples && nativeRootMarkers.Length == Samples,
                "Native Play Mode sample fixture is incomplete.");

            var fx = (AnimatorController)nativeAnimator.runtimeAnimatorController;
            recordParameter = "native_record";
            takeParameter = "native_take";
            playParameter = "native_play";
            nativeLayer = nativeAnimator.GetLayerIndex("native pose recording");
            nativePlaybackLayer = nativeAnimator.GetLayerIndex("native pose playback");
            nativeRotations = new Quaternion[Samples];
            nativePositions = new Vector3[Samples];
            nativeTrajectories = new Vector3[Samples];
            nextSample = 0;
            pendingSample = false;
            recordHoldChecked = false;
            nativeRecapture = false;
            pendingSampleFrames = 0;
            nativeStart = Time.time;
            initialFramePosition = nativeFrame.position;
            nativeStage = 0;
            EditorApplication.update += TickNativeRun;
        }

        static void TickNativeRun()
        {
            try
            {
                if (!nativeAnimator || !nativeRoot) throw new InvalidOperationException("Native fixture disappeared.");
                float elapsed = Time.time - nativeStart;
                if (nativeStage == 0)
                {
                    if (elapsed < 0.25f) return;
                    Assert(nativeFrame.position == initialFramePosition, "Frozen world frame moved before recording.");
                    nativeAnimator.SetBool(recordParameter, true);
                    nativeAnimator.Update(0.01f);
                    int commandLayer = nativeAnimator.GetLayerIndex("native pose record command");
                    Assert(nativeAnimator.GetCurrentAnimatorStateInfo(commandLayer).IsName("issue take 1"),
                        "Record request did not enter the one-shot Take command state.");
                    nativeAnimator.SetBool(takeParameter, true); // mirror the LocalOnly SDK driver write
                    nativeAnimator.SetBool(recordParameter, false); // mirror the menu Button auto-reset
                    nativeAnimator.SetBool(playParameter, false); // mirror the capture-entry SDK driver write
                    captureStart = 0f;
                    nativeStage = 1;
                    return;
                }
                if (nativeStage == 1)
                {
                var captureState = nativeAnimator.GetCurrentAnimatorStateInfo(nativeLayer);
                    if (!IsCaptureState(captureState, nativeRecapture)) return;
                    if (captureStart <= 0f) { captureStart = Time.time; return; }
                    float t = Time.time - captureStart;
                    float poseTime = t + (nativeRecapture ? 1.5f : 0f);
                    nativeRoot.position = new Vector3(5f + 0.4f * poseTime, 0f, 0.1f * poseTime);
                    nativeRoot.rotation = Quaternion.Euler(0f, poseTime * 8f, 0f);
                    nativeTarget.localPosition = new Vector3(0.1f, 0.2f + 0.08f * Mathf.Sin(poseTime * 2f), 0.3f);
                    nativeTarget.localRotation = Quaternion.Euler(poseTime * 25f, 4f + poseTime * 22f, 6f + poseTime * 10f);

                    // Let the constraint solver finish the frame where this sample freezes.
                    if (pendingSample && ++pendingSampleFrames >= 2)
                    {
                        nativeRotations[nextSample] = nativeMarkers[nextSample].transform.localRotation;
                        nativePositions[nextSample] = nativePositionMarkers[nextSample].transform.localPosition;
                        nativeTrajectories[nextSample] = nativeRootMarkers[nextSample].transform.position;
                        nextSample++;
                        pendingSample = false;
                    }
                    if (!pendingSample && nextSample < Samples && nativeMarkers[nextSample].FreezeToWorld &&
                        nativePositionMarkers[nextSample].FreezeToWorld && nativeRootMarkers[nextSample].FreezeToWorld)
                    {
                        pendingSample = true;
                        pendingSampleFrames = 0;
                    }
                    float firstFreezeAt = Duration * 0.5f / Samples;
                    if (!recordHoldChecked && t >= firstFreezeAt + 0.3f &&
                        IsCaptureState(nativeAnimator.GetCurrentAnimatorStateInfo(nativeLayer), nativeRecapture))
                    {
                        float rotationDrift = Quaternion.Angle(nativeMarkers[0].transform.localRotation, nativeRotations[0]);
                        float rootDrift = Vector3.Distance(nativeRootMarkers[0].transform.position, nativeTrajectories[0]);
                        Debug.Log($"record-state freeze drift after 0.3s: rotation={rotationDrift:F2} deg, root={rootDrift:F3} m");
                        Assert(rotationDrift < 0.1f, "Sample rotation drifted while still in the Record state.");
                        Assert(rootDrift < 0.02f, "Sample trajectory drifted while still in the Record state.");
                        recordHoldChecked = true;
                    }
                    if (t < Duration + 0.3f || nextSample != Samples ||
                        !IsCaptureState(nativeAnimator.GetCurrentAnimatorStateInfo(nativeLayer), nativeRecapture) ||
                        nativeAnimator.GetCurrentAnimatorStateInfo(nativeLayer).normalizedTime < 1f) return;

                    for (int i = 0; i < Samples; i++)
                    {
                        Assert(Quaternion.Angle(nativeRotations[i], nativeRotations[0]) > 2f || i == 0,
                            $"Native local rotation sample {i} did not change.");
                        Assert(Vector3.Distance(nativeTrajectories[i], nativeTrajectories[0]) > 0.05f || i == 0,
                            $"Native root trajectory sample {i} did not move.");
                    }
                    Debug.Log($"pose freeze={nativeMarkers[0].FreezeToWorld} localSolve={nativeMarkers[0].SolveInLocalSpace} actual={nativeMarkers[0].transform.localRotation.eulerAngles} sampled={nativeRotations[0].eulerAngles}; root freeze={nativeRootMarkers[0].FreezeToWorld} actual={nativeRootMarkers[0].transform.position} sampled={nativeTrajectories[0]}");
                    Assert(Quaternion.Angle(nativeMarkers[0].transform.localRotation, nativeRotations[0]) < 0.1f,
                        "Frozen local rotation sample drifted during capture.");
                    Assert(Vector3.Distance(nativeRootMarkers[0].transform.position, nativeTrajectories[0]) < 0.02f,
                        "Frozen root trajectory sample drifted during capture.");
                    Assert(nativeAnimator.GetBool(takeParameter) == !nativeRecapture,
                        "Durable Take phase changed before the next Record request.");
                    if (nativeRecapture)
                    {
                        Assert(Quaternion.Angle(nativeRotations[0], firstCapturedRotation) > 10f,
                            "A second record command did not capture a fresh pose.");
                        Assert(Vector3.Distance(nativeTrajectories[0], firstCapturedTrajectory) > 0.1f,
                            "A second record command did not capture a fresh trajectory.");
                        Debug.Log("NXCLONE_RECORDING_RECAPTURE_OK");
                        FinishNativeRun(null);
                        return;
                    }
                    firstCapturedRotation = nativeRotations[0];
                    firstCapturedTrajectory = nativeTrajectories[0];
                    nativeAnimator.SetBool(playParameter, true);
                    nativeStage = 2;
                    return;
                }
                if (nativeStage == 2)
                {
                    if (!nativeAnimator.GetCurrentAnimatorStateInfo(nativePlaybackLayer).IsName("playback") ||
                        nativeAnimator.GetCurrentAnimatorStateInfo(nativePlaybackLayer).normalizedTime < 0.24f) return;
                    var weights = nativePlacement.Sources;
                    Assert(Mathf.Abs(weights[0].Weight) < 0.05f &&
                           Mathf.Abs(weights[1].Weight - 0.25f) < 0.08f &&
                           Mathf.Abs(weights[2].Weight - 0.75f) < 0.08f,
                        "Native playback did not blend adjacent root trajectory samples.");
                    Assert(nativeOutputRotation.Sources.Count == Samples + 1 &&
                           Mathf.Abs(nativeOutputRotation.Sources[0].Weight) < 0.05f &&
                           Mathf.Abs(nativeOutputRotation.Sources[1].Weight - 0.25f) < 0.08f &&
                           Mathf.Abs(nativeOutputRotation.Sources[2].Weight - 0.75f) < 0.08f,
                        "Native playback did not blend adjacent rotation samples.");
                    Assert(nativeOutputPosition.Sources.Count == Samples + 1 &&
                           Mathf.Abs(nativeOutputPosition.Sources[0].Weight) < 0.05f &&
                           Mathf.Abs(nativeOutputPosition.Sources[1].Weight - 0.25f) < 0.08f &&
                           Mathf.Abs(nativeOutputPosition.Sources[2].Weight - 0.75f) < 0.08f,
                        "Native playback did not blend adjacent hips samples.");
                    expectedPlaybackRoot = Vector3.Lerp(nativeTrajectories[0], nativeTrajectories[1], 0.75f);
                    Assert(Vector3.Distance(nativeDriver.position, expectedPlaybackRoot) < 0.12f,
                        "Native root driver did not follow interpolated trajectory markers.");
                    Assert(nativePlacement.FreezeToWorld == false, "Trajectory playback was unexpectedly world-frozen.");
                    Assert(nativeMarkers.All(m => m.FreezeToWorld) && nativePositionMarkers.All(m => m.FreezeToWorld) &&
                           nativeRootMarkers.All(m => m.FreezeToWorld), "Playback released a frozen native sample.");
                    nativeRoot.position += Vector3.right * 3f;
                    nativeRoot.rotation *= Quaternion.Euler(0f, 40f, 0f);
                    nativeStage = 3;
                    return;
                }
                if (nativeStage == 3)
                {
                    Assert(Vector3.Distance(nativeDriver.position, expectedPlaybackRoot) < 0.15f,
                        "Playback trajectory moved with the live avatar instead of its frozen samples.");
                    nativeAnimator.SetBool(playParameter, false);
                    nativeStage = 4;
                    return;
                }
                var heldState = nativeAnimator.GetCurrentAnimatorStateInfo(nativeLayer);
                if (nativeStage == 4 && !nativeRecapture && IsCaptureState(heldState, false))
                {
                    Assert(nativeMarkers.All(m => m.FreezeToWorld) && nativePositionMarkers.All(m => m.FreezeToWorld) &&
                           nativeRootMarkers.All(m => m.FreezeToWorld), "Stopping native playback released a captured sample.");
                    Assert(Mathf.Abs(nativeOutputRotation.Sources[0].Weight - 1f) < 0.05f &&
                           nativeOutputRotation.Sources.Skip(1).All(s => Mathf.Abs(s.Weight) < 0.05f),
                        "Stopping playback did not restore the live rotation source.");
                    Assert(Mathf.Abs(nativeOutputPosition.Sources[0].Weight - 1f) < 0.05f &&
                           nativeOutputPosition.Sources.Skip(1).All(s => Mathf.Abs(s.Weight) < 0.05f),
                        "Stopping playback did not restore the live hips source.");
                    Assert(Mathf.Abs(nativePlacement.Sources[0].Weight - 1f) < 0.05f &&
                           nativePlacement.Sources.Skip(1).All(s => Mathf.Abs(s.Weight) < 0.05f),
                        "Stopping playback did not restore the live root source.");
                    Assert(nativeAnimator.GetBool(takeParameter),
                        "Durable Take phase did not remain set after playback stopped.");
                    nativeAnimator.SetBool(recordParameter, true);
                    nativeAnimator.Update(0.01f);
                    int commandLayer = nativeAnimator.GetLayerIndex("native pose record command");
                    Assert(nativeAnimator.GetCurrentAnimatorStateInfo(commandLayer).IsName("issue take 0"),
                        "Second Record request did not enter the opposite Take command state.");
                    nativeAnimator.SetBool(takeParameter, false); // mirror the LocalOnly SDK driver write
                    nativeAnimator.SetBool(recordParameter, false);
                    nativeRecapture = true;
                    nextSample = 0;
                    pendingSample = false;
                    pendingSampleFrames = 0;
                    recordHoldChecked = false;
                    captureStart = 0f;
                    return;
                }
                if (nativeStage == 4 && nativeRecapture && heldState.IsName("record take 0"))
                {
                    Assert(!nativeAnimator.GetBool(takeParameter), "Second Take phase did not persist through reset.");
                    captureStart = Time.time;
                    nativeStage = 1;
                    return;
                }
            }
            catch (Exception ex) { FinishNativeRun(ex); }
        }

        static bool IsCaptureState(AnimatorStateInfo state, bool recapture) =>
            state.IsName(recapture ? "record take 0" : "record take 1");

        static void FinishNativeRun(Exception error)
        {
            EditorApplication.update -= TickNativeRun;
            if (error != null) Debug.LogException(error);
            SessionState.SetInt(NativeExitCodeKey, error == null ? 0 : 1);
            SessionState.SetBool(NativeCleanupKey, true);
            EditorApplication.ExitPlaymode();
        }

        public static void Run()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "nxclone-recording-smoke");
            var fx = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/fx.controller");
            var root = new GameObject("recording smoke avatar").transform;
            try
            {
                var sourceParent = new GameObject("source parent").transform;
                sourceParent.SetParent(root, false);
                var target = new GameObject("source bone").transform;
                target.SetParent(sourceParent, false);
                target.localRotation = Quaternion.Euler(2f, 4f, 6f);
                target.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
                var group = new GameObject("nxclone").transform;
                group.SetParent(root, false);
                var worldFrame = new GameObject("world").transform;
                worldFrame.SetParent(group, false);
                NxClonePlacement.FreezeFrame(worldFrame);
                var driver = NxClonePlacement.Follow(worldFrame, root, "placement-1", Vector3.zero, Vector3.zero);
                var parentConstraint = driver.GetComponent<VRCParentConstraint>();
                parentConstraint.FreezeToWorld = true;
                parentConstraint.ApplyConfigurationChanges();
                var visual = new GameObject("visual").transform;
                visual.SetParent(driver, false);
                var cloneParent = new GameObject("source parent").transform;
                cloneParent.SetParent(visual, false);
                var cloneBone = new GameObject("source bone").transform;
                cloneBone.SetParent(cloneParent, false);
                cloneBone.localRotation = target.localRotation;
            var constraintHost = new GameObject("constraint host").transform;
            constraintHost.SetParent(visual, false);
            var rotation = constraintHost.gameObject.AddComponent<VRCRotationConstraint>();
            SetConstraintTarget(rotation, cloneBone);
                rotation.Sources.Add(new VRCConstraintSource(target, 1f));
                rotation.ActivateConstraint();
                rotation.ApplyConfigurationChanges();

                var result = NxCloneRecording.Configure(fx, root, visual, Folder, "smoke",
                    Samples, Duration, target);
                var position = cloneBone.GetComponent<VRCPositionConstraint>();
                Assert(position != null && result.PositionConstraints == 1, "Hips local-position constraint was not added.");
                Assert(result.RootTrajectorySamples == Samples, "Root trajectory sample count mismatch.");

                var driver2 = NxClonePlacement.Follow(worldFrame, root, "placement-2", Vector3.right, Vector3.zero);
                var visual2 = new GameObject("visual-2").transform;
                visual2.SetParent(driver2, false);
                var cloneParent2 = new GameObject("source parent").transform;
                cloneParent2.SetParent(visual2, false);
                var cloneBone2 = new GameObject("source bone").transform;
                cloneBone2.SetParent(cloneParent2, false);
                var rotation2 = cloneBone2.gameObject.AddComponent<VRCRotationConstraint>();
                rotation2.Sources.Add(new VRCConstraintSource(target, 1f));
                rotation2.ActivateConstraint();
                rotation2.ApplyConfigurationChanges();
                NxCloneRecording.Configure(fx, root, visual2, Folder, "smoke2", Samples, Duration, target);
                Assert(worldFrame.Find("__nxclone pose trajectory smoke") &&
                       worldFrame.Find("__nxclone pose trajectory smoke2"),
                    "Per-clone trajectory marker banks collided.");
                foreach (var layerAsset in fx.layers)
                    foreach (var stateAsset in layerAsset.stateMachine.states) stateAsset.state.writeDefaultValues = true;
                foreach (var layerAsset in fx.layers.Where(l => l.name.StartsWith("smoke", StringComparison.Ordinal)))
                    foreach (var stateAsset in layerAsset.stateMachine.states) stateAsset.state.writeDefaultValues = false;
                AssetDatabase.SaveAssets();

                var recordClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/smoke-pose-record.anim");
                var idleWeightsClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/smoke-pose-weights-idle.anim");
                var playbackClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/smoke-pose-playback.anim");
                Assert(recordClip && idleWeightsClip && playbackClip, "Recorder clips were not created.");
                var recordBindings = AnimationUtility.GetCurveBindings(recordClip);
                var idleBindings = AnimationUtility.GetCurveBindings(idleWeightsClip);
                var playBindings = AnimationUtility.GetCurveBindings(playbackClip);
                Assert(!recordBindings.Any(b => b.propertyName.StartsWith("Sources.source", StringComparison.Ordinal)),
                    "Capture layer must leave all source weights to the playback layer.");
                Assert(recordBindings.Count(b => b.type == typeof(VRCParentConstraint) && b.propertyName == "FreezeToWorld") == Samples * 2,
                    "Record clip must freeze rotation, hips, and root samples at their timestamps.");
                Assert(playBindings.Any(b => b.propertyName == "Sources.source0.Weight"), "Playback live-weight curve missing.");
                Assert(idleBindings.Any(b => b.type == typeof(VRCParentConstraint) && b.propertyName == "Sources.source0.Weight"),
                    "Root trajectory idle live-source curve missing.");
                Assert(!playBindings.Any(b => b.type == typeof(VRCParentConstraint) && b.path == "nxclone/world/placement-1" &&
                    b.propertyName == "FreezeToWorld"),
                    "Recorder must leave the placement driver world-drop flag untouched.");
                Assert(playBindings.Count(b => b.type == typeof(VRCPositionConstraint) && b.propertyName.StartsWith("Sources.source")) == Samples + 1,
                    "Hips position source curves are incomplete.");
                Assert(!playBindings.Any(b => b.propertyName == "FreezeToWorld"),
                    "Playback layer must not reapply sample freeze bindings.");
                for (int i = 0; i < Samples; i++)
                {
                    string property = $"Sources.source{i + 1}.Weight";
                    var binding = playBindings.FirstOrDefault(b => b.type == typeof(VRCRotationConstraint) && b.propertyName == property);
                    Assert(binding.type == typeof(VRCRotationConstraint), $"Playback curve {property} missing.");
                    float expected = AnimationUtility.GetEditorCurve(playbackClip, binding).Evaluate(Duration * 0.25f);
                    Assert(Mathf.Abs(expected - (i == 0 ? 0.25f : i == 1 ? 0.75f : 0f)) < 0.001f,
                        $"Playback curve {property} does not interpolate samples.");
                }

                var animator = root.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = fx;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.SetFloat(result.SpeedParameter, 0f); // capture duration must ignore playback speed
                animator.Update(0f);
                var sampleBank = worldFrame.Find("__nxclone pose samples smoke");
                var markers = sampleBank.GetComponentsInChildren<VRCParentConstraint>(true)
                    .Where(c => c.transform.name.StartsWith("bone-0000-sample-", StringComparison.Ordinal))
                    .OrderBy(c => c.transform.name).ToArray();
                Assert(markers.Length == Samples, "Sample marker count mismatch.");
                Assert(markers.All(m => !m.FreezeToWorld), "Ready must leave every rotation sample live.");
                var positionMarkers = markers;
                Assert(positionMarkers.Length == Samples && positionMarkers.All(m => !m.FreezeToWorld),
                    "Hips position samples were not live.");
                var rootMarkers = worldFrame.Find("__nxclone pose trajectory smoke").GetComponentsInChildren<VRCParentConstraint>(true)
                    .Where(c => c.transform.name.StartsWith("sample-", StringComparison.Ordinal))
                    .OrderBy(c => c.transform.name).ToArray();
                Assert(rootMarkers.Length == Samples && rootMarkers.All(m => !m.FreezeToWorld),
                    "Root trajectory samples were not live.");

                Assert(!string.IsNullOrEmpty(result.TakeParameter) && !string.IsNullOrEmpty(result.CommandLayerName),
                    "Take phase and Record command layer names are missing.");
                var commandLayerAsset = fx.layers.Single(l => l.name == result.CommandLayerName);
                var commandStates = commandLayerAsset.stateMachine.states.Select(entry => entry.state).ToArray();
                Assert(commandStates.Length == 4 && commandStates.All(state => !state.writeDefaultValues),
                    "Record command must use four Write Defaults Off handshake states.");
                RequireTransition(commandStates.Single(state => state.name == "wait take 0"), "issue take 1",
                    AnimatorConditionMode.If, result.RecordParameter);
                RequireTransition(commandStates.Single(state => state.name == "issue take 1"), "wait take 1",
                    AnimatorConditionMode.IfNot, result.RecordParameter);
                RequireTransition(commandStates.Single(state => state.name == "wait take 1"), "issue take 0",
                    AnimatorConditionMode.If, result.RecordParameter);
                RequireTransition(commandStates.Single(state => state.name == "issue take 0"), "wait take 0",
                    AnimatorConditionMode.IfNot, result.RecordParameter);
                foreach (var issue in commandStates.Where(state => state.name.StartsWith("issue ", StringComparison.Ordinal)))
                {
                    var commandDriver = issue.behaviours.OfType<VRCAvatarParameterDriver>().SingleOrDefault();
                    float expectedTake = issue.name.EndsWith("1", StringComparison.Ordinal) ? 1f : 0f;
                    Assert(commandDriver && commandDriver.localOnly && commandDriver.parameters.Count == 1 &&
                        commandDriver.parameters[0].name == result.TakeParameter &&
                        commandDriver.parameters[0].type == VRC_AvatarParameterDriver.ChangeType.Set &&
                        Mathf.Approximately(commandDriver.parameters[0].value, expectedTake),
                        "Command issue state must set the durable Take phase once through a LocalOnly driver.");
                }
                animator.SetBool(result.TakeParameter, true); // native mirror of the command driver write
                animator.Update(0.01f);
                int layer = animator.GetLayerIndex("smoke pose recording");
                int playbackLayer = animator.GetLayerIndex("smoke pose playback");
                Assert(!fx.layers.Where(l => l.name == "smoke pose recording" || l.name == "smoke pose playback" || l.name == result.CommandLayerName)
                    .SelectMany(l => l.stateMachine.states).Any(s => s.state.writeDefaultValues),
                    "Recorder states must keep Write Defaults off.");
                Assert(layer >= 0 && animator.GetCurrentAnimatorStateInfo(layer).IsName("record take 1"), "Take phase did not start capture.");

                float elapsed = animator.GetCurrentAnimatorStateInfo(layer).normalizedTime * Duration;
                var poses = new Quaternion[Samples];
                var positions = new Vector3[Samples];
                var trajectories = new Vector3[Samples];
                for (int i = 0; i < Samples; i++)
                {
                    poses[i] = Quaternion.Euler(15f + i * 11f, 7f + i * 19f, 3f + i * 13f);
                    positions[i] = new Vector3(0.2f + i * 0.1f, 0.4f + i * 0.2f, 0.6f + i * 0.3f);
                    trajectories[i] = new Vector3(i, i * 0.25f, -i * 0.5f);
                    for (int j = i; j < Samples; j++)
                    {
                        markers[j].transform.localRotation = poses[i];
                        positionMarkers[j].transform.localPosition = positions[i];
                        rootMarkers[j].transform.localPosition = trajectories[i];
                    }
                    float sampleTime = Duration * (i + 0.5f) / Samples;
                    animator.Update(Mathf.Max(0f, sampleTime - elapsed));
                    elapsed = sampleTime;
                    Assert(markers[i].FreezeToWorld, $"Rotation sample {i} did not freeze at its sample time.");
                    Assert(positionMarkers[i].FreezeToWorld && rootMarkers[i].FreezeToWorld,
                        $"Position or root marker {i} did not freeze at its sample time.");
                    Assert(Quaternion.Angle(markers[i].transform.localRotation, poses[i]) < 0.1f,
                        $"Marker {i} local rotation changed while freezing.");
                    Assert(Vector3.Distance(positionMarkers[i].transform.localPosition, positions[i]) < 0.001f,
                        $"Hips position marker {i} changed while freezing.");
                    Assert(Vector3.Distance(rootMarkers[i].transform.localPosition, trajectories[i]) < 0.001f,
                        $"Root trajectory marker {i} changed while freezing.");
                }

                animator.Update(Mathf.Max(0.01f, Duration - elapsed + 0.02f));
                Assert(animator.GetCurrentAnimatorStateInfo(layer).IsName("record take 1") &&
                       animator.GetCurrentAnimatorStateInfo(layer).normalizedTime >= 1f,
                    "Capture state did not stay at the end of its fixed-duration clip.");
                Assert(markers.All(m => m.FreezeToWorld), "Captured markers were unfrozen by the held state.");
                Assert(positionMarkers.All(m => m.FreezeToWorld) && rootMarkers.All(m => m.FreezeToWorld),
                    "Captured position or root markers were unfrozen by the held state.");
                Assert(animator.GetBool(result.TakeParameter), "Completed capture must retain its synced Take phase.");
                for (int i = 0; i < Samples; i++)
                {
                    Assert(Quaternion.Angle(markers[i].transform.localRotation, poses[i]) < 0.1f,
                        $"Captured marker {i} pose was overwritten.");
                    Assert(Vector3.Distance(positionMarkers[i].transform.localPosition, positions[i]) < 0.001f,
                        $"Captured hips position {i} was overwritten.");
                }
                Assert(parentConstraint.FreezeToWorld, "Recorder changed the world-drop freeze flag.");

                animator.SetBool(result.RecordParameter, false); // button request is independent of the retained Take phase
                animator.Update(0.01f);
                Assert(animator.GetCurrentAnimatorStateInfo(layer).IsName("record take 1"), "A released Record request exited the frozen sample hold.");
                Assert(markers.All(m => m.FreezeToWorld), "Record false unfroze a captured marker.");
                Assert(positionMarkers.All(m => m.FreezeToWorld) && rootMarkers.All(m => m.FreezeToWorld),
                    "Record false unfroze captured position or trajectory markers.");
                animator.SetFloat(result.SpeedParameter, 0.5f);
                animator.SetBool(result.PlayParameter, true);
                animator.Update(0.01f);
                Assert(animator.GetCurrentAnimatorStateInfo(playbackLayer).IsName("playback"), "Playback state did not start.");
                animator.Update(0.49f); // speed .5 * state multiplier 2: .5 seconds in the clip
                Assert(Mathf.Abs(rotation.Sources[0].Weight) < 0.01f,
                    $"Playback did not mute the live source (w0={rotation.Sources[0].Weight:F3}, state={animator.GetCurrentAnimatorStateInfo(playbackLayer).IsName("playback")}, play={animator.GetBool(result.PlayParameter)}).");
                Assert(Mathf.Abs(rotation.Sources[1].Weight - 0.25f) < 0.03f &&
                       Mathf.Abs(rotation.Sources[2].Weight - 0.75f) < 0.03f &&
                       Mathf.Abs(rotation.Sources[3].Weight) < 0.03f,
                    "Playback source weights did not interpolate adjacent samples.");
                Assert(markers.All(m => m.FreezeToWorld), "Playback unfroze a captured marker.");
                Assert(positionMarkers.All(m => m.FreezeToWorld) && rootMarkers.All(m => m.FreezeToWorld),
                    "Playback re-enabled a captured position or trajectory marker.");
                Assert(parentConstraint.FreezeToWorld, "Playback changed the world-drop freeze flag.");
                Assert(Mathf.Abs(parentConstraint.Sources[0].Weight) < 0.01f &&
                       Mathf.Abs(parentConstraint.Sources[1].Weight - 0.25f) < 0.03f &&
                       Mathf.Abs(parentConstraint.Sources[2].Weight - 0.75f) < 0.03f &&
                       Mathf.Abs(parentConstraint.Sources[3].Weight) < 0.03f,
                    "Root trajectory source weights did not interpolate adjacent samples.");
                Assert(Mathf.Abs(position.Sources[0].Weight) < 0.01f &&
                       Mathf.Abs(position.Sources[1].Weight - 0.25f) < 0.03f &&
                       Mathf.Abs(position.Sources[2].Weight - 0.75f) < 0.03f &&
                       Mathf.Abs(position.Sources[3].Weight) < 0.03f,
                    "Hips position source weights did not interpolate adjacent samples.");
                for (int i = 0; i < Samples; i++)
                {
                    Assert(Quaternion.Angle(markers[i].transform.localRotation, poses[i]) < 0.1f,
                        $"Playback changed captured marker {i} local rotation.");
                    Assert(Vector3.Distance(positionMarkers[i].transform.localPosition, positions[i]) < 0.001f,
                        $"Playback changed captured hips position {i}.");
                }

                animator.Update(Duration - 0.5f);
                Assert(Mathf.Abs(rotation.Sources[1].Weight - 1f) < 0.03f,
                    "Looped playback did not return to the first sample.");
                Assert(Mathf.Abs(parentConstraint.Sources[1].Weight - 1f) < 0.03f &&
                       Mathf.Abs(position.Sources[1].Weight - 1f) < 0.03f,
                    "Looped root or hips playback did not return to the first sample.");
                animator.SetBool(result.PlayParameter, false);
                animator.Update(0.01f);
                Assert(animator.GetCurrentAnimatorStateInfo(layer).IsName("record take 1") &&
                       animator.GetCurrentAnimatorStateInfo(playbackLayer).IsName("idle"),
                    "Playback did not return to the frozen sample hold.");
                Assert(animator.GetBool(result.TakeParameter), "Stopping playback cleared the durable Take phase.");
                Assert(Mathf.Abs(rotation.Sources[0].Weight - 1f) < 0.01f &&
                       Mathf.Abs(rotation.Sources[1].Weight) < 0.01f,
                    "Playback exit did not restore live bone weights.");
                Assert(Mathf.Abs(parentConstraint.Sources[0].Weight - 1f) < 0.01f &&
                       Mathf.Abs(parentConstraint.Sources[1].Weight) < 0.01f,
                    "Playback exit did not restore live root weights.");
                Assert(markers.All(m => m.FreezeToWorld), "Stopping playback unfroze captured markers.");
                Assert(positionMarkers.All(m => m.FreezeToWorld) && rootMarkers.All(m => m.FreezeToWorld),
                    "Stopping playback unfroze captured position or trajectory markers.");
                for (int i = 0; i < Samples; i++)
                {
                    Assert(Quaternion.Angle(markers[i].transform.localRotation, poses[i]) < 0.1f,
                        $"Stopping playback overwrote marker {i}.");
                    Assert(Vector3.Distance(positionMarkers[i].transform.localPosition, positions[i]) < 0.001f,
                        $"Stopping playback overwrote hips sample {i}.");
                }

                Debug.Log($"NXCLONE_RECORDING_SMOKE_OK samples={result.SampleCount} duration={result.DurationSeconds}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                AssetDatabase.DeleteAsset(Folder);
                AssetDatabase.SaveAssets();
            }
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void RequireTransition(AnimatorState state, string destination,
            AnimatorConditionMode mode, string parameter)
        {
            var transition = state.transitions.FirstOrDefault(item => item.destinationState &&
                item.destinationState.name == destination);
            Assert(transition != null && !transition.hasExitTime && transition.duration == 0f &&
                transition.conditions.Any(condition => condition.mode == mode && condition.parameter == parameter),
                $"Record command transition {state.name} -> {destination} is missing its {mode} {parameter} gate.");
        }

        static Transform ConstraintTarget(Component constraint) =>
            constraint.GetType().GetField("TargetTransform", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public)?.GetValue(constraint) as Transform;

        static void SetConstraintTarget(Component constraint, Transform target) =>
            constraint.GetType().GetField("TargetTransform", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public)?.SetValue(constraint, target);
    }
}
