using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;
using UnityEditor.Animations;

namespace nxclone
{
    /// <summary>Adds remote contact receivers that can move each limb IK target.</summary>
    public static class NxCloneLimbContacts
    {
        static readonly string[] Tags = { "HandL", "HandR", "FootL", "FootR" };

        public sealed class Result
        {
            public NxCloneContactAnchor.Result[] Contacts;
            public Transform[] TargetDrivers;
        }

        public static Result Configure(Transform avatarRoot, Transform placementDriver,
            NxCloneLimbIk.Result ik, AnimatorController rootFx, VRCExpressionParameters rootParameters,
            int cloneIndex, string outputFolder, string uniquePrefix)
        {
            if (!avatarRoot || !placementDriver || ik == null || !rootFx)
                throw new ArgumentException("Limb contacts need an avatar, placement driver, IK setup, and FX controller.");
            if (cloneIndex < 1 || cloneIndex > (int.MaxValue - Tags.Length) / 10)
                throw new ArgumentOutOfRangeException(nameof(cloneIndex));
            if (!placementDriver.IsChildOf(avatarRoot) || !placementDriver.parent)
                throw new ArgumentException("Limb contact targets must use a placement driver inside the avatar.");
            if (string.IsNullOrWhiteSpace(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder))
                throw new ArgumentException("Generated folder must exist in the AssetDatabase.", nameof(outputFolder));
            string prefix = Safe(uniquePrefix);
            if (prefix.Length == 0) throw new ArgumentException("Unique prefix must contain letters or digits.", nameof(uniquePrefix));
            if (ik.Solvers == null || ik.GoalHandles == null || ik.Solvers.Length != Tags.Length || ik.GoalHandles.Length != Tags.Length)
                throw new InvalidOperationException("Limb contacts require all four generated IK solvers and goal handles.");

            var frame = placementDriver.parent;
            var framePose = frame.GetComponent<VRCParentConstraint>();
            var frameScale = frame.GetComponent<VRCScaleConstraint>();
            if (!framePose || !frameScale || !framePose.FreezeToWorld || !frameScale.FreezeToWorld)
                throw new InvalidOperationException("Limb contact target drivers must live under the frozen placement frame.");

            var endpoints = new Transform[Tags.Length];
            var originalTargets = new UnityEngine.Object[Tags.Length];
            for (int i = 0; i < Tags.Length; i++)
            {
                var goal = ik.GoalHandles[i] ? ik.GoalHandles[i].transform : null;
                endpoints[i] = goal ? goal.Find("target") : null;
                if (!goal || !endpoints[i] || endpoints[i].parent != goal)
                    throw new InvalidOperationException("Limb IK goal is missing its original hand/foot endpoint child.");
                var serialized = new SerializedObject(ik.Solvers[i]);
                var target = serialized.FindProperty("solver.target");
                if (target == null || target.propertyType != SerializedPropertyType.ObjectReference || target.objectReferenceValue != endpoints[i])
                    throw new InvalidOperationException("Limb IK solver target changed before contact tracking was configured.");
                originalTargets[i] = target.objectReferenceValue;
            }

            string fxPath = AssetDatabase.GetAssetPath(rootFx);
            if (string.IsNullOrEmpty(fxPath)) throw new InvalidOperationException("Limb contacts require a saved root FX controller asset.");
            var originalLayers = rootFx.layers;
            var originalParameters = rootFx.parameters;
            var originalSubassets = new HashSet<UnityEngine.Object>(AssetDatabase.LoadAllAssetsAtPath(fxPath));
            var originalAssets = new HashSet<string>(AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith(outputFolder.TrimEnd('/') + "/", StringComparison.Ordinal)), StringComparer.Ordinal);
            var contacts = new NxCloneContactAnchor.Result[Tags.Length];
            var targetDrivers = new Transform[Tags.Length];
            var anchorTargets = new Transform[Tags.Length];

            try
            {
                for (int i = 0; i < Tags.Length; i++)
                {
                    string name = $"nxclone {prefix} IK target {cloneIndex} {Tags[i]}";
                    targetDrivers[i] = NxClonePlacement.Follow(frame, endpoints[i], name, Vector3.zero, Vector3.zero);
                    anchorTargets[i] = endpoints[i].Find("nxclone anchor " + name);
                    int contactNamespace = checked(cloneIndex * 10 + i + 1);
                    string contactPrefix = $"{prefix}_ik_{cloneIndex}_{Tags[i]}";
                    contacts[i] = NxCloneContactAnchor.Configure(avatarRoot, targetDrivers[i], rootFx,
                        rootParameters, contactNamespace, outputFolder, contactPrefix, Tags[i], allowSelf: false, allowOthers: true);

                    var serialized = new SerializedObject(ik.Solvers[i]);
                    var target = serialized.FindProperty("solver.target");
                    target.objectReferenceValue = targetDrivers[i];
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(rootFx);
                return new Result { Contacts = contacts, TargetDrivers = targetDrivers };
            }
            catch
            {
                rootFx.layers = originalLayers;
                rootFx.parameters = originalParameters;
                for (int i = 0; i < Tags.Length; i++)
                {
                    if (ik.Solvers[i])
                    {
                        var serialized = new SerializedObject(ik.Solvers[i]);
                        var target = serialized.FindProperty("solver.target");
                        if (target != null) target.objectReferenceValue = originalTargets[i];
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    if (contacts[i]?.root) UnityEngine.Object.DestroyImmediate(contacts[i].root);
                    if (targetDrivers[i]) UnityEngine.Object.DestroyImmediate(targetDrivers[i].gameObject);
                    if (anchorTargets[i]) UnityEngine.Object.DestroyImmediate(anchorTargets[i].gameObject);
                }
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fxPath)
                    .Where(asset => asset && !originalSubassets.Contains(asset)))
                    UnityEngine.Object.DestroyImmediate(asset, true);
                foreach (var path in AssetDatabase.GetAllAssetPaths()
                    .Where(path => path.StartsWith(outputFolder.TrimEnd('/') + "/", StringComparison.Ordinal) && !originalAssets.Contains(path))
                    .OrderByDescending(path => path.Length).ToArray())
                    AssetDatabase.DeleteAsset(path);
                EditorUtility.SetDirty(rootFx);
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        static string Safe(string value) => string.Concat((value ?? string.Empty).Select(character =>
            char.IsLetterOrDigit(character) || character == '_' ? character : '_'));
    }
}
