using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace nxclone
{
    /// <summary>Merges a separate avatar's FX layer and controls into a clone's root avatar.</summary>
    /// <remarks>
    /// The returned state-lock toggle gates transitions in copied external-source controllers while leaving
    /// their current motion running. It cannot lock transitions in a root-source clone whose FX is only mirrored.
    /// </remarks>
    public static class NxCloneSourceFx
    {
        public sealed class Result
        {
            public VRCExpressionsMenu menu;
            public VRCExpressionParameters.Parameter[] parameters;
            public IReadOnlyDictionary<string, string> parameterNames;
            public IReadOnlyDictionary<string, AnimatorControllerParameterType> parameterTypes;
            public NxCloneExpressionRecording.Mapping[] expressionInputMappings;
            public int syncedParameterCost;
            public string stateLockParameterName;
        }

        static readonly HashSet<string> BuiltInParameters = new HashSet<string>(StringComparer.Ordinal)
        {
            "IsLocal", "InStation", "AvatarVersion", "GestureLeft", "GestureRight",
            "GestureLeftWeight", "GestureRightWeight", "Viseme", "AngularY", "VelocityX",
            "VelocityY", "VelocityZ", "Upright", "Grounded", "Seated", "AFK", "TrackingType",
            "VRMode", "MuteSelf", "InVR", "Voice", "PreviewMode", "VelocityMagnitude",
            "ScaleModified", "ScaleFactor", "ScaleFactorInverse",
            "EyeHeightAsMeters", "EyeHeightAsPercent"
        };

        static readonly string[] ExpressionInputNames = {
            "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight", "Viseme", "Voice"
        };

        public static Result Merge(
            AnimatorController targetFx,
            AnimatorController sourceFx,
            Transform sourceRoot,
            Transform targetAvatarRoot,
            Transform cloneRoot,
            int cloneIndex,
            VRCExpressionParameters sourceParameters,
            VRCExpressionsMenu sourceMenu,
            VRCExpressionParameters existingRootParameters,
            string generatedFolder,
            bool includeStateLock = true,
            bool deferParameterBudgetCheck = false,
            bool isolateExpressionInputs = false,
            bool excludeGoGoLoco = false)
        {
            if (!targetFx || !sourceFx || !sourceRoot || !targetAvatarRoot || !cloneRoot)
                throw new ArgumentNullException("FX merge requires both controllers and all three avatar roots.");
            if (cloneIndex < 1) throw new ArgumentOutOfRangeException(nameof(cloneIndex));
            if (!AssetDatabase.IsValidFolder(generatedFolder))
                throw new ArgumentException("Generated folder must exist in the AssetDatabase.", nameof(generatedFolder));

            string targetPath = AssetDatabase.GetAssetPath(targetFx);
            string sourcePath = AssetDatabase.GetAssetPath(sourceFx);
            if (string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(sourcePath) || targetPath == sourcePath ||
                AssetDatabase.IsSubAsset(targetFx))
                throw new InvalidOperationException("Root FX must be a separate copied controller asset; source FX must be an asset controller.");
            bool rootSource = sourceRoot == targetAvatarRoot;
            if (sourceRoot == cloneRoot || (!rootSource && cloneRoot.IsChildOf(sourceRoot)) || !cloneRoot.IsChildOf(targetAvatarRoot))
                throw new InvalidOperationException("Source root and generated clone root must be separate hierarchies.");

            var sourceExpressionParameters = sourceParameters && sourceParameters.parameters != null
                ? sourceParameters.parameters
                : Array.Empty<VRCExpressionParameters.Parameter>();
            string lockSourceName = includeStateLock ? UniqueLockSourceName(sourceFx, sourceExpressionParameters) : null;
            var maps = BuildParameterMap(targetFx, sourceFx, sourceExpressionParameters, existingRootParameters,
                cloneIndex, lockSourceName, isolateExpressionInputs);
            var parameterTypes = BuildParameterTypes(targetFx, sourceFx, sourceExpressionParameters, maps.names, isolateExpressionInputs);
            var inputMappings = isolateExpressionInputs
                ? ExpressionInputNames.Select(name => new NxCloneExpressionRecording.Mapping(name, maps.names[name],
                    ExpressionInputType(targetFx, sourceFx, name))).ToArray()
                : Array.Empty<NxCloneExpressionRecording.Mapping>();
            var copiedExpressionParameters = CopyExpressionParameters(sourceExpressionParameters, maps.names);
            if (includeStateLock)
                copiedExpressionParameters = copiedExpressionParameters.Concat(new[] { new VRCExpressionParameters.Parameter {
                    name = maps.names[lockSourceName], valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0, saved = false, networkSynced = true
                } }).ToArray();
            int addedCost = copiedExpressionParameters.Where(p => p.networkSynced).Sum(Cost);
            int currentCost = existingRootParameters ? existingRootParameters.CalcTotalCost() : 0;
            if (!deferParameterBudgetCheck && currentCost + addedCost > VRCExpressionParameters.MAX_PARAMETER_COST)
                throw new InvalidOperationException($"Clone {cloneIndex} FX needs {addedCost} additional synced parameter bits; root uses {currentCost}/{VRCExpressionParameters.MAX_PARAMETER_COST}.");
            ValidateMenu(sourceMenu, maps.names);

            var generatedAssets = new List<string>();
            AnimatorController sourceCopy = null;
            try
            {
                string copiedPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{generatedFolder}/clone-{cloneIndex}-source-fx.controller");
                if (!AssetDatabase.CopyAsset(sourcePath, copiedPath))
                    throw new InvalidOperationException("Could not copy source FX controller into generated assets.");
                generatedAssets.Add(copiedPath);
                AssetDatabase.ImportAsset(copiedPath, ImportAssetOptions.ForceUpdate);
                sourceCopy = AssetDatabase.LoadAssetAtPath<AnimatorController>(copiedPath);
                if (!sourceCopy) throw new InvalidOperationException("Copied source FX controller could not be loaded.");
                CopyExternalAnimatorObjects(sourceCopy);
                var filteredLayers = sourceCopy.layers;
                if (excludeGoGoLoco) for (int i = 0; i < filteredLayers.Length; i++)
                {
                    var layer = filteredLayers[i];
                    string name = System.Text.RegularExpressions.Regex.Replace(layer.name, @"^\[VF\d+\]\s*", "");
                    if (!name.StartsWith("Go/", StringComparison.Ordinal) && !NxCloneMenuFilter.IsGoGoLocoName(name)) continue;
                    // Empty placeholders preserve layer-control and synced-layer indices.
                    var empty = new AnimatorStateMachine { name = "nxclone excluded GoGo Loco" };
                    AssetDatabase.AddObjectToAsset(empty, sourceCopy);
                    layer.stateMachine = empty;
                    layer.defaultWeight = 0f;
                    layer.syncedLayerIndex = -1;
                    filteredLayers[i] = layer;
                }
                sourceCopy.layers = filteredLayers;
                int sourceLayerCount = sourceCopy.layers.Length;
                int firstCloneLayer = targetFx.layers.Length;

                var clips = new Dictionary<AnimationClip, AnimationClip>();
                var trees = new Dictionary<BlendTree, BlendTree>();
                var visitedTrees = new HashSet<BlendTree>();
                var visitedMachines = new HashSet<AnimatorStateMachine>();
                foreach (var layer in sourceCopy.layers)
                {
                    if (layer.syncedLayerIndex < 0 && !layer.stateMachine)
                        throw new InvalidOperationException($"Clone {cloneIndex} FX layer '{layer.name}' has no state machine.");
                    if (!layer.stateMachine) continue;
                    RewriteStateMachine(layer.stateMachine, maps.names, includeStateLock ? maps.names[lockSourceName] : null,
                        sourceFx, sourceRoot, firstCloneLayer, sourceLayerCount, targetAvatarRoot, cloneRoot,
                        generatedFolder, generatedAssets, clips, trees, visitedTrees, visitedMachines);
                }

                var copiedMenu = sourceMenu
                    ? CopyMenu(sourceMenu, maps.names, generatedFolder, generatedAssets, new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>())
                    : null;
                if (includeStateLock)
                    copiedMenu = AddStateLockControl(copiedMenu, maps.names[lockSourceName], generatedFolder, generatedAssets);

                var originalParameters = targetFx.parameters;
                var originalLayers = targetFx.layers;
                try
                {
                    var newParameters = targetFx.parameters.ToList();
                    foreach (var parameter in SourceControllerParameters(sourceFx, sourceExpressionParameters))
                    {
                        if (IsBuiltIn(parameter.name) || !maps.names.TryGetValue(parameter.name, out var mapped)) continue;
                        if (newParameters.Any(p => p.name == mapped)) continue;
                        newParameters.Add(CloneControllerParameter(parameter, mapped));
                    }
                    foreach (var mapping in inputMappings)
                        if (!newParameters.Any(p => p.name == mapping.Target))
                            newParameters.Add(new AnimatorControllerParameter { name = mapping.Target, type = mapping.Type });
                    if (includeStateLock)
                        newParameters.Add(new AnimatorControllerParameter {
                            name = maps.names[lockSourceName], type = AnimatorControllerParameterType.Bool, defaultBool = false
                        });
                    targetFx.parameters = newParameters.ToArray();

                    var newLayers = targetFx.layers.ToList();
                    var sourceLayers = sourceCopy.layers;
                    for (int i = 0; i < sourceLayers.Length; i++)
                    {
                        var layer = sourceLayers[i];
                        newLayers.Add(new AnimatorControllerLayer
                        {
                            name = $"nxclone {cloneIndex} {layer.name}",
                            stateMachine = layer.stateMachine,
                            avatarMask = CopyMask(layer.avatarMask, targetAvatarRoot, cloneRoot, generatedFolder, generatedAssets),
                            blendingMode = layer.blendingMode,
                            defaultWeight = layer.stateMachine && layer.stateMachine.name == "nxclone excluded GoGo Loco"
                                ? 0f : i == 0 && layer.syncedLayerIndex < 0 ? 1f : layer.defaultWeight,
                            iKPass = layer.iKPass,
                            syncedLayerAffectsTiming = layer.syncedLayerAffectsTiming,
                            syncedLayerIndex = layer.syncedLayerIndex < 0 ? -1 : firstCloneLayer + layer.syncedLayerIndex
                        });
                    }
                    targetFx.layers = newLayers.ToArray();
                    EditorUtility.SetDirty(targetFx);
                    AssetDatabase.SaveAssets();
                }
                catch
                {
                    targetFx.parameters = originalParameters;
                    targetFx.layers = originalLayers;
                    EditorUtility.SetDirty(targetFx);
                    throw;
                }

                foreach (var receiver in cloneRoot.GetComponentsInChildren<Component>(true)
                    .Where(component => component && component.GetType().FullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver"))
                {
                    var original = Resolve(sourceRoot, AnimationUtility.CalculateTransformPath(receiver.transform, cloneRoot));
                    if (!original || !original.GetComponent(receiver.GetType())) continue;
                    var serialized = new SerializedObject(receiver);
                    var parameter = serialized.FindProperty("parameter");
                    if (parameter != null && parameter.propertyType == SerializedPropertyType.String)
                    {
                        // Unconsumed contact outputs must not write back into the main avatar.
                        parameter.stringValue = maps.names.TryGetValue(parameter.stringValue, out var alias) ? alias : "";
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                return new Result
                {
                    menu = copiedMenu,
                    parameters = copiedExpressionParameters,
                    parameterNames = maps.names,
                    parameterTypes = parameterTypes,
                    expressionInputMappings = inputMappings,
                    syncedParameterCost = addedCost,
                    stateLockParameterName = includeStateLock ? maps.names[lockSourceName] : null
                };
            }
            catch
            {
                foreach (var path in generatedAssets.AsEnumerable().Reverse())
                    if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        // CopyAsset only duplicates objects inside that asset file. Baked controllers may
        // reference states, transitions and behaviours owned by other controller files.
        public static void CopyExternalAnimatorObjects(AnimatorController controller)
        {
            string path = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Controller must be a saved asset.");
            var copies = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            UnityEngine.Object Own(UnityEngine.Object original)
            {
                if (!original || !(original is AnimatorStateMachine || original is AnimatorState ||
                    original is AnimatorTransitionBase || original is StateMachineBehaviour)) return original;
                if (copies.TryGetValue(original, out var existing)) return existing;
                var copy = original;
                if (AssetDatabase.GetAssetPath(original) != path)
                {
                    copy = UnityEngine.Object.Instantiate(original);
                    copy.name = original.name;
                    AssetDatabase.AddObjectToAsset(copy, controller);
                }
                copies.Add(original, copy);
                var serialized = new SerializedObject(copy);
                var iterator = serialized.GetIterator();
                while (iterator.Next(true))
                {
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var reference = iterator.objectReferenceValue;
                    var owned = Own(reference);
                    if (owned != reference) iterator.objectReferenceValue = owned;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(copy);
                return copy;
            }
            var layers = controller.layers;
            foreach (var layer in layers) layer.stateMachine = (AnimatorStateMachine)Own(layer.stateMachine);
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
        }

        public static void AddExpressionInputCopyLayer(AnimatorController targetFx, Result merge,
            Transform avatarRoot, string generatedFolder, string recordingPlayParameter = null)
        {
            if (!targetFx || merge == null || !avatarRoot || !AssetDatabase.IsValidFolder(generatedFolder))
                throw new ArgumentException("Live expression input copying needs a merged FX controller, avatar root, and generated folder.");
            var mappings = merge.expressionInputMappings ?? Array.Empty<NxCloneExpressionRecording.Mapping>();
            if (mappings.Length == 0) return;
            var parameters = targetFx.parameters.ToDictionary(p => p.name, StringComparer.Ordinal);
            var controllerParameters = targetFx.parameters.ToList();
            foreach (var mapping in mappings)
            {
                if (!parameters.TryGetValue(mapping.Target, out var target) || target.type != mapping.Type)
                    throw new InvalidOperationException($"Expression input alias '{mapping.Target}' is missing or has the wrong type.");
                if (parameters.TryGetValue(mapping.Input, out var input) && input.type != mapping.Type &&
                    !(Numeric(input.type) && Numeric(mapping.Type)))
                    throw new InvalidOperationException($"Built-in expression input '{mapping.Input}' has the wrong type.");
                if (input == null)
                {
                    input = new AnimatorControllerParameter { name = mapping.Input, type = mapping.Type };
                    parameters.Add(mapping.Input, input);
                    controllerParameters.Add(input);
                }
            }
            if (!string.IsNullOrEmpty(recordingPlayParameter) &&
                (!parameters.TryGetValue(recordingPlayParameter, out var gate) || gate.type != AnimatorControllerParameterType.Bool))
                throw new InvalidOperationException("Expression-copy gate must be a declared bool recording Play parameter.");

            string layerName = "nxclone expression input copy " + mappings[0].Target;
            var timerRoot = new GameObject(layerName + " timer");
            timerRoot.hideFlags = HideFlags.HideInHierarchy;
            timerRoot.transform.SetParent(avatarRoot, false);
            string timerTransformPath = AnimationUtility.CalculateTransformPath(timerRoot.transform, avatarRoot);
            var timer = new AnimationClip { name = layerName + " timer", frameRate = 60f };
            AnimationUtility.SetEditorCurve(timer,
                EditorCurveBinding.FloatCurve(timerTransformPath, typeof(Transform), "m_LocalScale.x"),
                new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1)));
            var timerSettings = AnimationUtility.GetAnimationClipSettings(timer);
            timerSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(timer, timerSettings);
            string timerPath = AssetDatabase.GenerateUniqueAssetPath(generatedFolder + "/" + layerName.Replace('/', '_') + "-timer.anim");
            AssetDatabase.CreateAsset(timer, timerPath);
            var machine = new AnimatorStateMachine { name = layerName };
            AssetDatabase.AddObjectToAsset(machine, targetFx);
            var copy = machine.AddState("copy live inputs");
            copy.motion = timer;
            copy.speed = 60f;
            copy.writeDefaultValues = false;
            var alternate = machine.AddState("copy live inputs alternate");
            alternate.motion = timer;
            alternate.speed = 60f;
            alternate.writeDefaultValues = false;
            AddInputCopyDriver(copy, mappings);
            AddInputCopyDriver(alternate, mappings);
            Timed(copy, alternate);
            Timed(alternate, copy);
            if (string.IsNullOrEmpty(recordingPlayParameter)) machine.defaultState = copy;
            else
            {
                var idle = machine.AddState("paused for expression playback");
                idle.writeDefaultValues = false;
                machine.defaultState = idle;
                var start = idle.AddTransition(copy);
                start.hasExitTime = false; start.duration = 0;
                start.AddCondition(AnimatorConditionMode.IfNot, 0, recordingPlayParameter);
                var pause = copy.AddTransition(idle);
                pause.hasExitTime = false; pause.duration = 0;
                pause.AddCondition(AnimatorConditionMode.If, 0, recordingPlayParameter);
                var pauseState = alternate.AddTransition(idle);
                pauseState.hasExitTime = false; pauseState.duration = 0;
                pauseState.AddCondition(AnimatorConditionMode.If, 0, recordingPlayParameter);
            }
            targetFx.AddLayer(new AnimatorControllerLayer { name = layerName, defaultWeight = 1f, stateMachine = machine });
            targetFx.parameters = controllerParameters.ToArray();
            EditorUtility.SetDirty(targetFx);
            AssetDatabase.SaveAssets();
        }

        static void AddInputCopyDriver(AnimatorState state, NxCloneExpressionRecording.Mapping[] mappings)
        {
            var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            foreach (var mapping in mappings)
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter {
                    name = mapping.Target, source = mapping.Input, type = VRC_AvatarParameterDriver.ChangeType.Copy
                });
        }

        static void Timed(AnimatorState source, AnimatorState destination)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = true; transition.exitTime = 1; transition.duration = 0;
        }

        static (Dictionary<string, string> names, string prefix) BuildParameterMap(
            AnimatorController targetFx, AnimatorController sourceFx,
            VRCExpressionParameters.Parameter[] expressionParameters,
            VRCExpressionParameters existingRootParameters, int cloneIndex, string generatedParameterName,
            bool isolateExpressionInputs)
        {
            var sourceNames = SourceControllerParameters(sourceFx, expressionParameters).Select(p => p.name)
                .Concat(expressionParameters.Select(p => p.name)).Append(generatedParameterName)
                .Concat(isolateExpressionInputs ? ExpressionInputNames : Array.Empty<string>())
                .Where(n => !string.IsNullOrEmpty(n) && (!IsBuiltIn(n) || isolateExpressionInputs && ExpressionInputNames.Contains(n, StringComparer.Ordinal)))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (sourceNames.Length != sourceNames.Distinct(StringComparer.Ordinal).Count())
                throw new InvalidOperationException("Source FX contains duplicate custom parameter names.");

            var used = new HashSet<string>(targetFx.parameters.Select(p => p.name), StringComparer.Ordinal);
            if (existingRootParameters && existingRootParameters.parameters != null)
                foreach (var p in existingRootParameters.parameters) used.Add(p.name);
            string basePrefix = $"nxclone_clone{cloneIndex}_";
            string prefix = basePrefix;
            for (int suffix = 2; sourceNames.Any(n => used.Contains(prefix + n)); suffix++)
                prefix = $"nxclone_clone{cloneIndex}_{suffix}_";

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in sourceNames) result.Add(name, prefix + name);
            return (result, prefix);
        }

        static Dictionary<string, AnimatorControllerParameterType> BuildParameterTypes(
            AnimatorController targetFx, AnimatorController sourceFx, VRCExpressionParameters.Parameter[] expressions,
            IReadOnlyDictionary<string, string> names, bool isolateExpressionInputs)
        {
            var result = SourceControllerParameters(sourceFx, expressions).ToDictionary(p => p.name, p => p.type, StringComparer.Ordinal);
            if (isolateExpressionInputs)
                foreach (string name in ExpressionInputNames) result[name] = ExpressionInputType(targetFx, sourceFx, name);
            return result;
        }

        static string UniqueLockSourceName(AnimatorController sourceFx, VRCExpressionParameters.Parameter[] parameters)
        {
            var used = new HashSet<string>(SourceControllerParameters(sourceFx, parameters).Select(p => p.name), StringComparer.Ordinal);
            used.UnionWith(parameters.Select(p => p.name));
            string name = "nxclone_fx_state_lock";
            for (int suffix = 2; used.Contains(name); suffix++) name = $"nxclone_fx_state_lock_{suffix}";
            return name;
        }

        static VRCExpressionsMenu AddStateLockControl(VRCExpressionsMenu menu, string parameter,
            string folder, List<string> generatedAssets)
        {
            var toggle = new VRCExpressionsMenu.Control {
                name = "Freeze FX transitions", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 1
            };
            if (!menu)
            {
                menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                menu.name = "Clone FX controls";
                menu.controls = new List<VRCExpressionsMenu.Control>();
            }
            menu.controls = menu.controls ?? new List<VRCExpressionsMenu.Control>();
            if (menu.controls.Count < 8)
            {
                menu.controls.Add(toggle);
                return menu;
            }
            var wrapper = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            wrapper.name = menu.name + " controls";
            wrapper.controls = new List<VRCExpressionsMenu.Control> {
                new VRCExpressionsMenu.Control {
                    name = "Expressions", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu
                },
                toggle
            };
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/clone-fx-controls.asset");
            AssetDatabase.CreateAsset(wrapper, path);
            generatedAssets.Add(path);
            return wrapper;
        }

        static IEnumerable<AnimatorControllerParameter> SourceControllerParameters(
            AnimatorController sourceFx, VRCExpressionParameters.Parameter[] expressionParameters)
        {
            var parameters = sourceFx.parameters.ToDictionary(p => p.name, StringComparer.Ordinal);
            foreach (var expression in expressionParameters)
            {
                if (string.IsNullOrEmpty(expression.name) || IsBuiltIn(expression.name)) continue;
                var expected = ToControllerType(expression.valueType);
                if (parameters.TryGetValue(expression.name, out var existing))
                {
                    // VRChat converts between numeric expression and Animator types at runtime.
                    // Keep the controller's type so its transitions and blend trees remain valid.
                    if (existing.type == AnimatorControllerParameterType.Trigger)
                        throw new InvalidOperationException($"Source parameter '{expression.name}' uses Trigger in FX. Use Bool, Int or Float for expression controls.");
                    continue;
                }
                parameters.Add(expression.name, new AnimatorControllerParameter
                {
                    name = expression.name,
                    type = expected,
                    defaultFloat = expected == AnimatorControllerParameterType.Float ? expression.defaultValue : 0,
                    defaultInt = expected == AnimatorControllerParameterType.Int ? Mathf.RoundToInt(expression.defaultValue) : 0,
                    defaultBool = expected == AnimatorControllerParameterType.Bool && expression.defaultValue != 0
                });
            }
            return parameters.Values;
        }

        static AnimatorControllerParameter CloneControllerParameter(AnimatorControllerParameter source, string name) => new AnimatorControllerParameter
        {
            name = name,
            type = source.type,
            defaultBool = source.defaultBool,
            defaultInt = source.defaultInt,
            defaultFloat = source.defaultFloat
        };

        static VRCExpressionParameters.Parameter[] CopyExpressionParameters(
            VRCExpressionParameters.Parameter[] source, IReadOnlyDictionary<string, string> names)
        {
            var result = new List<VRCExpressionParameters.Parameter>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in source)
            {
                if (string.IsNullOrEmpty(parameter.name))
                    throw new InvalidOperationException("Source expression parameters include an empty name.");
                if (IsBuiltIn(parameter.name))
                    throw new InvalidOperationException($"Source expression parameters incorrectly redeclare VRChat built-in '{parameter.name}'.");
                if (!seen.Add(parameter.name))
                    throw new InvalidOperationException($"Source expression parameters repeat '{parameter.name}'.");
                if (!names.TryGetValue(parameter.name, out var mapped))
                    throw new InvalidOperationException($"Source expression parameter '{parameter.name}' has no safe clone name.");
                result.Add(new VRCExpressionParameters.Parameter
                {
                    name = mapped,
                    valueType = parameter.valueType,
                    defaultValue = parameter.defaultValue,
                    saved = parameter.saved,
                    networkSynced = parameter.networkSynced
                });
            }
            return result.ToArray();
        }

        static int Cost(VRCExpressionParameters.Parameter parameter) => !parameter.networkSynced ? 0 :
            parameter.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8;

        static AnimatorControllerParameterType ToControllerType(VRCExpressionParameters.ValueType valueType) =>
            valueType == VRCExpressionParameters.ValueType.Bool ? AnimatorControllerParameterType.Bool :
            valueType == VRCExpressionParameters.ValueType.Int ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Float;

        static bool IsBuiltIn(string name) => BuiltInParameters.Contains(name);

        static AnimatorControllerParameterType? BuiltinType(string name) => name switch {
            "GestureLeft" or "GestureRight" or "Viseme" => AnimatorControllerParameterType.Int,
            "GestureLeftWeight" or "GestureRightWeight" or "Voice" => AnimatorControllerParameterType.Float,
            _ => null
        };

        static AnimatorControllerParameterType ExpressionInputType(AnimatorController targetFx, AnimatorController sourceFx, string name)
        {
            var expected = BuiltinType(name);
            if (!expected.HasValue) throw new ArgumentException("Unsupported expression input " + name, nameof(name));
            var declared = sourceFx.parameters.FirstOrDefault(parameter => parameter.name == name);
            if (declared != null)
            {
                if (!Numeric(declared.type)) throw new InvalidOperationException($"Source built-in '{name}' has incompatible FX type '{declared.type}'.");
                return declared.type;
            }
            var rootDeclared = targetFx.parameters.FirstOrDefault(parameter => parameter.name == name);
            if (rootDeclared != null)
            {
                if (!Numeric(rootDeclared.type)) throw new InvalidOperationException($"Root built-in '{name}' has incompatible FX type '{rootDeclared.type}'.");
                return rootDeclared.type;
            }
            return expected.Value;
        }

        static bool Numeric(AnimatorControllerParameterType type) =>
            type == AnimatorControllerParameterType.Int || type == AnimatorControllerParameterType.Float;

        static void ValidateMenu(VRCExpressionsMenu menu, IReadOnlyDictionary<string, string> names)
        {
            if (!menu) return;
            ValidateMenu(menu, names, new HashSet<VRCExpressionsMenu>(), new HashSet<VRCExpressionsMenu>());
        }

        static void ValidateMenu(VRCExpressionsMenu menu, IReadOnlyDictionary<string, string> names,
            HashSet<VRCExpressionsMenu> visited, HashSet<VRCExpressionsMenu> active)
        {
            if (!menu) return;
            if (active.Contains(menu))
                throw new InvalidOperationException($"Source menu '{menu.name}' contains a submenu cycle.");
            if (!visited.Add(menu)) return;
            active.Add(menu);
            if (menu.controls == null) { active.Remove(menu); return; }
            foreach (var control in menu.controls)
            {
                ValidateMenuParameter(control.parameter, names);
                if (control.subParameters != null)
                    foreach (var parameter in control.subParameters) ValidateMenuParameter(parameter, names);
                ValidateMenu(control.subMenu, names, visited, active);
            }
            active.Remove(menu);
        }

        static void ValidateMenuParameter(VRCExpressionsMenu.Control.Parameter parameter, IReadOnlyDictionary<string, string> names)
        {
            if (parameter == null || string.IsNullOrEmpty(parameter.name) || IsBuiltIn(parameter.name)) return;
            if (!names.ContainsKey(parameter.name))
                throw new InvalidOperationException($"Source menu references '{parameter.name}', but source FX and expression parameters do not define it.");
        }

        static VRCExpressionsMenu CopyMenu(VRCExpressionsMenu source, IReadOnlyDictionary<string, string> names,
            string folder, List<string> generatedAssets, Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> copies)
        {
            if (!source) return null;
            if (copies.TryGetValue(source, out var existing)) return existing;
            var copy = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            EditorUtility.CopySerialized(source, copy);
            copy.name = source.name + " (nxclone)";
            copies.Add(source, copy);
            foreach (var control in copy.controls ?? new List<VRCExpressionsMenu.Control>())
            {
                RemapMenuParameter(control.parameter, names);
                if (control.subParameters != null)
                    foreach (var parameter in control.subParameters) RemapMenuParameter(parameter, names);
                control.subMenu = CopyMenu(control.subMenu, names, folder, generatedAssets, copies);
            }
            copy.controls = copy.controls ?? new List<VRCExpressionsMenu.Control>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/clone-menu-{Guid.NewGuid():N}.asset");
            AssetDatabase.CreateAsset(copy, path);
            generatedAssets.Add(path);
            return copy;
        }

        static void RemapMenuParameter(VRCExpressionsMenu.Control.Parameter parameter, IReadOnlyDictionary<string, string> names)
        {
            if (parameter != null && !string.IsNullOrEmpty(parameter.name) && !IsBuiltIn(parameter.name) && names.TryGetValue(parameter.name, out var mapped))
                parameter.name = mapped;
        }

        static void RewriteStateMachine(AnimatorStateMachine machine, IReadOnlyDictionary<string, string> names,
            string stateLockParameter,
            AnimatorController originalSourceFx,
            Transform sourceRoot, int layerOffset, int sourceLayerCount,
            Transform targetAvatarRoot, Transform cloneRoot, string folder,
            List<string> generatedAssets, Dictionary<AnimationClip, AnimationClip> clips,
            Dictionary<BlendTree, BlendTree> trees, HashSet<BlendTree> visitedTrees,
            HashSet<AnimatorStateMachine> visitedMachines)
        {
            if (!machine || !visitedMachines.Add(machine)) return;
            machine.behaviours = CloneSafeBehaviours(machine.behaviours);
            foreach (var behaviour in machine.behaviours)
                RewriteBehaviour(behaviour, names, originalSourceFx, sourceRoot, targetAvatarRoot, cloneRoot, layerOffset, sourceLayerCount);
            foreach (var child in machine.states)
            {
                RewriteState(child.state, names, stateLockParameter, originalSourceFx, sourceRoot, targetAvatarRoot, cloneRoot, layerOffset, sourceLayerCount);
                child.state.motion = CloneMotion(child.state.motion, names, sourceRoot, targetAvatarRoot, cloneRoot,
                    folder, generatedAssets, clips, trees, visitedTrees);
            }
            foreach (var transition in machine.anyStateTransitions) RewriteTransition(transition, names, stateLockParameter);
            foreach (var transition in machine.entryTransitions) RewriteTransition(transition, names, stateLockParameter);
            foreach (var child in machine.stateMachines)
            {
                foreach (var transition in machine.GetStateMachineTransitions(child.stateMachine)) RewriteTransition(transition, names, stateLockParameter);
                RewriteStateMachine(child.stateMachine, names, stateLockParameter, originalSourceFx, sourceRoot, layerOffset, sourceLayerCount,
                    targetAvatarRoot, cloneRoot,
                    folder, generatedAssets, clips, trees, visitedTrees, visitedMachines);
            }
        }

        static void RewriteState(AnimatorState state, IReadOnlyDictionary<string, string> names, string stateLockParameter,
            AnimatorController originalSourceFx, Transform sourceRoot, Transform targetAvatarRoot, Transform cloneRoot, int layerOffset, int sourceLayerCount)
        {
            if (state.speedParameterActive) state.speedParameter = Map(state.speedParameter, names);
            if (state.cycleOffsetParameterActive) state.cycleOffsetParameter = Map(state.cycleOffsetParameter, names);
            if (state.mirrorParameterActive) state.mirrorParameter = Map(state.mirrorParameter, names);
            if (state.timeParameterActive) state.timeParameter = Map(state.timeParameter, names);
            foreach (var transition in state.transitions) RewriteTransition(transition, names, stateLockParameter);
            state.behaviours = CloneSafeBehaviours(state.behaviours);
            foreach (var behaviour in state.behaviours)
                RewriteBehaviour(behaviour, names, originalSourceFx, sourceRoot, targetAvatarRoot, cloneRoot, layerOffset, sourceLayerCount);
        }

        static void RewriteTransition(AnimatorTransitionBase transition, IReadOnlyDictionary<string, string> names, string stateLockParameter)
        {
            if (!transition) return;
            var conditions = transition.conditions;
            for (int i = 0; i < conditions.Length; i++) conditions[i].parameter = Map(conditions[i].parameter, names);
            transition.conditions = conditions;
            if (!string.IsNullOrEmpty(stateLockParameter))
                transition.AddCondition(AnimatorConditionMode.IfNot, 0f, stateLockParameter);
        }

        static AvatarMask CopyMask(AvatarMask source, Transform avatarRoot, Transform cloneRoot,
            string folder, List<string> generatedAssets)
        {
            if (!source) return null;
            var copy = UnityEngine.Object.Instantiate(source);
            string prefix = AnimationUtility.CalculateTransformPath(cloneRoot, avatarRoot);
            for (int i = 0; i < copy.transformCount; i++)
            {
                string path = source.GetTransformPath(i);
                copy.SetTransformPath(i, string.IsNullOrEmpty(path) ? prefix : prefix + "/" + path);
            }
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/clone-fx-mask.asset");
            AssetDatabase.CreateAsset(copy, assetPath);
            generatedAssets.Add(assetPath);
            return copy;
        }

        // These SDK behaviours affect the owning avatar, rather than a clone visual.
        static StateMachineBehaviour[] CloneSafeBehaviours(StateMachineBehaviour[] behaviours) =>
            behaviours.Where(behaviour => behaviour && behaviour.GetType().Name is not
                ("VRCPlayableLayerControl" or "VRCAnimatorTrackingControl" or
                 "VRCAnimatorLocomotionControl" or "VRCAnimatorTemporaryPoseSpace") &&
                (behaviour is not VRCAnimatorLayerControl layer || layer.playable == VRC_AnimatorLayerControl.BlendableLayer.FX)).ToArray();

        static void RewriteBehaviour(StateMachineBehaviour behaviour, IReadOnlyDictionary<string, string> names,
            AnimatorController originalSourceFx, Transform sourceRoot, Transform targetAvatarRoot, Transform cloneRoot, int layerOffset, int sourceLayerCount)
        {
            if (!behaviour) return;
            var serialized = new SerializedObject(behaviour);
            var changes = new List<(string path, string value)>();
            if (behaviour is VRCAnimatorLayerControl layerControl &&
                layerControl.playable == VRC_AnimatorLayerControl.BlendableLayer.FX)
            {
                int layer = layerControl.layer;
                if (layer < 0 || layer >= sourceLayerCount)
                    throw new InvalidOperationException($"Clone FX layer control references source FX layer {layer}, but source FX has {sourceLayerCount} layers.");
                var layerProperty = serialized.FindProperty("layer");
                if (layerProperty == null || layerProperty.propertyType != SerializedPropertyType.Integer &&
                    layerProperty.propertyType != SerializedPropertyType.Enum)
                    throw new InvalidOperationException("VRChat FX layer control has no serialized layer index.");
                layerProperty.intValue = layerOffset + layer;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(behaviour);
            }
            var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType == SerializedPropertyType.String && behaviour.GetType().Name == "VRCAnimatorPlayAudio" &&
                    property.name.Equals("SourcePath", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceAudio = Resolve(sourceRoot, property.stringValue);
                    var cloneAudio = Resolve(cloneRoot, property.stringValue);
                    if (!sourceAudio || !cloneAudio || !cloneAudio.GetComponent<AudioSource>())
                        throw new InvalidOperationException($"Clone audio behaviour cannot find AudioSource at '{property.stringValue}'.");
                    changes.Add((property.propertyPath, AnimationUtility.CalculateTransformPath(cloneAudio, targetAvatarRoot)));
                }
                else if (property.propertyType == SerializedPropertyType.String && names.TryGetValue(property.stringValue, out var mapped))
                {
                    if (!behaviour.GetType().Name.Contains("ParameterDriver", StringComparison.Ordinal) &&
                        !(behaviour.GetType().Name == "VRCAnimatorPlayAudio" && property.name == "ParameterName"))
                        throw new InvalidOperationException($"Clone FX behaviour '{behaviour.GetType().FullName}' stores parameter '{property.stringValue}' in unsupported field '{property.propertyPath}'.");
                    changes.Add((property.propertyPath, mapped));
                }
                else if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue)
                {
                    var referenced = property.objectReferenceValue;
                    if (referenced == behaviour || referenced == originalSourceFx ||
                        referenced is GameObject gameObject && gameObject.transform.IsChildOf(sourceRoot) ||
                        referenced is Component component && component.transform && component.transform.IsChildOf(sourceRoot))
                        throw new InvalidOperationException($"Clone FX behaviour '{behaviour.GetType().FullName}' references source-scene data that cannot be transferred to the generated clone.");
                }
            }
            foreach (var change in changes) serialized.FindProperty(change.path).stringValue = change.value;
            if (changes.Count > 0)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(behaviour);
            }
        }

        static string Map(string parameter, IReadOnlyDictionary<string, string> names) =>
            !string.IsNullOrEmpty(parameter) && names.TryGetValue(parameter, out var mapped) ? mapped : parameter;

        static Motion CloneMotion(Motion motion, IReadOnlyDictionary<string, string> names,
            Transform sourceRoot, Transform targetAvatarRoot, Transform cloneRoot, string folder,
            List<string> generatedAssets, Dictionary<AnimationClip, AnimationClip> clips,
            Dictionary<BlendTree, BlendTree> trees, HashSet<BlendTree> visitedTrees)
        {
            if (motion is AnimationClip clip)
            {
                if (!clips.TryGetValue(clip, out var copy))
                {
                    copy = UnityEngine.Object.Instantiate(clip);
                    copy.name = clip.name + " (nxclone source FX)";
                    string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/clone-{Guid.NewGuid():N}.anim");
                    AssetDatabase.CreateAsset(copy, path);
                    generatedAssets.Add(path);
                    RemapClip(clip, copy, sourceRoot, targetAvatarRoot, cloneRoot, names);
                    clips.Add(clip, copy);
                }
                return copy;
            }
            if (motion is BlendTree tree)
            {
                if (!trees.TryGetValue(tree, out var copy))
                {
                    copy = new BlendTree();
                    EditorUtility.CopySerialized(tree, copy);
                    copy.name = tree.name + " (nxclone source FX)";
                    string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/clone-{Guid.NewGuid():N}.asset");
                    AssetDatabase.CreateAsset(copy, path);
                    generatedAssets.Add(path);
                    trees.Add(tree, copy);
                }
                if (!visitedTrees.Add(tree)) return copy;
                copy.blendParameter = Map(copy.blendParameter, names);
                copy.blendParameterY = Map(copy.blendParameterY, names);
                var children = copy.children;
                for (int i = 0; i < children.Length; i++)
                {
                    // Direct trees use a weight parameter per child, rather than blendParameter.
                    // Leaving these shared lets the main avatar drive the clone's baked toggles.
                    children[i].directBlendParameter = Map(children[i].directBlendParameter, names);
                    children[i].motion = CloneMotion(children[i].motion, names, sourceRoot, targetAvatarRoot, cloneRoot,
                        folder, generatedAssets, clips, trees, visitedTrees);
                }
                copy.children = children;
                EditorUtility.SetDirty(copy);
                return copy;
            }
            if (motion) throw new InvalidOperationException($"Clone FX uses unsupported motion type '{motion.GetType().FullName}'.");
            return null;
        }

        static void RemapClip(AnimationClip source, AnimationClip copy, Transform sourceRoot,
            Transform targetAvatarRoot, Transform cloneRoot, IReadOnlyDictionary<string, string> names)
        {
            var curves = AnimationUtility.GetCurveBindings(source)
                .Select(binding => (binding, curve: AnimationUtility.GetEditorCurve(source, binding))).ToArray();
            var objectCurves = AnimationUtility.GetObjectReferenceCurveBindings(source)
                .Select(binding => (binding, curve: AnimationUtility.GetObjectReferenceCurve(source, binding))).ToArray();
            foreach (var binding in AnimationUtility.GetCurveBindings(copy)) AnimationUtility.SetEditorCurve(copy, binding, null);
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(copy)) AnimationUtility.SetObjectReferenceCurve(copy, binding, null);

            foreach (var item in curves)
            {
                var binding = item.binding;
                if (sourceRoot == targetAvatarRoot && IsGeneratedClonePath(binding.path))
                    throw new InvalidOperationException($"Root-source FX clip '{source.name}' targets generated nxclone path '{binding.path}'.");
                // VRCFury uses this nonexistent object solely to retain clip duration.
                if (binding.path == "__vrcf_length" && binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                {
                    AnimationUtility.SetEditorCurve(copy, binding, item.curve);
                    continue;
                }
                // Animator parameter curves operate on the merged avatar controller.
                if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path) && names.ContainsKey(binding.propertyName))
                {
                    binding.propertyName = Map(binding.propertyName, names);
                    AnimationUtility.SetEditorCurve(copy, binding, item.curve);
                    continue;
                }
                var sourceTarget = Resolve(sourceRoot, binding.path);
                var cloneTarget = Resolve(cloneRoot, binding.path);
                // The SPS bake leaves a build-only config curve alongside its runtime curves.
                if (binding.type.FullName == "VF.Component.VRCFuryHapticPlug" && binding.propertyName == "spsAnimatedEnabled" &&
                    curves.Any(entry => entry.binding.propertyName == "material._SPS_Enabled") &&
                    curves.Any(entry => entry.binding.path.StartsWith(binding.path + "/", StringComparison.Ordinal) &&
                        entry.binding.path.EndsWith("BakedSpsPlug", StringComparison.Ordinal) && entry.binding.propertyName == "m_IsActive"))
                    continue;
                if (!sourceTarget || !cloneTarget || !HasComponent(sourceTarget, binding.type) || !HasComponent(cloneTarget, binding.type))
                    throw UnsupportedCloneTrack(source, binding);
                binding.path = AnimationUtility.CalculateTransformPath(cloneTarget, targetAvatarRoot);
                AnimationUtility.SetEditorCurve(copy, binding, item.curve);
            }
            foreach (var item in objectCurves)
            {
                var binding = item.binding;
                if (sourceRoot == targetAvatarRoot && IsGeneratedClonePath(binding.path))
                    throw new InvalidOperationException($"Root-source FX clip '{source.name}' targets generated nxclone path '{binding.path}'.");
                var sourceTarget = Resolve(sourceRoot, binding.path);
                var cloneTarget = Resolve(cloneRoot, binding.path);
                if (!sourceTarget || !cloneTarget || !HasComponent(sourceTarget, binding.type) || !HasComponent(cloneTarget, binding.type))
                    throw UnsupportedCloneTrack(source, binding);
                binding.path = AnimationUtility.CalculateTransformPath(cloneTarget, targetAvatarRoot);
                var keys = item.curve;
                for (int i = 0; i < keys.Length; i++) keys[i].value = RemapObjectReference(keys[i].value, source, binding, sourceRoot, cloneRoot);
                AnimationUtility.SetObjectReferenceCurve(copy, binding, keys);
            }
            EditorUtility.SetDirty(copy);
        }

        static UnityEngine.Object RemapObjectReference(UnityEngine.Object value, AnimationClip source,
            EditorCurveBinding binding, Transform sourceRoot, Transform cloneRoot)
        {
            if (value is Transform transform && transform.IsChildOf(sourceRoot))
                return Resolve(cloneRoot, AnimationUtility.CalculateTransformPath(transform, sourceRoot).Replace('\\', '/'));
            if (value is GameObject gameObject && gameObject.transform.IsChildOf(sourceRoot))
                return Resolve(cloneRoot, AnimationUtility.CalculateTransformPath(gameObject.transform, sourceRoot).Replace('\\', '/')).gameObject;
            if (value is Component component && component.transform.IsChildOf(sourceRoot))
            {
                var target = Resolve(cloneRoot, AnimationUtility.CalculateTransformPath(component.transform, sourceRoot).Replace('\\', '/'));
                var copy = target ? target.GetComponent(component.GetType()) : null;
                if (!copy) throw UnsupportedCloneTrack(source, binding);
                return copy;
            }
            return value;
        }

        static InvalidOperationException UnsupportedCloneTrack(AnimationClip source, EditorCurveBinding binding) =>
            new InvalidOperationException($"Cannot remap source FX clip '{source.name}' track '{binding.path}:{binding.propertyName}' ({binding.type.FullName}) onto the clone visual. Disable Copy FX animations for this clone, or remove this track from a copied source FX controller. nxclone currently strips this component from clone visuals.");

        static Transform Resolve(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path);

        static bool IsGeneratedClonePath(string path)
        {
            var parts = path.Replace('\\', '/').Split('/');
            return parts.Length > 1 && parts[0] == "nxclone" && parts[1].StartsWith("clone-", StringComparison.Ordinal);
        }

        static bool HasComponent(Transform target, Type type) => type == typeof(GameObject) || target.GetComponent(type) != null;
    }
}
