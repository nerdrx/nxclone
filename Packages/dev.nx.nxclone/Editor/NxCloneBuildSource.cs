using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace nxclone
{
    internal static class NxCloneBuildSource
    {
        const string TempPackageRoot = "Packages/com.vrcfury.temp";
        const string VrcBuildPipelineType = "VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks";

        public static void Bake(GameObject copy, string generatedFolder)
        {
            if (!copy) throw new ArgumentNullException(nameof(copy));
            if (string.IsNullOrEmpty(generatedFolder) || !generatedFolder.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Generated folder must be an AssetDatabase path under Assets/.", nameof(generatedFolder));

            EnsureFolder(generatedFolder);
            RunAvatarPreprocessors(copy);
            EnsureNoVrcfuryConfigs(copy);
            AssetDatabase.SaveAssets();
            PreserveVrcfuryTempAssets(copy, generatedFolder);
        }

        static void RunAvatarPreprocessors(GameObject copy)
        {
            Type pipelineType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                pipelineType = assembly.GetType(VrcBuildPipelineType, false);
                if (pipelineType != null) break;
            }

            var method = pipelineType?.GetMethod("OnPreprocessAvatar", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(GameObject) }, null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new InvalidOperationException("VRChat SDK avatar preprocessing API is unavailable; cannot safely bake avatar build components.");

            try
            {
                if (!(bool)method.Invoke(null, new object[] { copy }))
                    throw new InvalidOperationException("VRChat avatar preprocessing rejected the isolated source copy.");
            }
            catch (TargetInvocationException e)
            {
                throw new InvalidOperationException("Avatar preprocessing failed on the isolated source copy.", e.InnerException ?? e);
            }
        }

        static void EnsureNoVrcfuryConfigs(GameObject copy)
        {
            var leftovers = copy.GetComponentsInChildren<Component>(true)
                .Where(c => c && IsVrcfuryBuildComponent(c.GetType()))
                .Select(c => c.GetType().FullName + " on " + AnimationUtility.CalculateTransformPath(c.transform, copy.transform))
                .ToArray();
            if (leftovers.Length != 0)
                throw new InvalidOperationException("VRCFury components remain after avatar preprocessing; refusing to extract an unbaked visual: " + string.Join(", ", leftovers));
        }

        static bool IsVrcfuryBuildComponent(Type type)
        {
            if (type.FullName == "VF.Model.VRCFuryTest" || type.FullName == "VF.Model.VRCFuryDebugInfo") return false;
            for (var current = type; current != null; current = current.BaseType)
                if (current.FullName == "VF.Component.VRCFuryComponent") return true;
            return type.GetInterfaces().Any(i => i.FullName == "VF.VrcfEditorOnly.IVrcfEditorOnly");
        }

        static void PreserveVrcfuryTempAssets(GameObject copy, string generatedFolder)
        {
            var map = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            var copiedAssets = new List<UnityEngine.Object>();
            var pending = new Queue<UnityEngine.Object>();
            var visitedPaths = new HashSet<string>(StringComparer.Ordinal);
            var bakedFolder = generatedFolder + "/baked-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            CollectReferences(copy.GetComponentsInChildren<Component>(true), pending);

            while (pending.Count > 0)
            {
                var source = pending.Dequeue();
                if (!source) continue;
                var sourcePath = AssetDatabase.GetAssetPath(source);
                if (!IsTempAsset(sourcePath) || !visitedPaths.Add(sourcePath)) continue;

                var targetPath = CopyTempAsset(sourcePath, bakedFolder);
                var sourceAssets = AssetDatabase.LoadAllAssetsAtPath(sourcePath).Where(a => a).ToArray();
                var targetAssets = AssetDatabase.LoadAllAssetsAtPath(targetPath).Where(a => a).ToArray();
                MapAssetObjects(sourceAssets, targetAssets, map);
                copiedAssets.AddRange(sourceAssets);
                CollectReferences(sourceAssets, pending);
            }

            if (map.Count == 0) return;
            foreach (var source in copiedAssets)
                RemapReferences(map[source], map);
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
                if (component) RemapReferences(component, map);
            AssetDatabase.SaveAssets();
        }

        static void CollectReferences(IEnumerable<UnityEngine.Object> objects, Queue<UnityEngine.Object> pending)
        {
            foreach (var obj in objects)
            {
                if (!obj) continue;
                var serialized = new SerializedObject(obj);
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue)
                        pending.Enqueue(property.objectReferenceValue);
            }
        }

        static void MapAssetObjects(UnityEngine.Object[] source, UnityEngine.Object[] target,
            Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            var remaining = target.ToList();
            foreach (var original in source)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string originalGuid, out long originalId);
                var match = remaining.FirstOrDefault(candidate => {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string candidateGuid, out long candidateId);
                    return candidate.GetType() == original.GetType() && candidateId == originalId;
                });
                if (!match)
                    throw new InvalidOperationException("Could not map generated VRCFury asset object " + original.name + " to its durable copy.");
                map[original] = match;
                remaining.Remove(match);
            }
        }

        static void RemapReferences(UnityEngine.Object target, Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            var serialized = new SerializedObject(target);
            var paths = new List<string>();
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue && map.ContainsKey(property.objectReferenceValue))
                    paths.Add(property.propertyPath);

            foreach (var path in paths)
            {
                property = serialized.FindProperty(path);
                property.objectReferenceValue = map[property.objectReferenceValue];
            }
            if (paths.Count == 0) return;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        static string CopyTempAsset(string sourcePath, string bakedFolder)
        {
            // Short filenames avoid both Unix component limits and Windows path limits.
            var targetPath = bakedFolder + "/asset-" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(sourcePath);
            EnsureFolder(Path.GetDirectoryName(targetPath)?.Replace('\\', '/'));
            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
                throw new InvalidOperationException("Could not preserve generated VRCFury asset " + sourcePath + " at " + targetPath + ".");
            return targetPath;
        }

        static bool IsTempAsset(string path) => path == TempPackageRoot || path.StartsWith(TempPackageRoot + "/", StringComparison.Ordinal);

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException("Invalid asset folder: " + path);
            EnsureFolder(parent);
            var guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Could not create asset folder " + path + ".");
        }
    }
}
