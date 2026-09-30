using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace nxclone
{
    /// <summary>Copies the source avatar's local render rig into a delayed visual.</summary>
    public static class NxCloneAfterimageRig
    {
        public static int Configure(Transform avatarRoot, Transform visual, float weight)
        {
            if (!avatarRoot || !visual)
                throw new ArgumentNullException("Avatar root and afterimage visual are required.");
            if (weight <= 0f || weight > 1f || float.IsNaN(weight) || float.IsInfinity(weight))
                throw new ArgumentOutOfRangeException(nameof(weight), "Constraint weight must be finite and between 0 and 1.");

            var required = new HashSet<Transform>();
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                AddAncestors(renderer.transform, visual, required);
            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                AddAncestors(renderer.rootBone, visual, required);
                foreach (var bone in renderer.bones) AddAncestors(bone, visual, required);
            }

            int configured = 0;
            foreach (var target in required.OrderBy(x => AnimationUtility.CalculateTransformPath(x, visual).Count(c => c == '/')))
            {
                string path = AnimationUtility.CalculateTransformPath(target, visual);
                var source = avatarRoot.Find(path);
                if (!source)
                    throw new InvalidOperationException($"Afterimage rig has no matching source transform for '{path}'.");

                var rotation = target.gameObject.AddComponent<VRCRotationConstraint>();
                rotation.Sources.Add(new VRCConstraintSource(target, 1f));
                rotation.Sources.Add(new VRCConstraintSource(source, weight));
                rotation.SolveInLocalSpace = true;
                rotation.ActivateConstraint();
                rotation.ApplyConfigurationChanges();

                var position = target.gameObject.AddComponent<VRCPositionConstraint>();
                position.Sources.Add(new VRCConstraintSource(target, 1f));
                position.Sources.Add(new VRCConstraintSource(source, weight));
                position.SolveInLocalSpace = true;
                position.ActivateConstraint();
                position.ApplyConfigurationChanges();

                var scale = target.gameObject.AddComponent<VRCScaleConstraint>();
                // Scale controls do not retain their local rest value when their own
                // transform is also a source, especially below rotated nonuniform rigs.
                // Copy scale directly; position and rotation carry the delayed feedback.
                scale.Sources.Add(new VRCConstraintSource(source, 1f));
                scale.SolveInLocalSpace = true;
                scale.ActivateConstraint();
                scale.ApplyConfigurationChanges();
                configured++;
            }
            return configured * 3;
        }

        static void AddAncestors(Transform item, Transform root, HashSet<Transform> required)
        {
            if (!item) return;
            while (item && item != root)
            {
                required.Add(item);
                item = item.parent;
            }
            if (item != root)
                throw new InvalidOperationException("All afterimage renderers and bones must be under the visual root.");
        }
    }
}
