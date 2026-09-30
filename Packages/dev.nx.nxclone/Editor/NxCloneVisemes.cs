using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace nxclone
{
    /// <summary>Copies a source avatar's blend-shape visemes onto a generated visual.</summary>
    public static class NxCloneVisemes
    {
        const string VisemeParameter = "Viseme";
        const int VisemeCount = 15;

        /// <summary>
        /// Adds a layer driven by VRChat's built-in Viseme parameter. Returns false when
        /// the source has no usable viseme renderer or blend-shape mapping.
        /// </summary>
        public static bool AddCloneVisemes(
            AnimatorController controller,
            VRCAvatarDescriptor source,
            Transform avatarRoot,
            Transform generatedVisualRoot,
            string generatedFolder,
            string layerName = "nxclone visemes",
            string parameterName = VisemeParameter)
        {
            if (!controller || !source || !avatarRoot || !generatedVisualRoot ||
                !AssetDatabase.IsValidFolder(generatedFolder)) return false;

            var sourceRenderer = source.VisemeSkinnedMesh;
            var shapes = source.VisemeBlendShapes;
            if (!sourceRenderer || !sourceRenderer.sharedMesh || shapes == null ||
                shapes.Length < VisemeCount || !sourceRenderer.transform.IsChildOf(source.transform))
                return false;

            string rendererPath = PathOf(source.transform, sourceRenderer.transform);
            var cloneTransform = string.IsNullOrEmpty(rendererPath) ? generatedVisualRoot : generatedVisualRoot.Find(rendererPath);
            var cloneRenderer = cloneTransform ? cloneTransform.GetComponent<SkinnedMeshRenderer>() : null;
            if (!cloneRenderer || !cloneRenderer.sharedMesh ||
                cloneRenderer.sharedMesh.blendShapeCount == 0) return false;

            var shapeIndices = new int[VisemeCount];
            int validShapes = 0;
            for (int i = 0; i < VisemeCount; i++)
            {
                shapeIndices[i] = string.IsNullOrEmpty(shapes[i])
                    ? -1 : cloneRenderer.sharedMesh.GetBlendShapeIndex(shapes[i]);
                if (shapeIndices[i] >= 0) validShapes++;
            }
            if (validShapes == 0) return false;

            var parameter = controller.parameters.FirstOrDefault(p => p.name == parameterName);
            if (parameter != null && parameter.type != AnimatorControllerParameterType.Int && parameter.type != AnimatorControllerParameterType.Float) return false;
            if (parameter == null) controller.AddParameter(parameterName, AnimatorControllerParameterType.Int);

            var machine = new AnimatorStateMachine { name = layerName };
            AssetDatabase.AddObjectToAsset(machine, controller);
            var layer = new AnimatorControllerLayer
            {
                name = layerName,
                defaultWeight = 1f,
                stateMachine = machine
            };
            var states = new AnimatorState[VisemeCount];
            string path = PathOf(avatarRoot, cloneRenderer.transform);

            for (int viseme = 0; viseme < VisemeCount; viseme++)
            {
                var clip = new AnimationClip { name = $"nxclone viseme {viseme}", wrapMode = WrapMode.Loop };
                for (int index = 0; index < VisemeCount; index++)
                {
                    int blendShape = shapeIndices[index];
                    if (blendShape < 0) continue;
                    string shapeName = cloneRenderer.sharedMesh.GetBlendShapeName(blendShape);
                    float value = index == viseme ? 100f : 0f;
                    var curve = new AnimationCurve(new Keyframe(0f, value), new Keyframe(1f / 60f, value));
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shapeName), curve);
                }
                clip.frameRate = 60f;
                AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(
                    $"{generatedFolder}/nxclone-viseme-{viseme:00}.anim"));
                states[viseme] = machine.AddState($"viseme {viseme}");
                states[viseme].motion = clip;
                states[viseme].writeDefaultValues = false;
                if (viseme == 0) machine.defaultState = states[viseme];

                var transition = machine.AddAnyStateTransition(states[viseme]);
                transition.hasExitTime = false;
                transition.duration = 0f;
                transition.canTransitionToSelf = false;
                if (parameter != null && parameter.type == AnimatorControllerParameterType.Float)
                {
                    transition.AddCondition(AnimatorConditionMode.Greater, viseme - 0.5f, parameterName);
                    transition.AddCondition(AnimatorConditionMode.Less, viseme + 0.5f, parameterName);
                }
                else transition.AddCondition(AnimatorConditionMode.Equals, viseme, parameterName);
            }

            controller.AddLayer(layer);
            EditorUtility.SetDirty(controller);
            return true;
        }

        static string PathOf(Transform root, Transform child)
        {
            if (root == child) return string.Empty;
            var parts = new System.Collections.Generic.List<string>();
            for (var current = child; current && current != root; current = current.parent)
                parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
