using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using nxclone;

public static class NxCloneExpressionRecordingSmoke
{
    [MenuItem("Tools/nxclone/Run expression recording schema smoke")]
    public static void Run()
    {
        const string folder = "Assets/nxclone-expression-recording-smoke";
        if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-expression-recording-smoke");
        var avatarRoot = new GameObject("nxclone expression recording smoke avatar").transform;
        AnimatorController fx = null;
        try
        {
            fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/expression.controller");
            Add(fx, "record", AnimatorControllerParameterType.Bool);
            Add(fx, "play", AnimatorControllerParameterType.Bool);
            Add(fx, "speed", AnimatorControllerParameterType.Float, 0.5f);
            Add(fx, "customBool", AnimatorControllerParameterType.Bool);
            Add(fx, "customFloat", AnimatorControllerParameterType.Float);
            Add(fx, "GestureLeft", AnimatorControllerParameterType.Float);
            Add(fx, "IsLocal", AnimatorControllerParameterType.Float);
            Add(fx, "Viseme", AnimatorControllerParameterType.Int);
            Add(fx, "nxclone_gesture", AnimatorControllerParameterType.Float);
            Add(fx, "nxclone_local", AnimatorControllerParameterType.Float);
            Add(fx, "nxclone_viseme", AnimatorControllerParameterType.Int);
            Add(fx, "nxclone_gesture_weight", AnimatorControllerParameterType.Float);
            var recordMachine = new AnimatorStateMachine { name = "pose capture" };
            AssetDatabase.AddObjectToAsset(recordMachine, fx);
            foreach (string stateName in new[] { "record take 0", "record take 1" })
            {
                var recordState = recordMachine.AddState(stateName);
                var recordDriver = recordState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                recordDriver.localOnly = true;
                recordDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter {
                    name = "play", type = VRC_AvatarParameterDriver.ChangeType.Set, value = 0
                });
            }
            fx.AddLayer(new AnimatorControllerLayer { name = "pose capture", defaultWeight = 1, stateMachine = recordMachine });

            var recorder = new NxCloneRecording.Result();
            Set(recorder, nameof(NxCloneRecording.Result.RecordParameter), "record");
            Set(recorder, nameof(NxCloneRecording.Result.TakeParameter), "take");
            Set(recorder, nameof(NxCloneRecording.Result.PlayParameter), "play");
            Set(recorder, nameof(NxCloneRecording.Result.SpeedParameter), "speed");
            Set(recorder, nameof(NxCloneRecording.Result.CaptureLayerName), "pose capture");
            Set(recorder, nameof(NxCloneRecording.Result.SampleCount), 3);
            Set(recorder, nameof(NxCloneRecording.Result.DurationSeconds), 3f);

            var maps = new[] {
                new NxCloneExpressionRecording.Mapping("customBool", "customBool", AnimatorControllerParameterType.Bool),
                new NxCloneExpressionRecording.Mapping("GestureLeft", "nxclone_gesture", AnimatorControllerParameterType.Float),
                new NxCloneExpressionRecording.Mapping("IsLocal", "nxclone_local", AnimatorControllerParameterType.Float),
                new NxCloneExpressionRecording.Mapping("Viseme", "nxclone_viseme", AnimatorControllerParameterType.Int),
                new NxCloneExpressionRecording.Mapping("GestureLeftWeight", "nxclone_gesture_weight", AnimatorControllerParameterType.Float),
                new NxCloneExpressionRecording.Mapping("customFloat", "nxclone_float", AnimatorControllerParameterType.Float)
            };
            Add(fx, "nxclone_float", AnimatorControllerParameterType.Float);
            var result = NxCloneExpressionRecording.Configure(fx, recorder, avatarRoot, folder, "nxclone_test", maps);
            var parameters = fx.parameters.ToDictionary(parameter => parameter.name);
            if (parameters["GestureLeft"].type != AnimatorControllerParameterType.Float ||
                parameters["IsLocal"].type != AnimatorControllerParameterType.Float ||
                parameters["Viseme"].type != AnimatorControllerParameterType.Int)
                throw new Exception("Expression recording must retain each effective built-in input type.");
            int parameterCount = fx.parameters.Length;
            int layerCount = fx.layers.Length;
            bool builtinTargetRejected = false;
            try
            {
                NxCloneExpressionRecording.Configure(fx, recorder, avatarRoot, folder, "nxclone_bad_target",
                    new[] { new NxCloneExpressionRecording.Mapping("GestureLeft", "GestureLeft", AnimatorControllerParameterType.Float) });
            }
            catch (InvalidOperationException) { builtinTargetRejected = true; }
            if (!builtinTargetRejected || fx.parameters.Length != parameterCount || fx.layers.Length != layerCount ||
                fx.parameters.Single(parameter => parameter.name == "GestureLeft").type != AnimatorControllerParameterType.Float)
                throw new Exception("Built-in source parameters must never be used as mutable clone targets.");
            if (!parameters.TryGetValue(result.CaptureRequestParameter, out var request) || request.type != AnimatorControllerParameterType.Bool)
                throw new Exception("Private capture request bool is missing.");
            if (result.Buffers.Length != 3 || result.Buffers.Any(row => row.Length != maps.Length))
                throw new Exception("Per-sample expression buffer dimensions are invalid.");
            for (int sample = 0; sample < result.Buffers.Length; sample++)
                for (int item = 0; item < maps.Length; item++)
                    if (!parameters.TryGetValue(result.Buffers[sample][item], out var buffer) || buffer.type != maps[item].Type)
                        throw new Exception("Expression snapshot buffer missing or mistyped.");

            var recordDrivers = new[] { "record take 0", "record take 1" }.Select(name =>
                recordMachine.states.Single(entry => entry.state.name == name).state.behaviours
                    .OfType<VRCAvatarParameterDriver>().Single()).ToArray();
            if (recordDrivers.Any(driver => !driver.parameters.Any(parameter =>
                parameter.name == result.CaptureRequestParameter && parameter.type == VRC_AvatarParameterDriver.ChangeType.Set &&
                Mathf.Approximately(parameter.value, 1))))
                throw new Exception("Both durable take states must request expression capture on entry.");
            if (recordDrivers.Any(driver => !driver.localOnly))
                throw new Exception("Expression capture changed an existing recorder driver's local/remote setting.");
            var capture = FindLayer(fx, result.CaptureLayerName).stateMachine;
            var captureStates = capture.states.Select(entry => entry.state).ToArray();
            var sampleStates = captureStates.Where(state => state.name.StartsWith("sample ", StringComparison.Ordinal)).ToArray();
            if (sampleStates.Length != recorder.SampleCount || !sampleStates.All(state => state.motion == captureStates[1].motion))
                throw new Exception("Capture state sequence does not match pose sample count/timer.");
            var firstDelay = captureStates.Single(state => state.name == "first sample delay");
            var resetRequest = firstDelay.behaviours.OfType<VRCAvatarParameterDriver>().Single().parameters
                .SingleOrDefault(parameter => parameter.name == result.CaptureRequestParameter);
            if (resetRequest == null || resetRequest.type != VRC_AvatarParameterDriver.ChangeType.Set ||
                !Mathf.Approximately(resetRequest.value, 0))
                throw new Exception("Expression capture request must clear after entry so the durable Take phase does not retrigger capture.");
            if (!Mathf.Approximately(firstDelay.motion.averageDuration, 1f) ||
                !Mathf.Approximately(firstDelay.speed, 2f * recorder.SampleCount / recorder.DurationSeconds) ||
                !Mathf.Approximately(sampleStates[0].speed, (float)recorder.SampleCount / recorder.DurationSeconds))
                throw new Exception("Capture timing is not aligned to the recorder sample cadence.");
            for (int sample = 0; sample < sampleStates.Length; sample++)
            {
                var copy = sampleStates[sample].behaviours.OfType<VRCAvatarParameterDriver>().Single();
                if (copy.localOnly || copy.parameters.Count != maps.Length ||
                    copy.parameters.Any(parameter => parameter.type != VRC_AvatarParameterDriver.ChangeType.Copy))
                    throw new Exception("Capture sample must copy each live input into an unsynced buffer.");
                for (int item = 0; item < maps.Length; item++)
                    if (!copy.parameters.Any(parameter => parameter.name == result.Buffers[sample][item] && parameter.source == maps[item].Input))
                        throw new Exception("Capture Copy driver has incorrect source or buffer target.");
            }
            float interval = recorder.DurationSeconds / recorder.SampleCount;
            float firstSampleAt = 1f / firstDelay.speed;
            float lastSampleAt = firstSampleAt + (sampleStates.Length - 1) * interval;
            if (!Mathf.Approximately(sampleStates[sampleStates.Length - 1].speed, 2f * recorder.SampleCount / recorder.DurationSeconds) ||
                !Mathf.Approximately(firstSampleAt, interval * 0.5f) ||
                !Mathf.Approximately(lastSampleAt, recorder.DurationSeconds - interval * 0.5f))
                throw new Exception("Capture must sample the first and last half-intervals, placing the final snapshot half an interval before duration.");

            var playback = FindLayer(fx, result.PlaybackLayerName).stateMachine;
            var playStates = playback.states.Select(entry => entry.state).ToArray();
            var playbackSamples = playStates.Where(state => state.name.StartsWith("sample ", StringComparison.Ordinal)).ToArray();
            if (playbackSamples.Length != recorder.SampleCount || playback.defaultState.name != "idle")
                throw new Exception("Playback layer must default to live idle and cycle over all snapshots.");
            if (!playbackSamples.All(state => state.speedParameterActive && state.speedParameter == "speed" &&
                Mathf.Approximately(state.speed, 2f * recorder.SampleCount / recorder.DurationSeconds)))
                throw new Exception("Discrete expression playback speed is not coupled to the pose recorder speed control.");
            if (playbackSamples.Any(state => state.behaviours.OfType<VRCAvatarParameterDriver>().Single().localOnly))
                throw new Exception("Playback aliases must update on each observer receiving the synced Play parameter.");
            var restoreState = playStates.Single(state => state.name == "restore live values");
            foreach (var sample in playbackSamples)
            {
                bool stopsOnCapture = sample.transitions.Any(transition => transition.destinationState == restoreState &&
                    transition.conditions.Any(condition => condition.mode == AnimatorConditionMode.If &&
                        condition.parameter == result.CaptureRequestParameter));
                bool usesShortRecordRequest = sample.transitions.Any(transition => transition.destinationState == restoreState &&
                    transition.conditions.Any(condition => condition.parameter == recorder.RecordParameter));
                if (!stopsOnCapture || usesShortRecordRequest)
                    throw new Exception("Expression playback must stop on the private capture-entry pulse, not the short Record button request.");
            }

            // This is an explicit mirror of the serialized SDK Copy operations, not SDK driver execution.
            var values = new Dictionary<string, float> {
                ["customBool"] = 1, ["GestureLeft"] = 4, ["IsLocal"] = 1, ["Viseme"] = 7,
                ["GestureLeftWeight"] = 0.6f, ["customFloat"] = 0.75f
            };
            for (int sample = 0; sample < sampleStates.Length; sample++)
                ApplyCopies(sampleStates[sample], values);
            ApplyCopies(playbackSamples[2], values);
            if (!Mathf.Approximately(values["customBool"], 1) || !Mathf.Approximately(values["nxclone_gesture"], 4) ||
                !Mathf.Approximately(values["nxclone_local"], 1) ||
                !Mathf.Approximately(values["nxclone_gesture_weight"], 0.6f) ||
                !Mathf.Approximately(values["nxclone_viseme"], 7) || !Mathf.Approximately(values["nxclone_float"], 0.75f))
                throw new Exception("Manual Copy mirror failed to restore the final captured snapshot.");

            var cache = playStates.Single(state => state.name == "cache live values");
            var cacheParameters = cache.behaviours.OfType<VRCAvatarParameterDriver>().Single().parameters;
            if (!cacheParameters.Any(parameter => parameter.source == "customBool" && parameter.name.StartsWith("nxclone_test_restore_")))
                throw new Exception("Same-name custom menu input/output must be cached before playback.");
            var restore = playStates.Single(state => state.name == "restore live values");
            var restoreParameters = restore.behaviours.OfType<VRCAvatarParameterDriver>().Single().parameters;
            if (!restoreParameters.Any(parameter => parameter.source.StartsWith("nxclone_test_restore_", StringComparison.Ordinal) && parameter.name == "customBool") ||
                !restoreParameters.Any(parameter => parameter.source == "GestureLeft" && parameter.name == "nxclone_gesture") ||
                !restoreParameters.Any(parameter => parameter.source == "IsLocal" && parameter.name == "nxclone_local") ||
                !restoreParameters.Any(parameter => parameter.source == "GestureLeftWeight" && parameter.name == "nxclone_gesture_weight"))
                throw new Exception("Stopping playback must restore cached same-name values and current live builtin inputs.");

            Debug.Log("NXCLONE_EXPRESSION_RECORDING_SCHEMA_SMOKE_OK (manual Copy mirror only; SDK drivers are not executed in Editor)");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
        finally
        {
            if (avatarRoot) UnityEngine.Object.DestroyImmediate(avatarRoot.gameObject);
            AssetDatabase.DeleteAsset(folder);
            AssetDatabase.Refresh();
        }
    }

    static AnimatorControllerLayer FindLayer(AnimatorController controller, string name) =>
        controller.layers.Single(layer => layer.name == name);

    static void ApplyCopies(AnimatorState state, Dictionary<string, float> values)
    {
        foreach (var parameter in state.behaviours.OfType<VRCAvatarParameterDriver>().SelectMany(driver => driver.parameters))
            if (parameter.type == VRC_AvatarParameterDriver.ChangeType.Copy &&
                values.TryGetValue(parameter.source, out var value)) values[parameter.name] = value;
    }

    static void Add(AnimatorController controller, string name, AnimatorControllerParameterType type, float defaultFloat = 0)
    {
        if (controller.parameters.All(parameter => parameter.name != name))
            controller.AddParameter(new AnimatorControllerParameter { name = name, type = type, defaultFloat = defaultFloat });
    }

    static void Set(NxCloneRecording.Result result, string property, object value) =>
        typeof(NxCloneRecording.Result).GetProperty(property).GetSetMethod(true).Invoke(result, new[] { value });
}
