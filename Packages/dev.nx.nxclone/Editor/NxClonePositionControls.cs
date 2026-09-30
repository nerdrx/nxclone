using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace nxclone
{
    public static class NxClonePositionControls
    {
        public sealed class Result
        {
            public string[] Parameters;
            public string[] LayerNames;
            public VRCExpressionsMenu Menu;
        }

        static readonly string[] Axes = { "x", "y", "z" };
        static readonly string[] Labels = { "Left / Right", "Down / Up", "Back / Forward" };

        public static Result Configure(AnimatorController fx, Transform avatarRoot, Transform anchor,
            string folder, string prefix, float rangeMeters = 2f)
        {
            if (!fx || !avatarRoot || !anchor) throw new ArgumentNullException("FX, avatar root and anchor are required.");
            if (anchor == avatarRoot || !anchor.IsChildOf(avatarRoot))
                throw new ArgumentException("Placement anchor must be inside the avatar root.", nameof(anchor));
            if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException("Generated folder must exist in the AssetDatabase.", nameof(folder));
            if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("Unique parameter prefix is required.", nameof(prefix));
            if (rangeMeters <= 0f || float.IsNaN(rangeMeters) || float.IsInfinity(rangeMeters))
                throw new ArgumentOutOfRangeException(nameof(rangeMeters));
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(fx)))
                throw new ArgumentException("FX controller must be a saved asset.", nameof(fx));

            var used = fx.parameters.Select(parameter => parameter.name).ToArray();
            string selectedPrefix = prefix.Trim();
            for (int suffix = 2; Axes.Any(axis => used.Contains(selectedPrefix + "_" + axis, StringComparer.Ordinal)); suffix++)
                selectedPrefix = prefix.Trim() + "_" + suffix;
            var parameters = Axes.Select(axis => selectedPrefix + "_" + axis).ToArray();
            var layerNames = new[] { $"nxclone {selectedPrefix} position" };
            string path = AnimationUtility.CalculateTransformPath(anchor, avatarRoot);
            string controllerPath = AssetDatabase.GetAssetPath(fx);

            foreach (string parameter in parameters)
                fx.AddParameter(new AnimatorControllerParameter
                {
                    name = parameter, type = AnimatorControllerParameterType.Float, defaultFloat = 0.5f
                });

            Vector3 origin = anchor.localPosition;
            var tree = BuildTree(fx, controllerPath, path, parameters, origin, rangeMeters, 0, "XYZ");
            var machine = new AnimatorStateMachine { name = layerNames[0] };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var state = machine.AddState("Adjust clone position");
            state.motion = tree;
            state.writeDefaultValues = false;
            machine.defaultState = state;
            fx.AddLayer(new AnimatorControllerLayer
            {
                name = layerNames[0], defaultWeight = 1f, stateMachine = machine
            });

            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "nxclone placement dials";
            menu.controls = Axes.Select((axis, i) => new VRCExpressionsMenu.Control
            {
                name = Labels[i],
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = parameters[i] } }
            }).ToList();
            AssetDatabase.AddObjectToAsset(menu, fx);
            EditorUtility.SetDirty(fx);
            AssetDatabase.SaveAssets();
            return new Result { Parameters = parameters, LayerNames = layerNames, Menu = menu };
        }

        static BlendTree BuildTree(AnimatorController fx, string controllerPath, string path,
            string[] parameters, Vector3 position, float range, int axis, string name)
        {
            var tree = new BlendTree
            {
                name = "nxclone position " + name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameters[axis],
                useAutomaticThresholds = false,
                minThreshold = 0f,
                maxThreshold = 1f
            };
            AssetDatabase.AddObjectToAsset(tree, fx);
            var children = new ChildMotion[2];
            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                var endpointPosition = position;
                endpointPosition[axis] += endpoint == 0 ? -range : range;
                Motion motion = axis == Axes.Length - 1
                    ? NewClip(controllerPath, path, endpointPosition, name + (endpoint == 0 ? "-" : "+"))
                    : BuildTree(fx, controllerPath, path, parameters, endpointPosition, range, axis + 1,
                        name + (endpoint == 0 ? "0" : "1"));
                children[endpoint] = new ChildMotion { motion = motion, threshold = endpoint, timeScale = 1f };
            }
            tree.children = children;
            return tree;
        }

        static AnimationClip NewClip(string controllerPath, string transformPath, Vector3 position, string suffix)
        {
            var clip = new AnimationClip { name = "nxclone position " + suffix, frameRate = 60f };
            foreach (int axis in Enumerable.Range(0, Axes.Length))
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalPosition." + Axes[axis]),
                    AnimationCurve.Constant(0f, 1f, position[axis]));
            AssetDatabase.AddObjectToAsset(clip, controllerPath);
            return clip;
        }
    }
}
