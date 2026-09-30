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
            bool silhouetteOnly = false,
            Transform sourceVisual = null)
        {
            if (!copiedController || !avatarRoot || generatedVisuals == null ||
                !AssetDatabase.IsValidFolder(generatedFolder) ||
                (sourceVisual && sourceVisual != avatarRoot && !sourceVisual.IsChildOf(avatarRoot))) return 0;

            var clips = new Dictionary<AnimationClip, AnimationClip>();
            var trees = new Dictionary<BlendTree, BlendTree>();
            var visitedTrees = new HashSet<BlendTree>();
            foreach (var layer in copiedController.layers)
                CloneMotions(layer.stateMachine, clips, trees, visitedTrees, avatarRoot, generatedVisuals, generatedFolder, silhouetteOnly, sourceVisual);

            EditorUtility.SetDirty(copiedController);
            int copied = 0;
            foreach (var pair in clips) if (pair.Key != pair.Value) copied++;
            return copied;
        }

        static void CloneMotions(AnimatorStateMachine machine,
            Dictionary<AnimationClip, AnimationClip> clips,
            Dictionary<BlendTree, BlendTree> trees, HashSet<BlendTree> visitedTrees, Transform root,
            IReadOnlyList<Transform> visuals, string folder, bool silhouetteOnly, Transform sourceVisual)
        {
            // Synced AnimatorController layers intentionally have no independent state machine.
            if (!machine) return;
            foreach (var child in machine.states)
                child.state.motion = CloneMotion(child.state.motion, clips, trees, visitedTrees, root, visuals, folder, silhouetteOnly, sourceVisual);
            foreach (var child in machine.stateMachines)
                CloneMotions(child.stateMachine, clips, trees, visitedTrees, root, visuals, folder, silhouetteOnly, sourceVisual);
        }

        static Motion CloneMotion(Motion motion, Dictionary<AnimationClip, AnimationClip> clips,
            Dictionary<BlendTree, BlendTree> trees, HashSet<BlendTree> visitedTrees,
            Transform root, IReadOnlyList<Transform> visuals, string folder, bool silhouetteOnly, Transform sourceVisual)
        {
            if (motion is AnimationClip clip)
            {
                if (!clips.TryGetValue(clip, out var copy))
                {
                    copy = HasRemappableBindings(clip, root, visuals, silhouetteOnly, sourceVisual) ? Object.Instantiate(clip) : clip;
                    clips.Add(clip, copy);
                    if (copy != clip)
                    {
                        copy.name = clip.name + " (nxclone FX)";
                        AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(
                            $"{folder}/{Sanitize(clip.name)}-fx.anim"));
                        CopyBindings(clip, copy, root, visuals, silhouetteOnly, sourceVisual);
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
                    children[i].motion = CloneMotion(children[i].motion, clips, trees, visitedTrees, root, visuals, folder, silhouetteOnly, sourceVisual);
                }
                copy.children = children;
                EditorUtility.SetDirty(copy);
                return copy;
            }
            return motion;
        }

        static void CopyBindings(AnimationClip source, AnimationClip destination,
            Transform root, IReadOnlyList<Transform> visuals, bool silhouetteOnly, Transform sourceVisual)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (!Eligible(binding, silhouetteOnly)) continue;
                if (!TryResolveSource(root, sourceVisual, binding.path, out var sourceTarget, out var relativePath)) continue;
                if (!sourceTarget || !HasTargetComponent(sourceTarget, binding)) continue;
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null) continue;

                foreach (var visual in visuals)
                {
                    if (!visual) continue;
                    var target = Resolve(visual, relativePath);
                    if (!target || !HasTargetComponent(target, binding)) continue;
                    var cloneBinding = binding;
                    cloneBinding.path = AnimationUtility.CalculateTransformPath(target, root);
                    AnimationUtility.SetEditorCurve(destination, cloneBinding, curve);
                }
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                if (!Eligible(binding, silhouetteOnly)) continue;
                if (!TryResolveSource(root, sourceVisual, binding.path, out var sourceTarget, out var relativePath)) continue;
                if (!sourceTarget || !HasTargetComponent(sourceTarget, binding)) continue;
                var curve = AnimationUtility.GetObjectReferenceCurve(source, binding);
                foreach (var visual in visuals)
                {
                    if (!visual) continue;
                    var target = Resolve(visual, relativePath);
                    if (!target || !HasTargetComponent(target, binding)) continue;
                    var cloneBinding = binding;
                    cloneBinding.path = AnimationUtility.CalculateTransformPath(target, root);
                    AnimationUtility.SetObjectReferenceCurve(destination, cloneBinding, curve);
                }
            }
        }

        static bool HasRemappableBindings(AnimationClip clip, Transform root, IReadOnlyList<Transform> visuals, bool silhouetteOnly, Transform sourceVisual)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (HasDestination(binding, root, visuals, silhouetteOnly, sourceVisual)) return true;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                if (HasDestination(binding, root, visuals, silhouetteOnly, sourceVisual)) return true;
            return false;
        }

        static bool HasDestination(EditorCurveBinding binding, Transform root, IReadOnlyList<Transform> visuals, bool silhouetteOnly, Transform sourceVisual)
        {
            if (!Eligible(binding, silhouetteOnly)) return false;
            if (!TryResolveSource(root, sourceVisual, binding.path, out var source, out var relativePath)) return false;
            if (!source || !HasTargetComponent(source, binding)) return false;
            foreach (var visual in visuals)
            {
                if (!visual) continue;
                var target = Resolve(visual, relativePath);
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

        static bool TryResolveSource(Transform root, Transform sourceVisual, string bindingPath,
            out Transform source, out string relativePath)
        {
            if (!sourceVisual)
            {
                relativePath = bindingPath;
                source = Resolve(root, bindingPath);
                return source;
            }
            string prefix = AnimationUtility.CalculateTransformPath(sourceVisual, root);
            if (bindingPath == prefix) relativePath = string.Empty;
            else if (prefix.Length == 0) relativePath = bindingPath;
            else if (bindingPath.StartsWith(prefix + "/", System.StringComparison.Ordinal)) relativePath = bindingPath.Substring(prefix.Length + 1);
            else { source = null; relativePath = null; return false; }
            source = Resolve(sourceVisual, relativePath);
            return source;
        }

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
