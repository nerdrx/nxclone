using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;

namespace nxclone
{
    /// <summary>Native Animator setup for temporarily wearing one generated clone as the visible avatar.</summary>
    public static class NxCloneWear
    {
        public sealed class Slot
        {
            public string Name;
            public Transform Driver;
            public Transform Visual;
            public string VisibleParameter;
        }

        public sealed class Result
        {
            public string ParameterName;
            public string SelectorLayerName;
            public string BaselineLayerName;
            public string[] VisualLayerNames;
            public VRCExpressionsMenu Menu;
            public AnimationClip BaselineClip;
            public IReadOnlyList<AnimationClip> Clips;
            public bool AfterimageMaskIntegrated;
        }

        /// <summary>
        /// Adds one zero-weight override layer per clone. A native selector uses
        /// VRCAnimatorLayerControl to weight exactly the chosen wear layer to one.
        /// Selection is Int 0 (off), then 1..slot count.
        /// </summary>
        public static Result Configure(AnimatorController fx, Transform avatarRoot, Renderer[] originalRenderers,
            IReadOnlyList<Slot> slots, string parameterName, string globalVisibleParameter, string afterimagesParameter,
            Transform primaryMask, Transform[] wornMasks, string generatedFolder)
        {
            if (!fx || !avatarRoot || slots == null || slots.Count < 1 || slots.Count > 4 ||
                string.IsNullOrWhiteSpace(parameterName) || !AssetDatabase.IsValidFolder(generatedFolder))
                throw new ArgumentException("Wear setup needs an FX controller, avatar root, one to four slots, parameter name, and an existing output folder.");
            originalRenderers = (originalRenderers ?? Array.Empty<Renderer>()).Where(r => r).Distinct().ToArray();
            if (originalRenderers.Length == 0) throw new InvalidOperationException("Wear mode needs at least one original avatar Renderer to hide.");
            if (fx.parameters.Any(p => p.name == parameterName)) throw new InvalidOperationException("Wear parameter already exists: " + parameterName);
            if (!IsBool(fx, globalVisibleParameter)) throw new InvalidOperationException("Wear mode needs the existing master visibility Bool parameter.");
            bool hasMasks = primaryMask || (wornMasks != null && wornMasks.Any(mask => mask));
            if (hasMasks && (!primaryMask || wornMasks == null || wornMasks.Length != slots.Count || wornMasks.Any(mask => !mask) ||
                !IsBool(fx, afterimagesParameter)))
                throw new InvalidOperationException("Afterimage wear masks need the primary mask, one worn-clone mask per slot, and the existing afterimages Bool.");
            if (originalRenderers.Any(renderer => renderer.transform != avatarRoot && !renderer.transform.IsChildOf(avatarRoot)))
                throw new InvalidOperationException("Wear mode cannot animate a renderer outside the uploaded avatar root.");
            if (hasMasks && ((primaryMask != avatarRoot && !primaryMask.IsChildOf(avatarRoot)) ||
                wornMasks.Any(mask => mask != avatarRoot && !mask.IsChildOf(avatarRoot))))
                throw new InvalidOperationException("Afterimage masks must be under the uploaded avatar root.");

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.Driver || !slot.Visual || string.IsNullOrWhiteSpace(slot.VisibleParameter) ||
                    !slot.Driver.IsChildOf(avatarRoot) || !slot.Visual.IsChildOf(slot.Driver))
                    throw new InvalidOperationException($"Wear slot {i + 1} needs a clone visual, placement driver, and visibility parameter under the avatar root.");
                if (!IsBool(fx, slot.VisibleParameter)) throw new InvalidOperationException($"Wear slot {i + 1} visibility parameter is missing or not Bool: {slot.VisibleParameter}");
                CanAddWearSource(slot.Driver.GetComponent<VRCParentConstraint>(), avatarRoot, i + 1);
                CanAddWearSource(slot.Driver.GetComponent<VRCScaleConstraint>(), avatarRoot, i + 1);
                foreach (string suffix in hasMasks ? new[] { "mask-off", "mask-on" } : new[] { "mask-off" })
                    if (AssetDatabase.LoadAssetAtPath<AnimationClip>($"{generatedFolder}/wear-{i + 1}-{suffix}.anim"))
                        throw new InvalidOperationException($"Wear clip already exists for clone {i + 1}. Regenerate into a clean output folder.");
            }
            for (int i = 0; i < slots.Count; i++)
            {
                EnsureWearSource(slots[i].Driver.GetComponent<VRCParentConstraint>(), avatarRoot);
                EnsureWearSource(slots[i].Driver.GetComponent<VRCScaleConstraint>(), avatarRoot);
            }

            fx.AddParameter(new AnimatorControllerParameter { name = parameterName, type = AnimatorControllerParameterType.Int, defaultInt = 0 });
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>(); menu.name = "nxclone wear";
            menu.controls = new List<VRCExpressionsMenu.Control>();
            var clips = new List<AnimationClip>();
            var layers = new List<AnimatorControllerLayer>();
            var visualNames = new string[slots.Count];

            // Wear clips change renderer and placement state. Restore only
            // properties without an existing FX curve so authored FX stays in charge.
            var existingFxClips = fx.animationClips.Where(clip => clip).ToArray();
            var existingFxBindings = existingFxClips.SelectMany(AnimationUtility.GetCurveBindings)
                .GroupBy(binding => BindingKey(binding.path, binding.propertyName))
                .ToDictionary(group => group.Key, group => group.Select(binding => binding.type).Distinct().ToArray());
            string baselineLayerName = null;
            AnimationClip baselineClip = null;
            baselineClip = new AnimationClip { name = "nxclone wear baseline" };
            foreach (var renderer in originalRenderers)
            {
                string path = Path(avatarRoot, renderer.transform);
                if (!HasBinding(existingFxBindings, path, renderer.GetType(), "m_Enabled"))
                    Curve(baselineClip, path, renderer.GetType(), "m_Enabled", renderer.enabled ? 1f : 0f);
            }
            foreach (var slot in slots)
            {
                AddConstraintBaseline(slot.Driver.GetComponent<VRCParentConstraint>(), avatarRoot, existingFxBindings, baselineClip);
                AddConstraintBaseline(slot.Driver.GetComponent<VRCScaleConstraint>(), avatarRoot, existingFxBindings, baselineClip);
            }
            if (hasMasks)
                foreach (var mask in wornMasks)
                    Curve(baselineClip, Path(avatarRoot, mask), typeof(GameObject), "m_IsActive", 0f);
            if (AnimationUtility.GetCurveBindings(baselineClip).Length > 0)
            {
                SaveClip(baselineClip, generatedFolder, "wear-baseline.anim");
                var baselineMachine = new AnimatorStateMachine { name = "nxclone wear baseline" };
                AssetDatabase.AddObjectToAsset(baselineMachine, fx);
                var baselineState = baselineMachine.AddState("Restore original renderers");
                baselineState.motion = baselineClip;
                baselineMachine.defaultState = baselineState;
                baselineState.writeDefaultValues = false;
                baselineLayerName = UniqueLayerName(fx.layers, "nxclone wear baseline");
                fx.AddLayer(new AnimatorControllerLayer { name = baselineLayerName, defaultWeight = 1f, stateMachine = baselineMachine });
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(baselineClip);
                baselineClip = null;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var machine = new AnimatorStateMachine { name = $"nxclone wear {i + 1}" };
                AssetDatabase.AddObjectToAsset(machine, fx);
                var wearOffMaskClip = CreateWearClip(i, false, avatarRoot, originalRenderers, slots, primaryMask, wornMasks, hasMasks, generatedFolder, clips);
                var offMask = machine.AddState("Wear mask off"); offMask.motion = wearOffMaskClip; machine.defaultState = offMask; offMask.writeDefaultValues = false;
                if (hasMasks)
                {
                    var wearOnMaskClip = CreateWearClip(i, true, avatarRoot, originalRenderers, slots, primaryMask, wornMasks, true, generatedFolder, clips);
                    var onMask = machine.AddState("Wear mask on"); onMask.motion = wearOnMaskClip; onMask.writeDefaultValues = false;
                    AddBoolTransition(offMask, onMask, afterimagesParameter, true);
                    AddBoolTransition(onMask, offMask, afterimagesParameter, false);
                }
                string layerName = UniqueLayerName(fx.layers, "nxclone wear " + (i + 1));
                visualNames[i] = layerName;
                var visualLayer = new AnimatorControllerLayer { name = layerName, defaultWeight = 0f, stateMachine = machine };
                fx.AddLayer(visualLayer); layers.Add(visualLayer);
                menu.controls.Add(new VRCExpressionsMenu.Control {
                    name = string.IsNullOrWhiteSpace(slot.Name) ? $"Wear clone {i + 1}" : "Wear " + slot.Name,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = parameterName }, value = i + 1
                });
            }

            var selectorMachine = new AnimatorStateMachine { name = "nxclone wear selector" };
            AssetDatabase.AddObjectToAsset(selectorMachine, fx);
            var off = selectorMachine.AddState("Off"); selectorMachine.defaultState = off; off.writeDefaultValues = false;
            for (int i = 0; i < visualNames.Length; i++)
                SetLayerWeight(off.AddStateMachineBehaviour<VRCAnimatorLayerControl>(), Array.FindIndex(fx.layers, l => l.name == visualNames[i]), 0f);
            var selectedStates = new AnimatorState[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                selectedStates[i] = selectorMachine.AddState("Wear " + (i + 1)); selectedStates[i].writeDefaultValues = false;
            }
            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = 0; j < visualNames.Length; j++)
                    SetLayerWeight(selectedStates[i].AddStateMachineBehaviour<VRCAnimatorLayerControl>(),
                        Array.FindIndex(fx.layers, l => l.name == visualNames[j]), i == j ? 1f : 0f);
                AddIntTransition(off, selectedStates[i], parameterName, i + 1);
                AddIntTransition(selectedStates[i], off, parameterName, 0);
                for (int j = 0; j < i; j++) AddIntTransition(selectedStates[i], selectedStates[j], parameterName, j + 1);
                for (int j = i + 1; j < slots.Count; j++) AddIntTransition(selectedStates[i], selectedStates[j], parameterName, j + 1);
            }
            string selectorName = UniqueLayerName(fx.layers, "nxclone wear selector");
            fx.AddLayer(new AnimatorControllerLayer { name = selectorName, defaultWeight = 1f, stateMachine = selectorMachine });
            EditorUtility.SetDirty(fx);
            return new Result {
                ParameterName = parameterName, SelectorLayerName = selectorName, BaselineLayerName = baselineLayerName,
                BaselineClip = baselineClip, VisualLayerNames = visualNames,
                Menu = menu, Clips = clips, AfterimageMaskIntegrated = hasMasks
            };
        }

        static AnimationClip CreateWearClip(int index, bool afterimagesOn, Transform avatarRoot, Renderer[] originals,
            IReadOnlyList<Slot> slots, Transform primaryMask, Transform[] wornMasks, bool hasMasks, string folder, List<AnimationClip> clips)
        {
            var slot = slots[index];
            var clip = new AnimationClip { name = $"nxclone wear {index + 1} mask {(afterimagesOn ? "on" : "off")}" };
            foreach (var renderer in originals) Curve(clip, Path(avatarRoot, renderer.transform), renderer.GetType(), "m_Enabled", 0f);
            Curve(clip, Path(avatarRoot, slot.Visual), typeof(GameObject), "m_IsActive", 1f);
            var parent = slot.Driver.GetComponent<VRCParentConstraint>();
            var scale = slot.Driver.GetComponent<VRCScaleConstraint>();
            Curve(clip, Path(avatarRoot, slot.Driver), typeof(VRCParentConstraint), "FreezeToWorld", 0f);
            Curve(clip, Path(avatarRoot, slot.Driver), typeof(VRCScaleConstraint), "FreezeToWorld", 0f);
            for (int source = 0; source < parent.Sources.Count; source++)
                Weight(clip, avatarRoot, slot.Driver, typeof(VRCParentConstraint), source, parent.Sources[source].SourceTransform == avatarRoot ? 1f : 0f);
            for (int source = 0; source < scale.Sources.Count; source++)
                Weight(clip, avatarRoot, slot.Driver, typeof(VRCScaleConstraint), source, scale.Sources[source].SourceTransform == avatarRoot ? 1f : 0f);
            if (hasMasks)
            {
                Curve(clip, Path(avatarRoot, primaryMask), typeof(GameObject), "m_IsActive", 0f);
                for (int mask = 0; mask < wornMasks.Length; mask++)
                    Curve(clip, Path(avatarRoot, wornMasks[mask]), typeof(GameObject), "m_IsActive", mask == index && afterimagesOn ? 1f : 0f);
            }
            string mode = afterimagesOn ? "mask-on" : "mask-off";
            SaveClip(clip, folder, $"wear-{index + 1}-{mode}.anim"); clips.Add(clip);
            return clip;
        }

        static void EnsureWearSource(VRCParentConstraint constraint, Transform root)
        {
            if (constraint.Sources.Any(source => source.SourceTransform == root)) return;
            constraint.Sources.Add(new VRCConstraintSource(root, 0f)); constraint.ApplyConfigurationChanges();
        }

        static void EnsureWearSource(VRCScaleConstraint constraint, Transform root)
        {
            if (constraint.Sources.Any(source => source.SourceTransform == root)) return;
            constraint.Sources.Add(new VRCConstraintSource(root, 0f)); constraint.ApplyConfigurationChanges();
        }

        static void AddConstraintBaseline(VRCParentConstraint constraint, Transform root,
            IReadOnlyDictionary<string, Type[]> existingFxBindings, AnimationClip clip)
        {
            string path = Path(root, constraint.transform);
            if (!HasBinding(existingFxBindings, path, typeof(VRCParentConstraint), "FreezeToWorld"))
                Curve(clip, path, typeof(VRCParentConstraint), "FreezeToWorld", constraint.FreezeToWorld ? 1f : 0f);
            for (int i = 0; i < constraint.Sources.Count; i++)
            {
                string property = $"Sources.source{i}.Weight";
                if (!HasBinding(existingFxBindings, path, typeof(VRCParentConstraint), property))
                    Weight(clip, root, constraint.transform, typeof(VRCParentConstraint), i, constraint.Sources[i].Weight);
            }
        }

        static void AddConstraintBaseline(VRCScaleConstraint constraint, Transform root,
            IReadOnlyDictionary<string, Type[]> existingFxBindings, AnimationClip clip)
        {
            string path = Path(root, constraint.transform);
            if (!HasBinding(existingFxBindings, path, typeof(VRCScaleConstraint), "FreezeToWorld"))
                Curve(clip, path, typeof(VRCScaleConstraint), "FreezeToWorld", constraint.FreezeToWorld ? 1f : 0f);
            for (int i = 0; i < constraint.Sources.Count; i++)
            {
                string property = $"Sources.source{i}.Weight";
                if (!HasBinding(existingFxBindings, path, typeof(VRCScaleConstraint), property))
                    Weight(clip, root, constraint.transform, typeof(VRCScaleConstraint), i, constraint.Sources[i].Weight);
            }
        }

        static bool HasBinding(IReadOnlyDictionary<string, Type[]> bindings, string path, Type targetType, string property) =>
            bindings.TryGetValue(BindingKey(path, property), out var types) && types.Any(type => type.IsAssignableFrom(targetType));

        static string BindingKey(string path, string property) => path + "\0" + property;

        static void CanAddWearSource(VRCParentConstraint constraint, Transform root, int slot)
        {
            if (!constraint) throw new InvalidOperationException($"Wear clone {slot} has no native parent constraint.");
            if (!constraint.Sources.Any(source => source.SourceTransform == root) && constraint.Sources.Count >= 16)
                throw new InvalidOperationException($"Wear clone {slot} already uses all 16 parent-constraint sources; free one before enabling wear mode.");
        }

        static void CanAddWearSource(VRCScaleConstraint constraint, Transform root, int slot)
        {
            if (!constraint) throw new InvalidOperationException($"Wear clone {slot} has no native scale constraint.");
            if (!constraint.Sources.Any(source => source.SourceTransform == root) && constraint.Sources.Count >= 16)
                throw new InvalidOperationException($"Wear clone {slot} already uses all 16 scale-constraint sources; free one before enabling wear mode.");
        }

        static void SetLayerWeight(VRCAnimatorLayerControl behavior, int layer, float weight)
        {
            behavior.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            behavior.layer = layer; behavior.goalWeight = weight; behavior.blendDuration = 0f;
        }

        static void Weight(AnimationClip clip, Transform root, Transform driver, Type type, int index, float weight) =>
            Curve(clip, Path(root, driver), type, $"Sources.source{index}.Weight", weight);
        static void AddIntTransition(AnimatorState from, AnimatorState to, string parameter, int value)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false; transition.duration = 0f; transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.Equals, value, parameter);
        }
        static void AddBoolTransition(AnimatorState from, AnimatorState to, string parameter, bool value)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false; transition.duration = 0f; transition.canTransitionToSelf = false;
            transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, parameter);
        }
        static string UniqueLayerName(AnimatorControllerLayer[] layers, string name)
        {
            string result = name;
            for (int suffix = 2; layers.Any(layer => layer.name == result); suffix++) result = name + " " + suffix;
            return result;
        }
        static string Path(Transform root, Transform target) => AnimationUtility.CalculateTransformPath(target, root);
        static bool IsBool(AnimatorController fx, string name) => !string.IsNullOrWhiteSpace(name) &&
            fx.parameters.Any(parameter => parameter.name == name && parameter.type == AnimatorControllerParameterType.Bool);
        static void Curve(AnimationClip clip, string path, Type type, string property, float value) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), AnimationCurve.Constant(0, 1, value));
        static void SaveClip(AnimationClip clip, string folder, string name)
        {
            var path = $"{folder}/{name}";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path)) throw new InvalidOperationException("Wear clip already exists: " + path);
            AssetDatabase.CreateAsset(clip, path);
        }
    }
}
