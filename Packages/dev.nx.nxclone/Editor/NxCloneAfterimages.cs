using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace nxclone
{
    /// <summary>
    /// Builds the invisible primary stencil copy. Integration: create a material using
    /// nxclone/silhouette mask, call CreateMainSilhouetteMask(root, root, material), then
    /// include its returned transform in NxCloneFxMirror.MirrorFxCurves' visuals list.
    ///
    /// The shaders reserve stencil bits 0 and 1. Custom camera effects that overwrite
    /// those bits can break masking. Silhouettes are a single flat layer, clipped by
    /// opaque scene depth; transparent/cutout avatar material details are not reproduced.
    /// </summary>
    public static class NxCloneAfterimages
    {
        /// <summary>
        /// Creates a renderer-only hierarchy that follows the source transforms and FX
        /// bindings. Pass the returned root to NxCloneFxMirror.MirrorFxCurves so animated
        /// renderer enable and blendshape curves also drive the mask.
        /// </summary>
        public static Transform CreateMainSilhouetteMask(
            Transform sourceRoot, Transform outputRoot, Material maskMaterial)
        {
            if (!sourceRoot || !outputRoot || !maskMaterial)
                throw new System.ArgumentNullException("Source, output, and mask material are required.");

            var root = new GameObject("__nxclone main silhouette mask").transform;
            root.gameObject.layer = sourceRoot.gameObject.layer;
            root.SetParent(outputRoot, false);
            CopyTransforms(sourceRoot, root, maskMaterial);
            return root;
        }

        static void CopyTransforms(Transform source, Transform destination, Material material)
        {
            foreach (var renderer in source.GetComponents<Renderer>())
            {
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    var copy = destination.gameObject.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = skinned.sharedMesh;
                    copy.bones = skinned.bones;
                    copy.rootBone = skinned.rootBone;
                    copy.localBounds = skinned.localBounds;
                    copy.quality = skinned.quality;
                    copy.updateWhenOffscreen = skinned.updateWhenOffscreen;
                    copy.enabled = skinned.enabled;
                    copy.sharedMaterials = MaterialsFor(renderer, material);
                    if (skinned.sharedMesh)
                        for (int i = 0; i < skinned.sharedMesh.blendShapeCount; i++)
                            copy.SetBlendShapeWeight(i, skinned.GetBlendShapeWeight(i));
                }
                else if (renderer is MeshRenderer meshRenderer &&
                         source.GetComponent<MeshFilter>() is MeshFilter filter)
                {
                    var copyFilter = destination.gameObject.AddComponent<MeshFilter>();
                    copyFilter.sharedMesh = filter.sharedMesh;
                    var copy = destination.gameObject.AddComponent<MeshRenderer>();
                    copy.enabled = meshRenderer.enabled;
                    copy.sharedMaterials = MaterialsFor(renderer, material);
                }
            }

            if (destination.GetComponent<Renderer>())
            {
                var placement = destination.gameObject.AddComponent<VRCParentConstraint>();
                placement.Sources.Add(new VRCConstraintSource(source, 1f));
                placement.ActivateConstraint();
                placement.ApplyConfigurationChanges();
                var scale = destination.gameObject.AddComponent<VRCScaleConstraint>();
                scale.Sources.Add(new VRCConstraintSource(source, 1f));
                scale.ActivateConstraint();
                scale.ApplyConfigurationChanges();
            }

            // Snapshot before parenting the generated root; sourceRoot can equal outputRoot.
            var children = new Transform[source.childCount];
            for (int i = 0; i < children.Length; i++) children[i] = source.GetChild(i);
            foreach (var child in children)
            {
                // The generated group contains clones and afterimages, which must never
                // be included in the primary-avatar mask.
                if (child.name == "nxclone" || child.name.StartsWith("__nxclone")) continue;
                var copy = new GameObject(child.name).transform;
                copy.SetParent(destination, false);
                copy.gameObject.layer = child.gameObject.layer;
                copy.gameObject.SetActive(child.gameObject.activeSelf);
                copy.localPosition = child.localPosition;
                copy.localRotation = child.localRotation;
                copy.localScale = child.localScale;
                CopyTransforms(child, copy, material);
            }
        }

        static Material[] MaterialsFor(Renderer source, Material material)
        {
            var materials = source.sharedMaterials;
            int submeshes = source is SkinnedMeshRenderer skinned && skinned.sharedMesh
                ? skinned.sharedMesh.subMeshCount
                : source.GetComponent<MeshFilter>()?.sharedMesh?.subMeshCount ?? 1;
            int count = Mathf.Max(1, Mathf.Max(materials == null ? 0 : materials.Length, submeshes));
            var copies = new Material[count];
            for (int i = 0; i < count; i++) copies[i] = material;
            return copies;
        }
    }
}
