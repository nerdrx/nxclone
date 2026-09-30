using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace nxclone
{
    public static class NxCloneRotationControls
    {
        public sealed class Result
        {
            public string[] Parameters;
            public string[] LayerNames;
            public VRCExpressionsMenu Menu;
        }

        static readonly string[] Axes = { "x", "y", "z" };
        static readonly string[] Labels = { "Pitch", "Yaw", "Roll" };

        public static Result Configure(AnimatorController fx, Transform avatarRoot, Transform driver,
            string folder, string prefix, NxCloneAxes enabledAxes = NxCloneAxes.All)
        {
            if (!fx || !avatarRoot || !driver) throw new ArgumentNullException("FX, avatar root and driver are required.");
            if (driver == avatarRoot || !driver.IsChildOf(avatarRoot))
                throw new ArgumentException("Placement driver must be inside the avatar root.", nameof(driver));
            if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException("Generated folder must exist in the AssetDatabase.", nameof(folder));
            if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("Unique parameter prefix is required.", nameof(prefix));
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(fx)))
                throw new ArgumentException("FX controller must be a saved asset.", nameof(fx));

            var placement = driver.GetComponent<VRCParentConstraint>();
            if (!placement || placement.Sources.Count == 0 || !placement.Sources[0].SourceTransform)
                throw new InvalidOperationException("Placement driver needs a VRC Parent Constraint with source 0.");
            Transform anchor = placement.Sources[0].SourceTransform;
            if (anchor == driver || anchor.IsChildOf(driver) || (anchor != avatarRoot && !anchor.IsChildOf(avatarRoot)))
                throw new InvalidOperationException("Placement source 0 must be an avatar transform outside the driver hierarchy.");
            if ((enabledAxes & ~NxCloneAxes.All) != 0)
                throw new ArgumentOutOfRangeException(nameof(enabledAxes));

            int[] selectedAxes = Enumerable.Range(0, Axes.Length)
                .Where(axis => (enabledAxes & (NxCloneAxes)(1 << axis)) != 0).ToArray();
            if (selectedAxes.Length == 0)
                return new Result { Parameters = Array.Empty<string>(), LayerNames = Array.Empty<string>(), Menu = null };

            var used = fx.parameters.Select(parameter => parameter.name).ToArray();
            string selectedPrefix = prefix.Trim();
            for (int suffix = 2; selectedAxes.Any(index => used.Contains(selectedPrefix + "_" + Axes[index], StringComparer.Ordinal)); suffix++)
                selectedPrefix = prefix.Trim() + "_" + suffix;
            var parameters = selectedAxes.Select(index => selectedPrefix + "_" + Axes[index]).ToArray();
            var layerNames = selectedAxes.Select(axis => "nxclone " + selectedPrefix + " rotation " + Labels[axis].ToLowerInvariant()).ToArray();
            string controllerPath = AssetDatabase.GetAssetPath(fx);

            var carriers = new Transform[selectedAxes.Length];
            Transform parent = anchor;
            for (int axis = 0; axis < selectedAxes.Length; axis++)
            {
                int selectedAxis = selectedAxes[axis];
                var carrier = new GameObject("__nxclone rotation " + selectedPrefix + " " + Axes[selectedAxis]).transform;
                carrier.SetParent(parent, false);
                carrier.localPosition = Vector3.zero;
                carrier.localRotation = Quaternion.identity;
                carrier.localScale = Vector3.one;
                carriers[axis] = parent = carrier;
            }
            var source = placement.Sources[0];
            source.SourceTransform = carriers[carriers.Length - 1];
            placement.Sources[0] = source;
            placement.ApplyConfigurationChanges();

            string[] carrierPaths = carriers.Select(carrier => AnimationUtility.CalculateTransformPath(carrier, avatarRoot)).ToArray();
            for (int axis = 0; axis < selectedAxes.Length; axis++)
            {
                int selectedAxis = selectedAxes[axis];
                fx.AddParameter(new AnimatorControllerParameter
                {
                    name = parameters[axis], type = AnimatorControllerParameterType.Float, defaultFloat = 0.5f
                });
                var tree = new BlendTree
                {
                    name = "nxclone rotation " + Labels[selectedAxis].ToLowerInvariant(),
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = parameters[axis],
                    useAutomaticThresholds = false,
                    minThreshold = 0f,
                    maxThreshold = 1f,
                    children = new[]
                    {
                        new ChildMotion { motion = NewClip(controllerPath, carrierPaths[axis], selectedAxis, -180f), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = NewClip(controllerPath, carrierPaths[axis], selectedAxis, 0f), threshold = 0.5f, timeScale = 1f },
                        new ChildMotion { motion = NewClip(controllerPath, carrierPaths[axis], selectedAxis, 180f), threshold = 1f, timeScale = 1f }
                    }
                };
                AssetDatabase.AddObjectToAsset(tree, fx);
                var machine = new AnimatorStateMachine { name = layerNames[axis] };
                AssetDatabase.AddObjectToAsset(machine, fx);
                var state = machine.AddState("Adjust clone " + Labels[selectedAxis].ToLowerInvariant());
                state.motion = tree;
                state.writeDefaultValues = false;
                machine.defaultState = state;
                fx.AddLayer(new AnimatorControllerLayer { name = layerNames[axis], defaultWeight = 1f, stateMachine = machine });
            }

            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "nxclone rotation dials";
            menu.controls = selectedAxes.Select((axis, i) => new VRCExpressionsMenu.Control
            {
                name = Labels[axis],
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = parameters[i] } }
            }).ToList();
            AssetDatabase.AddObjectToAsset(menu, fx);
            EditorUtility.SetDirty(placement);
            EditorUtility.SetDirty(fx);
            AssetDatabase.SaveAssets();
            return new Result { Parameters = parameters, LayerNames = layerNames, Menu = menu };
        }

        static AnimationClip NewClip(string controllerPath, string transformPath, int axis, float angle)
        {
            var clip = new AnimationClip { name = "nxclone rotation " + Axes[axis] + " " + angle, frameRate = 60f };
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "localEulerAnglesRaw." + Axes[axis]),
                AnimationCurve.Constant(0f, 1f, angle));
            AssetDatabase.AddObjectToAsset(clip, controllerPath);
            return clip;
        }
    }
}
