using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace nxclone
{
    /// <summary>Duplicates eligible source FX curves onto matching generated visuals.</summary>
    public static class NxCloneFxMirror
    {
        /// <summary>
        /// Clones clips referenced by the copied controller and adds matching renderer and
        /// GameObject-active curves for each generated visual. Source clips stay untouched.
        /// </summary>
        public static int MirrorFxCurves(
            AnimatorController copiedController,
            Transform avatarRoot,
            IReadOnlyList<Transform> generatedVisuals,
            string generatedFolder,
            bool silhouetteOnly = false)
        {
            if (!copiedController || !avatarRoot || generatedVisuals == null ||
                !AssetDatabase.IsValidFolder(generatedFolder)) return 0;

            var clips = new Dictionary<AnimationClip, AnimationClip>();
            var trees = new Dictionary<BlendTree, BlendTree>();
            var visitedTrees = new HashSet<BlendTree>();
            foreach (var layer in copiedController.layers)
                CloneMotions(layer.stateMachine, clips, trees, visitedTrees, avatarRoot, generatedVisuals, generatedFolder, silhouetteOnly);

            EditorUtility.SetDirty(copiedController);
            int copied = 0;
            foreach (var pair in clips) if (pair.Key != pair.Value) copied++;
            return copied;
        }

        static void CloneMotions(AnimatorStateMachine machine,
            Dictionary<AnimationClip, AnimationClip> clips,
            Dictionary<BlendTree, BlendTree> trees, HashSet<BlendTree> visitedTrees, Transform root,
            IReadOnlyList<Transform> visuals, string folder, bool silhouetteOnly)
        {
            foreach (var child in machine.states)
                child.state.motion = CloneMotion(child.state.motion, clips, trees, visitedTrees, root, visuals, folder, silhouetteOnly);
            foreach (var child in machine.stateMachines)
                CloneMotions(child.stateMachine, clips, trees, visitedTrees, root, visuals, folder, silhouetteOnly);
        }

        static Motion CloneMotion(Motion motion, Dictionary<AnimationClip, AnimationClip> clips,
            Dictionary<BlendTree, BlendTree> trees, HashSet<BlendTree> visitedTrees,
            Transform root, IReadOnlyList<Transform> visuals, string folder, bool silhouetteOnly)
        {
            if (motion is AnimationClip clip)
            {
                if (!clips.TryGetValue(clip, out var copy))
                {
                    copy = HasRemappableBindings(clip, root, visuals, silhouetteOnly) ? Object.Instantiate(clip) : clip;
                    clips.Add(clip, copy);
                    if (copy != clip)
                    {
                        copy.name = clip.name + " (nxclone FX)";
                        AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(
                            $"{folder}/{Sanitize(clip.name)}-fx.anim"));
                        CopyBindings(clip, copy, root, visuals, silhouetteOnly);
                    }
                }
                return copy;
            }

            if (motion is BlendTree tree)
            {
                if (!trees.TryGetValue(tree, out var copy))
                {
                    copy = new BlendTree();
                    EditorUtility.CopySerialized(tree, copy);
                    copy.name = tree.name + " (nxclone FX)";
                    AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(
                        $"{folder}/{Sanitize(tree.name)}-fx-blendtree.asset"));
                    trees.Add(tree, copy);
                }
                if (!visitedTrees.Add(tree)) return copy;

                var children = copy.children;
                for (int i = 0; i < children.Length; i++)
                {
                    children[i].motion = CloneMotion(children[i].motion, clips, trees, visitedTrees, root, visuals, folder, silhouetteOnly);
                }
                copy.children = children;
                EditorUtility.SetDirty(copy);
                return copy;
            }
            return motion;
        }

        static void CopyBindings(AnimationClip source, AnimationClip destination,
            Transform root, IReadOnlyList<Transform> visuals, bool silhouetteOnly)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (!Eligible(binding, silhouetteOnly)) continue;
                var sourceTarget = Resolve(root, binding.path);
                if (!sourceTarget || !HasTargetComponent(sourceTarget, binding)) continue;
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null) continue;

                foreach (var visual in visuals)
                {
                    if (!visual) continue;
                    var target = Resolve(visual, binding.path);
                    if (!target || !HasTargetComponent(target, binding)) continue;
                    var cloneBinding = binding;
                    cloneBinding.path = AnimationUtility.CalculateTransformPath(target, root);
                    AnimationUtility.SetEditorCurve(destination, cloneBinding, curve);
                }
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                if (!Eligible(binding, silhouetteOnly)) continue;
                var sourceTarget = Resolve(root, binding.path);
                if (!sourceTarget || !HasTargetComponent(sourceTarget, binding)) continue;
                var curve = AnimationUtility.GetObjectReferenceCurve(source, binding);
                foreach (var visual in visuals)
                {
                    if (!visual) continue;
                    var target = Resolve(visual, binding.path);
                    if (!target || !HasTargetComponent(target, binding)) continue;
                    var cloneBinding = binding;
                    cloneBinding.path = AnimationUtility.CalculateTransformPath(target, root);
                    AnimationUtility.SetObjectReferenceCurve(destination, cloneBinding, curve);
                }
            }
        }

        static bool HasRemappableBindings(AnimationClip clip, Transform root, IReadOnlyList<Transform> visuals, bool silhouetteOnly)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (HasDestination(binding, root, visuals, silhouetteOnly)) return true;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                if (HasDestination(binding, root, visuals, silhouetteOnly)) return true;
            return false;
        }

        static bool HasDestination(EditorCurveBinding binding, Transform root, IReadOnlyList<Transform> visuals, bool silhouetteOnly)
        {
            if (!Eligible(binding, silhouetteOnly)) return false;
            var source = Resolve(root, binding.path);
            if (!source || !HasTargetComponent(source, binding)) return false;
            foreach (var visual in visuals)
            {
                if (!visual) continue;
                var target = Resolve(visual, binding.path);
                if (target && HasTargetComponent(target, binding)) return true;
            }
            return false;
        }

        static bool Eligible(EditorCurveBinding binding, bool silhouetteOnly)
        {
            if (silhouetteOnly)
            {
                if (binding.type == typeof(GameObject)) return binding.propertyName == "m_IsActive";
                if (binding.type == typeof(SkinnedMeshRenderer) || binding.type == typeof(MeshRenderer))
                    return binding.propertyName == "m_Enabled" ||
                           (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape."));
                return false;
            }
            if (binding.type == typeof(Transform) || binding.type == typeof(RectTransform)) return false;
            if (binding.type == typeof(GameObject)) return binding.propertyName == "m_IsActive";
            return binding.type == typeof(SkinnedMeshRenderer) || binding.type == typeof(MeshRenderer);
        }

        static bool HasTargetComponent(Transform target, EditorCurveBinding binding)
        {
            if (binding.type == typeof(GameObject)) return true;
            return target.GetComponent(binding.type) != null;
        }

        static Transform Resolve(Transform root, string path) =>
            string.IsNullOrEmpty(path) ? root : root.Find(path);

        static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "animation";
            var result = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
                result.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            var safe = result.ToString().Trim('_', '.');
            return safe.Length > 0 ? safe.Substring(0, System.Math.Min(64, safe.Length)) : "animation";
        }
    }
}
