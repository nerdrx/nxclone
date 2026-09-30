using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace nxclone
{
    /// <summary>
    /// Samples local FX parameters into unsynced Animator buffers and replays them as timed steps.
    /// SDK parameter drivers are serialized here; Unity Editor does not execute them.
    /// </summary>
    public static class NxCloneExpressionRecording
    {
        public struct Mapping
        {
            public string Input;
            public string Target;
            // Keep the effective source type on the clone alias and every private sample buffer.
            public AnimatorControllerParameterType Type;

            public Mapping(string input, string target, AnimatorControllerParameterType type)
            { Input = input; Target = target; Type = type; }
        }

        public sealed class Result
        {
            public string CaptureRequestParameter { get; internal set; }
            public string CaptureLayerName { get; internal set; }
            public string PlaybackLayerName { get; internal set; }
            public string TimerPath { get; internal set; }
            public string[][] Buffers { get; internal set; }
            public Mapping[] Mappings { get; internal set; }
        }

        /// <summary>
        /// Adds one snapshot buffer per pose sample. Each durable take phase emits a private
        /// one-shot request when its capture state begins.
        /// </summary>
        public static Result Configure(AnimatorController fx, NxCloneRecording.Result recording,
            Transform avatarRoot, string folder, string prefix, IEnumerable<Mapping> mappings)
        {
            if (!fx || recording == null || !avatarRoot || string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException("Expression recording needs an FX controller, avatar root, and existing asset folder.");
            if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("Expression recording needs a unique parameter prefix.");
            var items = (mappings ?? Enumerable.Empty<Mapping>()).ToArray();
            if (items.Length == 0) throw new ArgumentException("Expression recording needs at least one parameter mapping.");
            if (items.Any(item => string.IsNullOrWhiteSpace(item.Input) || string.IsNullOrWhiteSpace(item.Target) ||
                !ValidType(item.Type))) throw new ArgumentException("Expression mappings need names and bool, int, or float types.");
            if (items.Select(item => item.Target).Distinct(StringComparer.Ordinal).Count() != items.Length)
                throw new InvalidOperationException("Expression recording target aliases must be unique.");
            foreach (var item in items)
            {
                if (IsBuiltin(item.Target))
                    throw new InvalidOperationException("Expression playback targets must be per-clone aliases, never live VRChat built-ins.");
                RequireParameter(fx, item.Target, item.Type, true);
                RequireInput(fx, item.Input, item.Type);
            }

            var recordLayer = fx.layers.FirstOrDefault(layer => layer.name == recording.CaptureLayerName);
            if (recordLayer == null || !recordLayer.stateMachine)
                throw new InvalidOperationException("Pose recorder capture layer is missing.");
            var recordStates = recordLayer.stateMachine.states.Select(entry => entry.state)
                .Where(state => state.name == "record take 0" || state.name == "record take 1").ToArray();
            if (recordStates.Length != 2)
                throw new InvalidOperationException("Pose recorder needs both durable take capture states to signal expression capture.");

            string request = UniqueName(fx, Safe(prefix) + "_capture_request");
            AddParameter(fx, request, AnimatorControllerParameterType.Bool);
            foreach (var recordState in recordStates)
            {
                var recorderDriver = recordState.behaviours.OfType<VRCAvatarParameterDriver>().FirstOrDefault();
                if (!recorderDriver) throw new InvalidOperationException($"Pose recorder state '{recordState.name}' has no SDK ParameterDriver.");
                if (!recorderDriver.parameters.Any(parameter => parameter.name == request))
                    recorderDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter {
                        name = request, type = VRC_AvatarParameterDriver.ChangeType.Set, value = 1f
                    });
            }

            var buffers = new string[recording.SampleCount][];
            for (int sample = 0; sample < recording.SampleCount; sample++)
            {
                buffers[sample] = new string[items.Length];
                for (int mapping = 0; mapping < items.Length; mapping++)
                {
                    string name = UniqueName(fx, Safe(prefix) + $"_s{sample + 1:00}_p{mapping + 1:00}");
                    AddParameter(fx, name, items[mapping].Type);
                    buffers[sample][mapping] = name;
                }
            }

            var backups = new string[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Input != items[i].Target) continue;
                backups[i] = UniqueName(fx, Safe(prefix) + $"_restore_p{i + 1:00}");
                AddParameter(fx, backups[i], items[i].Type);
            }

            var timer = new GameObject("__nxclone expression timer " + Safe(prefix)).transform;
            timer.SetParent(avatarRoot, false);
            // A harmless constant transform curve gives timed Animator states a one-second clock.
            var timerClip = new AnimationClip { name = Safe(prefix) + " expression timer", frameRate = 60f };
            AnimationUtility.SetEditorCurve(timerClip,
                EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(timer, avatarRoot),
                    typeof(Transform), "m_LocalScale.x"),
                new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1)));
            string timerPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Safe(prefix) + "-expression-timer.anim");
            AssetDatabase.CreateAsset(timerClip, timerPath);

            string captureLayerName = Safe(prefix) + " expression capture";
            string playbackLayerName = Safe(prefix) + " expression playback";
            AddCaptureLayer(fx, captureLayerName, timerClip, recording, request, items, buffers);
            AddPlaybackLayer(fx, playbackLayerName, timerClip, recording, request, items, buffers, backups);
            EditorUtility.SetDirty(fx);
            return new Result { CaptureRequestParameter = request, CaptureLayerName = captureLayerName,
                PlaybackLayerName = playbackLayerName, TimerPath = timerPath, Buffers = buffers, Mappings = items };
        }

        static void AddCaptureLayer(AnimatorController fx, string name, AnimationClip timer,
            NxCloneRecording.Result recording, string request, Mapping[] mappings, string[][] buffers)
        {
            var machine = NewMachine(fx, name);
            var ready = machine.AddState("ready");
            var firstWait = machine.AddState("first sample delay"); firstWait.motion = timer;
            firstWait.speed = 2f * recording.SampleCount / recording.DurationSeconds;
            Driver(firstWait, new[] { Set(request, 0) });
            var states = new AnimatorState[recording.SampleCount];
            for (int sample = 0; sample < states.Length; sample++)
            {
                states[sample] = machine.AddState("sample " + (sample + 1));
                states[sample].motion = timer;
                states[sample].speed = (float)recording.SampleCount / recording.DurationSeconds;
                if (sample == states.Length - 1) states[sample].speed *= 2f;
                var copies = new List<VRC_AvatarParameterDriver.Parameter>();
                for (int p = 0; p < mappings.Length; p++) copies.Add(Copy(mappings[p].Input, buffers[sample][p]));
                Driver(states[sample], copies);
                if (sample + 1 < states.Length) ExitAtEnd(states[sample], states[sample + 1]);
            }
            var held = machine.AddState("captured");
            if (states.Length > 0) ExitAtEnd(states[states.Length - 1], held);
            machine.defaultState = ready;
            Any(machine, firstWait, AnimatorConditionMode.If, request, true);
            ExitAtEnd(firstWait, states[0]);
            fx.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1f, stateMachine = machine });
        }

        static void AddPlaybackLayer(AnimatorController fx, string name, AnimationClip timer,
            NxCloneRecording.Result recording, string request, Mapping[] mappings,
            string[][] buffers, string[] backups)
        {
            var machine = NewMachine(fx, name);
            var idle = machine.AddState("idle");
            var backup = machine.AddState("cache live values"); backup.motion = timer; backup.speed = 60f;
            var cache = new List<VRC_AvatarParameterDriver.Parameter>();
            for (int p = 0; p < mappings.Length; p++)
                if (!string.IsNullOrEmpty(backups[p])) cache.Add(Copy(mappings[p].Target, backups[p]));
            if (cache.Count > 0) Driver(backup, cache);
            var states = new AnimatorState[recording.SampleCount];
            for (int sample = 0; sample < states.Length; sample++)
            {
                states[sample] = machine.AddState("sample " + (sample + 1));
                states[sample].motion = timer;
                states[sample].speed = 2f * recording.SampleCount / recording.DurationSeconds;
                states[sample].speedParameterActive = true;
                states[sample].speedParameter = recording.SpeedParameter;
                var copies = new List<VRC_AvatarParameterDriver.Parameter>();
                for (int p = 0; p < mappings.Length; p++) copies.Add(Copy(buffers[sample][p], mappings[p].Target));
                Driver(states[sample], copies);
                if (sample + 1 < states.Length) ExitAtEnd(states[sample], states[sample + 1]);
            }
            var restore = machine.AddState("restore live values"); restore.motion = timer; restore.speed = 60f;
            var restoreCopies = new List<VRC_AvatarParameterDriver.Parameter>();
            for (int p = 0; p < mappings.Length; p++)
                restoreCopies.Add(Copy(string.IsNullOrEmpty(backups[p]) ? mappings[p].Input : backups[p], mappings[p].Target));
            Driver(restore, restoreCopies);
            var resume = machine.AddState("live");
            machine.defaultState = idle;
            Condition(idle.AddTransition(backup), AnimatorConditionMode.If, recording.PlayParameter);
            ExitAtEnd(backup, states[0]);
            for (int sample = 0; sample < states.Length; sample++)
            {
                Condition(states[sample].AddTransition(restore), AnimatorConditionMode.IfNot, recording.PlayParameter);
                Condition(states[sample].AddTransition(restore), AnimatorConditionMode.If, request);
                if (sample + 1 < states.Length) continue;
                ExitAtEnd(states[sample], states[0]);
            }
            ExitAtEnd(restore, resume);
            Condition(resume.AddTransition(backup), AnimatorConditionMode.If, recording.PlayParameter);
            Condition(resume.AddTransition(idle), AnimatorConditionMode.IfNot, recording.PlayParameter);
            fx.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1f, stateMachine = machine });
        }

        static AnimatorStateMachine NewMachine(AnimatorController fx, string name)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, fx);
            return machine;
        }

        static void Driver(AnimatorState state, IEnumerable<VRC_AvatarParameterDriver.Parameter> parameters)
        {
            var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            driver.parameters.AddRange(parameters);
        }

        static VRC_AvatarParameterDriver.Parameter Copy(string source, string target) => new VRC_AvatarParameterDriver.Parameter {
            name = target, source = source, type = VRC_AvatarParameterDriver.ChangeType.Copy
        };

        static VRC_AvatarParameterDriver.Parameter Set(string target, float value) => new VRC_AvatarParameterDriver.Parameter {
            name = target, value = value, type = VRC_AvatarParameterDriver.ChangeType.Set
        };

        static void Any(AnimatorStateMachine machine, AnimatorState destination, AnimatorConditionMode mode,
            string parameter, bool canTransitionToSelf)
        {
            var transition = machine.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = 0;
            transition.canTransitionToSelf = canTransitionToSelf;
            transition.AddCondition(mode, 0, parameter);
        }

        static void ExitAtEnd(AnimatorState source, AnimatorState destination)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = 1;
            transition.duration = 0;
        }

        static void Condition(AnimatorStateTransition transition, AnimatorConditionMode mode, string parameter)
        {
            transition.hasExitTime = false;
            transition.duration = 0;
            transition.AddCondition(mode, 0, parameter);
        }

        static void AddParameter(AnimatorController fx, string name, AnimatorControllerParameterType type)
        {
            var existing = fx.parameters.FirstOrDefault(parameter => parameter.name == name);
            if (existing != null)
            {
                if (existing.type != type) throw new InvalidOperationException("Animator parameter '" + name + "' has the wrong type.");
                return;
            }
            fx.AddParameter(new AnimatorControllerParameter { name = name, type = type });
        }

        static string UniqueName(AnimatorController fx, string requested)
        {
            var used = new HashSet<string>(fx.parameters.Select(parameter => parameter.name), StringComparer.Ordinal);
            string name = requested;
            for (int suffix = 2; used.Contains(name); suffix++) name = requested + "_" + suffix;
            return name;
        }

        static void RequireParameter(AnimatorController fx, string name, AnimatorControllerParameterType type, bool add)
        {
            var found = fx.parameters.FirstOrDefault(parameter => parameter.name == name);
            if (found != null && found.type != type) throw new InvalidOperationException("Animator parameter '" + name + "' has the wrong type.");
            if (found == null && add) AddParameter(fx, name, type);
            else if (found == null) throw new InvalidOperationException("Animator target parameter '" + name + "' is missing.");
        }

        static void RequireInput(AnimatorController fx, string name, AnimatorControllerParameterType type)
        {
            var found = fx.parameters.FirstOrDefault(parameter => parameter.name == name);
            if (found != null && found.type != type) throw new InvalidOperationException("Animator input parameter '" + name + "' has the wrong type.");
            var builtinTypes = BuiltinTypes(name);
            if (found == null && builtinTypes.Length == 0) throw new InvalidOperationException("Animator input parameter '" + name + "' is missing.");
            if (builtinTypes.Length > 0 && !builtinTypes.Contains(type))
                throw new InvalidOperationException("Built-in input parameter '" + name + "' has an unsupported declared type.");
        }

        static bool IsBuiltin(string name) => BuiltinTypes(name).Length > 0;

        static AnimatorControllerParameterType[] BuiltinTypes(string name) => name switch {
            "GestureLeft" or "GestureRight" or "Viseme" => new[] { AnimatorControllerParameterType.Int, AnimatorControllerParameterType.Float },
            "IsLocal" => new[] { AnimatorControllerParameterType.Bool, AnimatorControllerParameterType.Int, AnimatorControllerParameterType.Float },
            "InStation" or "VRMode" or "MuteSelf" => new[] { AnimatorControllerParameterType.Bool },
            "GestureLeftWeight" or "GestureRightWeight" or "Voice" => new[] { AnimatorControllerParameterType.Float },
            _ => Array.Empty<AnimatorControllerParameterType>()
        };

        static bool ValidType(AnimatorControllerParameterType type) => type == AnimatorControllerParameterType.Bool ||
            type == AnimatorControllerParameterType.Int || type == AnimatorControllerParameterType.Float;

        static string Safe(string value) => string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character == '_' ? character : '_'));
    }
}
