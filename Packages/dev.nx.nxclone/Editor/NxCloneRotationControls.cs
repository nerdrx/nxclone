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
            string folder, string prefix)
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

            var used = fx.parameters.Select(parameter => parameter.name).ToArray();
            string selectedPrefix = prefix.Trim();
            for (int suffix = 2; Axes.Any(axis => used.Contains(selectedPrefix + "_" + axis, StringComparer.Ordinal)); suffix++)
                selectedPrefix = prefix.Trim() + "_" + suffix;
            var parameters = Axes.Select(axis => selectedPrefix + "_" + axis).ToArray();
            var layerNames = Axes.Select((axis, i) => "nxclone " + selectedPrefix + " rotation " + Labels[i].ToLowerInvariant()).ToArray();
            string controllerPath = AssetDatabase.GetAssetPath(fx);

            var carriers = new Transform[Axes.Length];
            Transform parent = anchor;
            for (int axis = 0; axis < Axes.Length; axis++)
            {
                var carrier = new GameObject("__nxclone rotation " + selectedPrefix + " " + Axes[axis]).transform;
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
            for (int axis = 0; axis < Axes.Length; axis++)
            {
                fx.AddParameter(new AnimatorControllerParameter
                {
                    name = parameters[axis], type = AnimatorControllerParameterType.Float, defaultFloat = 0.5f
                });
                var tree = new BlendTree
                {
                    name = "nxclone rotation " + Labels[axis].ToLowerInvariant(),
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = parameters[axis],
                    useAutomaticThresholds = false,
                    minThreshold = 0f,
                    maxThreshold = 1f,
                    children = new[]
                    {
                        new ChildMotion { motion = NewClip(controllerPath, carrierPaths[axis], axis, -180f), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = NewClip(controllerPath, carrierPaths[axis], axis, 0f), threshold = 0.5f, timeScale = 1f },
                        new ChildMotion { motion = NewClip(controllerPath, carrierPaths[axis], axis, 180f), threshold = 1f, timeScale = 1f }
                    }
                };
                AssetDatabase.AddObjectToAsset(tree, fx);
                var machine = new AnimatorStateMachine { name = layerNames[axis] };
                AssetDatabase.AddObjectToAsset(machine, fx);
                var state = machine.AddState("Adjust clone " + Labels[axis].ToLowerInvariant());
                state.motion = tree;
                state.writeDefaultValues = false;
                machine.defaultState = state;
                fx.AddLayer(new AnimatorControllerLayer { name = layerNames[axis], defaultWeight = 1f, stateMachine = machine });
            }

            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "nxclone rotation dials";
            menu.controls = Axes.Select((axis, i) => new VRCExpressionsMenu.Control
            {
                name = Labels[i],
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
