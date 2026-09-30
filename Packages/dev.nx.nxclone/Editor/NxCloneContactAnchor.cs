using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace nxclone
{
    /// <summary>Adapts VRLabs Contact Tracker to an nxclone placement driver.</summary>
    public static class NxCloneContactAnchor
    {
        const string TrackerRootName = "Contact Tracker";
        const string PrefabPath = "Packages/dev.nx.nxclone/ThirdParty/ContactTracker/Contact Tracker.prefab";
        const string ControllerPath = "Packages/dev.nx.nxclone/ThirdParty/ContactTracker/Contact Tracker FX.controller";

        public sealed class Result
        {
            public GameObject root;
            public Transform trackingPoint;
            public AnimatorController controller;
            public VRCExpressionsMenu menu;
            public VRCExpressionParameters.Parameter[] parameters;
            public IReadOnlyDictionary<string, string> parameterNames;
            public string parameterName;
            public string remoteAttachBoolParameter;
            public string senderTag;
            public string requiredSenderHelp;
            public int syncedParameterCost;
            public string[] writeDefaultsOnLayers;
        }

        /// <summary>
        /// Adds a disabled-by-default remote contact anchor to one clone driver. Other avatars
        /// must expose a Contact Sender carrying <paramref name="tag"/> and permit avatar contacts.
        /// </summary>
        public static Result Configure(
            Transform avatarRoot,
            Transform placementDriver,
            AnimatorController rootFx,
            VRCExpressionParameters rootParameters,
            int cloneIndex,
            string outputFolder,
            string uniquePrefix,
            string tag = "HandL",
            bool allowSelf = false,
            bool allowOthers = true)
        {
            if (!avatarRoot || !placementDriver || !rootFx)
                throw new ArgumentNullException("Avatar root, placement driver and root FX controller are required.");
            if (!placementDriver.IsChildOf(avatarRoot) || placementDriver == avatarRoot)
                throw new ArgumentException("Contact placement driver must be inside the avatar root.", nameof(placementDriver));
            if (cloneIndex < 1) throw new ArgumentOutOfRangeException(nameof(cloneIndex));
            if (string.IsNullOrWhiteSpace(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder))
                throw new ArgumentException("Generated folder must exist in the AssetDatabase.", nameof(outputFolder));
            string senderTag = tag?.Trim();
            if (string.IsNullOrEmpty(senderTag))
                throw new ArgumentException("Set a matching Contact Sender tag (for example HandL). Add a VRCContactSender with this tag to the other avatar if it has no matching sender.", nameof(tag));
            if (!allowSelf && !allowOthers)
                throw new ArgumentException("Enable self contacts or other-avatar contacts; both Contact Receiver permissions are off.");
            string safePrefix = SafeName(uniquePrefix);
            if (string.IsNullOrEmpty(safePrefix)) throw new ArgumentException("Unique prefix must contain letters or digits.", nameof(uniquePrefix));
            if (avatarRoot.Find(safePrefix + " contact"))
                throw new InvalidOperationException($"Avatar already contains '{safePrefix} contact'. Choose another unique prefix or remove the existing Contact Tracker output.");

            var placement = placementDriver.GetComponent<VRCParentConstraint>();
            if (!placement || placement.Sources.Count < 1 || !placement.Sources[0].SourceTransform)
                throw new InvalidOperationException("Remote contact attachment needs a placement driver with a live original-anchor source. Regenerate the clone before enabling it.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var sourceFx = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!prefab || !sourceFx)
                throw new InvalidOperationException("Bundled VRLabs Contact Tracker assets are missing. Reinstall nxclone.");
            if (sourceFx.parameters.Length != 8 || sourceFx.layers.Length != 2)
                throw new InvalidOperationException("Bundled Contact Tracker controller contract changed; update the adapter before using this asset version.");

            string controlSourceName = "ContactTracker/Control";
            var controlSource = sourceFx.parameters.SingleOrDefault(p => p.name == controlSourceName);
            if (controlSource == null || controlSource.type != AnimatorControllerParameterType.Bool)
                throw new InvalidOperationException("Contact Tracker controller is missing its boolean Control parameter.");

            var wrapper = new GameObject(safePrefix + " contact");
            wrapper.transform.SetParent(avatarRoot, false);
            GameObject tracker = null;
            GameObject sourceHolder = null;
            Transform detachedTrackerTarget = null;
            var originalLayers = rootFx.layers;
            var originalFxParameters = rootFx.parameters;
            var originalSources = placement.Sources.ToArray();
            string rootFxPath = AssetDatabase.GetAssetPath(rootFx);
            var originalFxSubassets = string.IsNullOrEmpty(rootFxPath)
                ? new HashSet<UnityEngine.Object>()
                : new HashSet<UnityEngine.Object>(AssetDatabase.LoadAllAssetsAtPath(rootFxPath));
            var originalFolderAssets = new HashSet<string>(AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith(outputFolder.TrimEnd('/') + "/", StringComparison.Ordinal)), StringComparer.Ordinal);
            var sourceParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var sourceMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            sourceParameters.parameters = sourceFx.parameters.Select(p => new VRCExpressionParameters.Parameter
            {
                name = p.name,
                valueType = p.type == AnimatorControllerParameterType.Bool
                    ? VRCExpressionParameters.ValueType.Bool
                    : p.type == AnimatorControllerParameterType.Int
                        ? VRCExpressionParameters.ValueType.Int : VRCExpressionParameters.ValueType.Float,
                defaultValue = p.type == AnimatorControllerParameterType.Bool ? (p.defaultBool ? 1f : 0f) :
                    p.type == AnimatorControllerParameterType.Int ? p.defaultInt : p.defaultFloat,
                saved = false,
                networkSynced = p.name == controlSourceName
            }).ToArray();
            sourceMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Contact attach",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = controlSourceName },
                value = 1f
            });

            try
            {
                tracker = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (!tracker) throw new InvalidOperationException("Could not instantiate the bundled Contact Tracker prefab.");
                PrefabUtility.UnpackPrefabInstance(tracker, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                tracker.name = TrackerRootName;
                tracker.transform.SetParent(wrapper.transform, false);
                var trackingPoint = tracker.transform.Find("Tracking Points");
                var trackerTarget = tracker.transform.Find("Tracker Target");
                if (!trackingPoint || !trackerTarget)
                    throw new InvalidOperationException("Contact Tracker prefab must contain Tracking Points and Tracker Target.");

                var receivers = tracker.GetComponentsInChildren<Component>(true)
                    .Where(c => c && c.GetType().FullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver")
                    .ToArray();
                if (receivers.Length != 6)
                    throw new InvalidOperationException($"Contact Tracker expected six VRChat Contact Receivers, found {receivers.Length}. Check that VRChat Avatars SDK 3.7+ is installed.");
                foreach (var receiver in receivers) ConfigureReceiver(receiver, senderTag, allowSelf, allowOthers);

                // The original anchor is the live fallback used by Contact Tracker and by the
                // placement driver's source 0. Keeping this target outside the tracker avoids
                // a constraint cycle while still following the avatar's current anchor.
                trackerTarget.SetParent(placement.Sources[0].SourceTransform, false);
                detachedTrackerTarget = trackerTarget;
                var marker = tracker.transform.Find("Container/Cube");
                if (marker) UnityEngine.Object.DestroyImmediate(marker.gameObject);

                sourceHolder = new GameObject("nxclone contact source");
                sourceHolder.hideFlags = HideFlags.HideAndDontSave;
                var sourceTracker = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (!sourceTracker) throw new InvalidOperationException("Could not instantiate the Contact Tracker source hierarchy.");
                PrefabUtility.UnpackPrefabInstance(sourceTracker, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                sourceTracker.name = TrackerRootName;
                sourceTracker.transform.SetParent(sourceHolder.transform, false);

                int firstTrackerLayer = rootFx.layers.Length;
                var merge = NxCloneSourceFx.Merge(rootFx, sourceFx, sourceHolder.transform, avatarRoot,
                    wrapper.transform, cloneIndex, sourceParameters, sourceMenu, rootParameters, outputFolder,
                    includeStateLock: false);
                string parameterName = merge.parameterNames[controlSourceName];
                if (!allowOthers) DisableOtherSenderCurves(rootFx, firstTrackerLayer, sourceFx.layers.Length);

                // Contact Tracker owns contact selection. When enabled this layer mutes every
                // existing placement source. Off restores unanimated source defaults and
                // mutes the tracker; recorded-trajectory layers retain their owned weights.
                int contactSourceIndex = placement.Sources.Count;
                var originalWeights = originalSources.Select(source => source.Weight).ToArray();
                placement.Sources.Add(new VRCConstraintSource(trackingPoint, 0f));
                placement.ApplyConfigurationChanges();
                AddPlacementLayer(rootFx, outputFolder, avatarRoot, placementDriver,
                    parameterName, contactSourceIndex, cloneIndex, originalWeights);

                return new Result
                {
                    root = wrapper,
                    trackingPoint = trackingPoint,
                    controller = rootFx,
                    menu = merge.menu,
                    parameters = merge.parameters,
                    parameterNames = merge.parameterNames,
                    parameterName = parameterName,
                    remoteAttachBoolParameter = parameterName,
                    senderTag = senderTag,
                    requiredSenderHelp = SenderHelp(senderTag),
                    syncedParameterCost = merge.syncedParameterCost,
                    writeDefaultsOnLayers = new[]
                    {
                        $"nxclone {cloneIndex} Contact Tracker Control",
                        $"nxclone {cloneIndex} Contact Tracker Blend Tree"
                    }
                };
            }
            catch
            {
                rootFx.layers = originalLayers;
                rootFx.parameters = originalFxParameters;
                while (placement.Sources.Count > originalSources.Length)
                    placement.Sources.RemoveAt(placement.Sources.Count - 1);
                placement.ApplyConfigurationChanges();
                EditorUtility.SetDirty(rootFx);
                if (!string.IsNullOrEmpty(rootFxPath))
                    foreach (var subasset in AssetDatabase.LoadAllAssetsAtPath(rootFxPath).Where(asset => asset && !originalFxSubassets.Contains(asset)))
                        UnityEngine.Object.DestroyImmediate(subasset, true);
                foreach (var path in AssetDatabase.GetAllAssetPaths()
                             .Where(path => path.StartsWith(outputFolder.TrimEnd('/') + "/", StringComparison.Ordinal) && !originalFolderAssets.Contains(path))
                             .OrderByDescending(path => path.Length).ToArray())
                    AssetDatabase.DeleteAsset(path);
                if (detachedTrackerTarget) UnityEngine.Object.DestroyImmediate(detachedTrackerTarget.gameObject);
                if (tracker) UnityEngine.Object.DestroyImmediate(tracker);
                if (wrapper) UnityEngine.Object.DestroyImmediate(wrapper);
                AssetDatabase.SaveAssets();
                throw;
            }
            finally
            {
                if (sourceHolder) UnityEngine.Object.DestroyImmediate(sourceHolder);
                UnityEngine.Object.DestroyImmediate(sourceParameters);
                UnityEngine.Object.DestroyImmediate(sourceMenu);
            }
        }

        public static string SenderHelp(string tag) => tag == "HandL" || tag == "HandR" || tag == "FootL" || tag == "FootR" || tag == "Head" || tag == "Torso" || tag == "Hand" || tag == "Foot"
            ? $"'{tag}' is a built-in body contact tag. Humanoid avatars normally provide it automatically; both users must allow Avatar Dynamics contacts."
            : $"The other avatar needs a Contact Sender tagged '{tag}' (case-sensitive), and both users must allow Avatar Dynamics contacts.";

        static void ConfigureReceiver(Component receiver, string tag, bool allowSelf, bool allowOthers)
        {
            var serialized = new SerializedObject(receiver);
            var tags = serialized.FindProperty("collisionTags");
            var self = serialized.FindProperty("allowSelf");
            var others = serialized.FindProperty("allowOthers");
            var localOnly = serialized.FindProperty("localOnly");
            if (tags == null || !tags.isArray || self == null || others == null || localOnly == null)
                throw new InvalidOperationException("This VRChat SDK Contact Receiver has an unsupported serialized contract; update nxclone's contact adapter.");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).stringValue = tag;
            self.boolValue = allowSelf;
            others.boolValue = allowOthers;
            localOnly.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(receiver);
        }

        static void AddPlacementLayer(AnimatorController fx, string folder, Transform avatarRoot,
            Transform driver, string parameter, int trackedSourceIndex, int cloneIndex,
            float[] originalWeights)
        {
            if (fx.parameters.Any(p => p.name == parameter) == false)
                throw new InvalidOperationException("Contact attachment parameter was not merged into root FX.");
            var path = AnimationUtility.CalculateTransformPath(driver, avatarRoot);
            var fxOwnedSources = new HashSet<int>();
            foreach (var clip in fx.animationClips.Where(clip => clip))
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(binding =>
                    binding.path == path && binding.type == typeof(VRCParentConstraint)))
                    if (TrySourceWeightIndex(binding.propertyName, out int index)) fxOwnedSources.Add(index);
            var live = NewWeightClip(folder, $"nxclone contact {cloneIndex} live", path,
                trackedSourceIndex, false, originalWeights, fxOwnedSources);
            var tracked = NewWeightClip(folder, $"nxclone contact {cloneIndex} tracked", path,
                trackedSourceIndex, true, originalWeights, fxOwnedSources);
            var machine = new AnimatorStateMachine { name = $"nxclone contact anchor {cloneIndex}" };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var liveState = machine.AddState("Original anchor");
            liveState.motion = live;
            liveState.writeDefaultValues = false;
            var trackedState = machine.AddState("Contact target");
            trackedState.motion = tracked;
            trackedState.writeDefaultValues = false;
            machine.defaultState = liveState;
            AddToggle(liveState.AddTransition(trackedState), AnimatorConditionMode.If, parameter);
            AddToggle(trackedState.AddTransition(liveState), AnimatorConditionMode.IfNot, parameter);
            fx.AddLayer(new AnimatorControllerLayer
            {
                name = $"nxclone contact anchor {cloneIndex}",
                defaultWeight = 1f,
                stateMachine = machine
            });
            EditorUtility.SetDirty(fx);
            AssetDatabase.SaveAssets();
        }

        static void DisableOtherSenderCurves(AnimatorController controller, int firstLayer, int layerCount)
        {
            var clips = new HashSet<AnimationClip>();
            var layers = controller.layers;
            for (int i = firstLayer; i < firstLayer + layerCount; i++)
                CollectClips(layers[i].stateMachine, clips, new HashSet<AnimatorStateMachine>(), new HashSet<BlendTree>());
            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)
                             .Where(b => b.type.FullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver" && b.propertyName == "allowOthers"))
                    AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f, 0f));
                EditorUtility.SetDirty(clip);
            }
        }

        static void CollectClips(AnimatorStateMachine machine, HashSet<AnimationClip> clips,
            HashSet<AnimatorStateMachine> seenMachines, HashSet<BlendTree> seenTrees)
        {
            if (!machine || !seenMachines.Add(machine)) return;
            foreach (var state in machine.states) CollectClips(state.state.motion, clips, seenTrees);
            foreach (var child in machine.stateMachines) CollectClips(child.stateMachine, clips, seenMachines, seenTrees);
        }

        static void CollectClips(Motion motion, HashSet<AnimationClip> clips, HashSet<BlendTree> seenTrees)
        {
            if (motion is AnimationClip clip) clips.Add(clip);
            else if (motion is BlendTree tree && seenTrees.Add(tree))
                foreach (var child in tree.children) CollectClips(child.motion, clips, seenTrees);
        }

        static AnimationClip NewWeightClip(string folder, string name, string path, int trackedIndex,
            bool track, float[] originalWeights, HashSet<int> fxOwnedSources)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f };
            if (track)
                for (int i = 0; i < trackedIndex; i++) SetWeight(clip, path, i, 0f);
            else
                for (int i = 0; i < trackedIndex; i++)
                    if (!fxOwnedSources.Contains(i)) SetWeight(clip, path, i, originalWeights[i]);
            SetWeight(clip, path, trackedIndex, track ? 1f : 0f);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeName(name)}.anim");
            AssetDatabase.CreateAsset(clip, assetPath);
            return clip;
        }

        static bool TrySourceWeightIndex(string property, out int index)
        {
            const string prefix = "Sources.source";
            const string suffix = ".Weight";
            index = -1;
            return property.StartsWith(prefix, StringComparison.Ordinal) && property.EndsWith(suffix, StringComparison.Ordinal) &&
                int.TryParse(property.Substring(prefix.Length, property.Length - prefix.Length - suffix.Length), out index);
        }

        static void SetWeight(AnimationClip clip, string path, int sourceIndex, float value)
        {
            var curve = AnimationCurve.Constant(0f, 1f, value);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(VRCParentConstraint), $"Sources.source{sourceIndex}.Weight"), curve);
        }

        static void AddToggle(AnimatorStateTransition transition, AnimatorConditionMode mode, string parameter)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(mode, 0f, parameter);
        }

        static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Trim().Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray();
            return new string(chars).Trim('_');
        }
    }
}
