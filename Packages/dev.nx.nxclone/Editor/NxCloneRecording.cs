using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;

namespace nxclone
{
    /// <summary>
    /// Adds sampled bone rotations, hips local position, and root position/rotation to a constrained visual.
    /// Root travel is represented by evenly spaced samples; gestures and continuous trajectories are not recorded.
    /// </summary>
    public static class NxCloneRecording
    {
        public const int DefaultSampleCount = 8;
        public const int MaximumSampleCount = 15; // VRChat animates at most 16 sources per constraint.
        public const float DefaultDurationSeconds = 5f;

        public sealed class Result
        {
            public string RecordParameter { get; internal set; }
            public string TakeParameter { get; internal set; }
            public string PlayParameter { get; internal set; }
            public string SpeedParameter { get; internal set; }
            public string CaptureLayerName { get; internal set; }
            public string CommandLayerName { get; internal set; }
            public string PlaybackLayerName { get; internal set; }
            public int SampleCount { get; internal set; }
            public float DurationSeconds { get; internal set; }
            public int RotationConstraints { get; internal set; }
            public int PositionConstraints { get; internal set; }
            public int RootTrajectorySamples { get; internal set; }
        }

        /// <summary>
        /// Adds a native sample capture layer and a separate sampled playback layer.
        /// The folder must exist, and the visual must be fresh because its source lists are replaced.
        /// </summary>
        public static Result Configure(AnimatorController fx, Transform avatarRoot,
            Transform visual, string folder, string parameterPrefix,
            int sampleCount = DefaultSampleCount, float durationSeconds = DefaultDurationSeconds,
            Transform hipsBone = null)
        {
            if (!fx) throw new ArgumentNullException(nameof(fx));
            if (!avatarRoot) throw new ArgumentNullException(nameof(avatarRoot));
            if (!visual) throw new ArgumentNullException(nameof(visual));
            if (visual != avatarRoot && !visual.IsChildOf(avatarRoot))
                throw new ArgumentException("The visual must be under the avatar root.", nameof(visual));
            if (sampleCount < 1 || sampleCount > MaximumSampleCount)
                throw new ArgumentOutOfRangeException(nameof(sampleCount), $"Use 1 to {MaximumSampleCount} samples.");
            if (durationSeconds <= 0f || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException("The recorder asset folder must already exist.", nameof(folder));
            if (string.IsNullOrWhiteSpace(parameterPrefix))
                throw new ArgumentException("A parameter prefix is required.", nameof(parameterPrefix));

            string prefix = parameterPrefix.Trim();
            NormalizeTargetConstraintHosts(visual);
            var bones = visual.GetComponentsInChildren<VRCRotationConstraint>(true)
                .Select(c => (Constraint: c, Target: LiveSource(c, visual)))
                .Where(x => x.Target)
                .ToArray();
            if (bones.Length == 0) throw new InvalidOperationException("The visual has no live-target rotation constraints.");

            var placement = visual.parent ? visual.parent.GetComponent<VRCParentConstraint>() : null;
            var worldFrame = visual.parent ? visual.parent.parent : null;
            if (!placement || !worldFrame || placement.Sources.Count != 1 || !placement.Sources[0].SourceTransform)
                throw new InvalidOperationException("The visual must be under a single-source nxclone placement driver in the frozen world frame.");
            var rootTarget = placement.Sources[0].SourceTransform;
            hipsBone = hipsBone ? hipsBone : avatarRoot.GetComponent<Animator>()?.GetBoneTransform(HumanBodyBones.Hips);
            int hipsIndex = Array.FindIndex(bones, b => b.Target == hipsBone);

            string record = prefix + "_record";
            string take = prefix + "_take";
            string play = prefix + "_play";
            string speed = prefix + "_speed";
            AddParameter(fx, record, AnimatorControllerParameterType.Bool);
            AddParameter(fx, take, AnimatorControllerParameterType.Bool);
            AddParameter(fx, play, AnimatorControllerParameterType.Bool);
            AddParameter(fx, speed, AnimatorControllerParameterType.Float, 0.5f);

            var bank = new GameObject("__nxclone pose samples " + prefix).transform;
            bank.SetParent(worldFrame, false);
            bank.localPosition = Vector3.zero;
            bank.localRotation = Quaternion.identity;
            bank.localScale = Vector3.one;

            var rootMarkers = new Transform[sampleCount];
            int rootSourceStart = placement.Sources.Count;
            string rootDriverPath = PathOf(avatarRoot, placement.transform);
            var trajectoryBank = new GameObject("__nxclone pose trajectory " + prefix).transform;
            trajectoryBank.SetParent(worldFrame, false);
            trajectoryBank.localPosition = Vector3.zero;
            trajectoryBank.localRotation = Quaternion.identity;
            trajectoryBank.localScale = Vector3.one;
            for (int i = 0; i < sampleCount; i++)
            {
                var marker = new GameObject($"sample-{i + 1:00}").transform;
                marker.SetParent(trajectoryBank, false);
                marker.SetPositionAndRotation(rootTarget.position, rootTarget.rotation);
                var markerConstraint = marker.gameObject.AddComponent<VRCParentConstraint>();
                markerConstraint.Sources.Add(new VRCConstraintSource(rootTarget, 1f));
                markerConstraint.ActivateConstraint();
                markerConstraint.ApplyConfigurationChanges();
                rootMarkers[i] = marker;
                placement.Sources.Add(new VRCConstraintSource(marker, 0f));
            }
            placement.ActivateConstraint();
            placement.ApplyConfigurationChanges();

            var ready = new AnimationClip { name = prefix + " pose ready" };
            var capture = new AnimationClip { name = prefix + " pose record", frameRate = 60f };
            var idleWeights = new AnimationClip { name = prefix + " pose weights idle" };
            var playback = new AnimationClip { name = prefix + " pose playback", frameRate = 60f };
            var playbackSettings = AnimationUtility.GetAnimationClipSettings(playback);
            playbackSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(playback, playbackSettings);

            SetRootWeightCurves(idleWeights, playback, rootDriverPath, rootSourceStart, sampleCount, durationSeconds);

            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                var item = bones[boneIndex];
                var constraint = item.Constraint;
                string visualPath = PathOf(avatarRoot, constraint.transform);
                while (constraint.Sources.Count > 0) constraint.Sources.RemoveAt(constraint.Sources.Count - 1);
                constraint.SolveInLocalSpace = true;
                constraint.Sources.Add(new VRCConstraintSource(item.Target, 1f));
                constraint.ActivateConstraint();

                SetStatic(idleWeights, visualPath, typeof(VRCRotationConstraint), "Sources.source0.Weight", 1f);
                SetStatic(playback, visualPath, typeof(VRCRotationConstraint), "Sources.source0.Weight", 0f, durationSeconds);

                for (int i = 0; i < sampleCount; i++)
                {
                    var marker = new GameObject($"bone-{boneIndex:0000}-sample-{i + 1:00}").transform;
                    marker.SetParent(bank, false);
                    marker.localRotation = item.Target.localRotation;
                    if (boneIndex == hipsIndex) marker.localPosition = item.Target.localPosition;
                    var markerConstraint = marker.gameObject.AddComponent<VRCParentConstraint>();
                    markerConstraint.SolveInLocalSpace = true;
                    markerConstraint.Sources.Add(new VRCConstraintSource(item.Target, 1f));
                    markerConstraint.ActivateConstraint();
                    markerConstraint.ApplyConfigurationChanges();

                    int sourceIndex = i + 1;
                    constraint.Sources.Add(new VRCConstraintSource(marker, 0f));
                    string markerPath = PathOf(avatarRoot, marker);
                    AddFreezeCurves(ready, capture, markerPath,
                        typeof(VRCParentConstraint), i, sampleCount, durationSeconds);
                    SetStatic(idleWeights, visualPath, typeof(VRCRotationConstraint),
                        $"Sources.source{sourceIndex}.Weight", 0f);
                    AddPlaybackWeight(playback, visualPath, sourceIndex, sampleCount, i, durationSeconds,
                        typeof(VRCRotationConstraint));
                }
                constraint.ApplyConfigurationChanges();
            }

            var positionConstraints = 0;
            if (hipsIndex >= 0)
            {
                var hip = bones[hipsIndex];
                var hipsTarget = ConstraintTarget(hip.Constraint) ?? hip.Constraint.transform;
                var position = visual.GetComponentsInChildren<VRCPositionConstraint>(true)
                    .FirstOrDefault(c => (ConstraintTarget(c) ?? c.transform) == hipsTarget);
                if (!position) position = hipsTarget.gameObject.AddComponent<VRCPositionConstraint>();
                SetConstraintTarget(position, hipsTarget);
                string path = PathOf(avatarRoot, position.transform);
                while (position.Sources.Count > 0) position.Sources.RemoveAt(position.Sources.Count - 1);
                position.SolveInLocalSpace = true;
                position.Sources.Add(new VRCConstraintSource(hipsBone, 1f));
                for (int i = 0; i < sampleCount; i++)
                    position.Sources.Add(new VRCConstraintSource(bank.Find($"bone-{hipsIndex:0000}-sample-{i + 1:00}"), 0f));
                position.ActivateConstraint();
                position.ApplyConfigurationChanges();
                for (int i = 0; i <= sampleCount; i++)
                {
                    string source = $"Sources.source{i}.Weight";
                    if (i == 0)
                    {
                        SetStatic(idleWeights, path, typeof(VRCPositionConstraint), source, 1f);
                        SetStatic(playback, path, typeof(VRCPositionConstraint), source, 0f, durationSeconds);
                    }
                    else AddPlaybackWeight(playback, path, i, sampleCount, i - 1,
                        durationSeconds, typeof(VRCPositionConstraint));
                    if (i > 0) SetStatic(idleWeights, path, typeof(VRCPositionConstraint), source, 0f);
                }
                positionConstraints = 1;
            }

            for (int i = 0; i < sampleCount; i++)
            {
                string markerPath = PathOf(avatarRoot, rootMarkers[i]);
                AddFreezeCurves(ready, capture, markerPath,
                    typeof(VRCParentConstraint), i, sampleCount, durationSeconds);
            }

            AssetDatabase.CreateAsset(ready, $"{folder}/{SafeName(prefix)}-pose-ready.anim");
            AssetDatabase.CreateAsset(capture, $"{folder}/{SafeName(prefix)}-pose-record.anim");
            AssetDatabase.CreateAsset(idleWeights, $"{folder}/{SafeName(prefix)}-pose-weights-idle.anim");
            AssetDatabase.CreateAsset(playback, $"{folder}/{SafeName(prefix)}-pose-playback.anim");
            string captureLayerName = prefix + " pose recording";
            string commandLayerName = prefix + " pose record command";
            string playbackLayerName = prefix + " pose playback";
            AddCaptureLayer(fx, captureLayerName, ready, capture, take, play);
            AddCommandLayer(fx, commandLayerName, record, take);
            AddPlaybackLayer(fx, playbackLayerName, idleWeights, playback, play, speed);
            EditorUtility.SetDirty(fx);
            return new Result { RecordParameter = record, TakeParameter = take, PlayParameter = play, SpeedParameter = speed,
                CaptureLayerName = captureLayerName, CommandLayerName = commandLayerName, PlaybackLayerName = playbackLayerName,
                SampleCount = sampleCount, DurationSeconds = durationSeconds,
                RotationConstraints = bones.Length, PositionConstraints = positionConstraints,
                RootTrajectorySamples = sampleCount };
        }

        static Transform LiveSource(VRCRotationConstraint constraint, Transform visual)
        {
            foreach (var source in constraint.Sources)
            {
                var transform = source.SourceTransform;
                if (transform && transform != constraint.transform && !transform.IsChildOf(visual)) return transform;
            }
            return null;
        }

        static void NormalizeTargetConstraintHosts(Transform visual)
        {
            foreach (var group in visual.GetComponentsInChildren<VRCRotationConstraint>(true).GroupBy(c => c.transform))
                if (group.Count() > 1) foreach (var constraint in group.ToArray()) MoveTargetedConstraintToChild(constraint);
            foreach (var group in visual.GetComponentsInChildren<VRCPositionConstraint>(true).GroupBy(c => c.transform))
                if (group.Count() > 1) foreach (var constraint in group.ToArray()) MoveTargetedConstraintToChild(constraint);
        }

        static void MoveTargetedConstraintToChild(Component original)
        {
            var target = ConstraintTarget(original);
            if (!target || target == original.transform) return;
            var child = new GameObject($"__nxclone recording {original.GetType().Name} {target.name}").transform;
            child.SetParent(original.transform, false);
            var copy = child.gameObject.AddComponent(original.GetType());
            EditorUtility.CopySerialized(original, copy);
            SetConstraintTarget(copy, target);
            copy.GetType().GetMethod("ActivateConstraint", BindingFlags.Instance | BindingFlags.Public)?.Invoke(copy, null);
            copy.GetType().GetMethod("ApplyConfigurationChanges", BindingFlags.Instance | BindingFlags.Public)?.Invoke(copy, null);
            UnityEngine.Object.DestroyImmediate(original);
        }

        static Transform ConstraintTarget(Component constraint)
        {
            var field = constraint.GetType().GetField("TargetTransform", BindingFlags.Instance | BindingFlags.Public);
            return field != null && field.FieldType == typeof(Transform) ? field.GetValue(constraint) as Transform : null;
        }

        static void SetConstraintTarget(Component constraint, Transform target)
        {
            var field = constraint.GetType().GetField("TargetTransform", BindingFlags.Instance | BindingFlags.Public);
            if (field != null && field.FieldType == typeof(Transform)) field.SetValue(constraint, target);
        }

        static void AddParameter(AnimatorController fx, string name,
            AnimatorControllerParameterType type, float defaultFloat = 0f)
        {
            var existing = fx.parameters.FirstOrDefault(p => p.name == name);
            if (existing != null)
            {
                if (existing.type != type) throw new InvalidOperationException($"Animator parameter '{name}' has the wrong type.");
                return;
            }
            fx.AddParameter(new AnimatorControllerParameter { name = name, type = type, defaultFloat = defaultFloat });
        }

        static void AddCaptureLayer(AnimatorController fx, string name, AnimationClip readyClip,
            AnimationClip captureClip, string take, string play)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var idle = AddState(machine, "idle", readyClip);
            var reset = AddState(machine, "reset", readyClip);
            var capture0 = AddState(machine, "record take 0", captureClip);
            var capture1 = AddState(machine, "record take 1", captureClip);
            machine.defaultState = idle;

            AddCondition(idle.AddTransition(capture1), AnimatorConditionMode.If, take);
            AddCondition(capture0.AddTransition(reset), AnimatorConditionMode.If, take);
            AddCondition(capture1.AddTransition(reset), AnimatorConditionMode.IfNot, take);
            AddExitCondition(reset.AddTransition(capture0), AnimatorConditionMode.IfNot, take, 0.02f);
            AddExitCondition(reset.AddTransition(capture1), AnimatorConditionMode.If, take, 0.02f);

            // Each phase owns the completed clip tail until the next take. The intervening
            // reset state releases FreezeToWorld before the opposite phase captures again.
            foreach (var captureState in new[] { capture0, capture1 })
            {
                var driver = captureState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter {
                name = play, type = VRC_AvatarParameterDriver.ChangeType.Set, value = 0f
                });
            }
            fx.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1f, stateMachine = machine });
        }

        static void AddCommandLayer(AnimatorController fx, string name, string record, string take)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var wait0 = AddState(machine, "wait take 0", null);
            var issue1 = AddState(machine, "issue take 1", null);
            var wait1 = AddState(machine, "wait take 1", null);
            var issue0 = AddState(machine, "issue take 0", null);
            machine.defaultState = wait0;
            AddCondition(wait0.AddTransition(issue1), AnimatorConditionMode.If, record);
            AddCondition(issue1.AddTransition(wait1), AnimatorConditionMode.IfNot, record);
            AddCondition(wait1.AddTransition(issue0), AnimatorConditionMode.If, record);
            AddCondition(issue0.AddTransition(wait0), AnimatorConditionMode.IfNot, record);
            SetLocal(issue1, take, 1f);
            SetLocal(issue0, take, 0f);
            fx.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1f, stateMachine = machine });
        }

        static void SetLocal(AnimatorState state, string parameter, float value)
        {
            var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            driver.localOnly = true;
            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter {
                name = parameter, type = VRC_AvatarParameterDriver.ChangeType.Set, value = value
            });
        }

        static void AddPlaybackLayer(AnimatorController fx, string name, AnimationClip idleClip,
            AnimationClip playbackClip, string play, string speed)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var idle = AddState(machine, "idle", idleClip);
            var playback = AddState(machine, "playback", playbackClip);
            machine.defaultState = idle;
            AddCondition(idle.AddTransition(playback), AnimatorConditionMode.If, play);
            AddCondition(playback.AddTransition(idle), AnimatorConditionMode.IfNot, play);
            AddSpeed(playback, speed, 2f);
            fx.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1f, stateMachine = machine });
        }

        static AnimatorState AddState(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            var state = machine.AddState(name);
            state.motion = clip;
            state.writeDefaultValues = false;
            return state;
        }

        static void AddSpeed(AnimatorState state, string parameter, float multiplier)
        {
            state.speedParameterActive = true;
            state.speedParameter = parameter;
            state.speed = multiplier;
        }

        static void AddCondition(AnimatorStateTransition transition, AnimatorConditionMode mode, string parameter)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(mode, 0f, parameter);
        }

        static void AddExitCondition(AnimatorStateTransition transition, AnimatorConditionMode mode,
            string parameter, float exitTime)
        {
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = 0f;
            transition.AddCondition(mode, 0f, parameter);
        }

        static void AddFreezeCurves(AnimationClip ready, AnimationClip capture, string markerPath,
            Type constraintType, int sample, int count, float duration)
        {
            SetStatic(ready, markerPath, constraintType, "FreezeToWorld", 0f);
            SetStep(capture, markerPath, constraintType, "FreezeToWorld", 0f,
                duration * (sample + 0.5f) / count, 1f, duration);
        }

        static void SetRootWeightCurves(AnimationClip idle, AnimationClip playback, string path,
            int sourceStart, int count, float duration)
        {
            for (int i = 0; i <= count; i++)
            {
                int index = i == 0 ? 0 : sourceStart + i - 1;
                string property = $"Sources.source{index}.Weight";
                if (i == 0)
                {
                    SetStatic(idle, path, typeof(VRCParentConstraint), property, 1f);
                    SetStatic(playback, path, typeof(VRCParentConstraint), property, 0f, duration);
                }
                else
                {
                    SetStatic(idle, path, typeof(VRCParentConstraint), property, 0f);
                    AddPlaybackWeight(playback, path, index, count, i - 1, duration,
                        typeof(VRCParentConstraint));
                }
            }
        }

        static void AddPlaybackWeight(AnimationClip clip, string path, int sourceIndex,
            int count, int sample, float duration, Type type)
        {
            var keys = new Keyframe[count + 1];
            for (int frame = 0; frame <= count; frame++)
            {
                int selected = frame == count ? 0 : frame;
                keys[frame] = new Keyframe(duration * frame / count, selected == sample ? 1f : 0f);
            }
            SetCurve(clip, path, type,
                $"Sources.source{sourceIndex}.Weight", keys, AnimationUtility.TangentMode.Linear);
        }

        static void SetStatic(AnimationClip clip, string path, Type type, string property,
            float value, float duration = 1f) =>
            SetCurve(clip, path, type, property,
                new[] { new Keyframe(0f, value), new Keyframe(duration, value) }, AnimationUtility.TangentMode.Linear);

        static void SetStep(AnimationClip clip, string path, Type type, string property,
            float start, float stepTime, float end, float duration)
        {
            SetCurve(clip, path, type, property,
                new[] { new Keyframe(0f, start), new Keyframe(stepTime, end), new Keyframe(duration, end) },
                AnimationUtility.TangentMode.Constant);
        }

        static void SetCurve(AnimationClip clip, string path, Type type, string property,
            Keyframe[] keys, AnimationUtility.TangentMode mode)
        {
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, mode);
                AnimationUtility.SetKeyRightTangentMode(curve, i, mode);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
        }

        static string PathOf(Transform root, Transform child)
        {
            var parts = new List<string>();
            for (var current = child; current && current != root; current = current.parent) parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string SafeName(string value) => string.Concat(value.Select(c =>
            char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
    }
}
