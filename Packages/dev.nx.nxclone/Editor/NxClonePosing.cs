using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace nxclone
{
    /// <summary>Native PhysBone chain posing for clone limbs; this is manual posing, not IK.</summary>
    public static class NxClonePosing
    {
        public sealed class Result
        {
            public string ParameterName;
            public string LayerName;
            public AnimationClip PoseOn;
            public AnimationClip Live;
            public AnimationClip Frozen;
            public AnimationClip Hidden;
            public int PhysBoneCount;
            public int ConstraintHostCount;
        }

        public sealed class FreezeResult
        {
            public AnimationClip Live;
            public AnimationClip Frozen;
            public GameObject ControlHost;
            public int ConstraintCount;
            public string LayerName;
        }

        sealed class Chain
        {
            public string name;
            public HumanBodyBones root;
            public HumanBodyBones middle;
            public HumanBodyBones end;
        }

        static readonly Chain[] Chains = {
            new Chain { name = "left arm", root = HumanBodyBones.LeftUpperArm, middle = HumanBodyBones.LeftLowerArm, end = HumanBodyBones.LeftHand },
            new Chain { name = "right arm", root = HumanBodyBones.RightUpperArm, middle = HumanBodyBones.RightLowerArm, end = HumanBodyBones.RightHand },
            new Chain { name = "left leg", root = HumanBodyBones.LeftUpperLeg, middle = HumanBodyBones.LeftLowerLeg, end = HumanBodyBones.LeftFoot },
            new Chain { name = "right leg", root = HumanBodyBones.RightUpperLeg, middle = HumanBodyBones.RightLowerLeg, end = HumanBodyBones.RightFoot }
        };

        /// <summary>Adds upload-whitelisted PhysBones to the clone and creates its controller clips.</summary>
        public static Result Configure(Animator sourceAnimator, Transform sourceRoot, Transform cloneRoot,
            string generatedFolder, int cloneIndex, string parameterName, Transform avatarRootOverride = null)
        {
            if (!sourceAnimator || !sourceAnimator.avatar || !sourceAnimator.avatar.isHuman)
                throw new InvalidOperationException("Interactive limb posing requires a valid humanoid source Animator.");
            if (!sourceRoot || sourceAnimator.transform != sourceRoot || !cloneRoot)
                throw new InvalidOperationException("Interactive limb posing needs the source Animator root and clone root.");
            var avatarAnimator = cloneRoot.GetComponentInParent<Animator>();
            var avatarRoot = avatarRootOverride ? avatarRootOverride : avatarAnimator ? avatarAnimator.transform : null;
            if (!avatarRoot) throw new InvalidOperationException("Interactive posing cannot find the root avatar Animator for animation paths.");
            if (string.IsNullOrWhiteSpace(generatedFolder) || !AssetDatabase.IsValidFolder(generatedFolder))
                throw new InvalidOperationException("Interactive posing output folder must already exist in the Unity project.");
            if (string.IsNullOrWhiteSpace(parameterName)) throw new InvalidOperationException("Interactive posing needs a unique FX bool parameter name.");

            var physBoneType = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            if (physBoneType == null || !typeof(Behaviour).IsAssignableFrom(physBoneType))
                throw new InvalidOperationException("VRChat SDK PhysBone component is missing. Install/update the VRChat Avatars SDK, then rebuild.");

            var mapped = new List<(Chain chain, Transform root, Transform[] bones, Transform[] ignored)>();
            var missing = new List<string>();
            foreach (var chain in Chains)
            {
                var sourceTop = sourceAnimator.GetBoneTransform(chain.root);
                var sourceMid = sourceAnimator.GetBoneTransform(chain.middle);
                var sourceEnd = sourceAnimator.GetBoneTransform(chain.end);
                var top = Map(sourceRoot, cloneRoot, sourceTop);
                var mid = Map(sourceRoot, cloneRoot, sourceMid);
                var end = Map(sourceRoot, cloneRoot, sourceEnd);
                if (!top || !mid || !end || !mid.IsChildOf(top) || !end.IsChildOf(mid))
                {
                    missing.Add(chain.name);
                    continue;
                }
                var bones = PathNodes(top, mid).Concat(PathNodes(mid, end).Skip(1)).ToArray();
                mapped.Add((chain, top, bones, IgnoredBranches(bones)));
            }
            if (missing.Count != 0)
                throw new InvalidOperationException("Interactive posing requires all clone arm and leg chains (upper arm/lower arm/hand and upper leg/lower leg/foot). Missing or incompatible: " + string.Join(", ", missing));

            var hosts = new List<GameObject>();
            var constraintHosts = new List<GameObject>();
            try
            {
                foreach (var item in mapped)
                {
                    var host = new GameObject("nxclone pose " + item.chain.name);
                    host.transform.SetParent(cloneRoot, false);
                    host.SetActive(false);
                    hosts.Add(host);
                    var component = host.AddComponent(physBoneType);
                    ConfigurePhysBone(component, item.root, item.ignored);

                    var constraintHost = new GameObject("nxclone constraints " + item.chain.name);
                    constraintHost.transform.SetParent(cloneRoot, false);
                    constraintHost.SetActive(false);
                    constraintHosts.Add(constraintHost);
                    var perBoneHosts = new Dictionary<Transform, GameObject>();
                    foreach (var bone in item.bones)
                    {
                        bool hasConstraints = bone.GetComponents<VRCRotationConstraint>().Length > 0 ||
                            bone.GetComponents<VRCPositionConstraint>().Length > 0;
                        if (!hasConstraints) continue;
                        var boneHost = NewConstraintTargetHost(constraintHost, bone, perBoneHosts.Count);
                        perBoneHosts.Add(bone, boneHost);
                        foreach (var constraint in bone.GetComponents<VRCRotationConstraint>())
                            MoveConstraint(constraint, boneHost, bone);
                        foreach (var constraint in bone.GetComponents<VRCPositionConstraint>())
                            MoveConstraint(constraint, boneHost, bone);
                    }
                    constraintHost.SetActive(true);
                }

                var result = new Result {
                    ParameterName = parameterName,
                    LayerName = "nxclone " + cloneIndex + " interactive limb posing",
                    PhysBoneCount = hosts.Count,
                    ConstraintHostCount = constraintHosts.Count,
                    PoseOn = new AnimationClip { name = $"nxclone {cloneIndex} posing on" },
                    Live = new AnimationClip { name = $"nxclone {cloneIndex} posing off" },
                    Frozen = new AnimationClip { name = $"nxclone {cloneIndex} posing frozen" },
                    Hidden = new AnimationClip { name = $"nxclone {cloneIndex} posing hidden" }
                };
                for (int i = 0; i < mapped.Count; i++)
                {
                    var item = mapped[i];
                    string path = PathOf(avatarRoot, hosts[i].transform);
                    SetCurve(result.PoseOn, path, typeof(GameObject), "m_IsActive", 1);
                    SetCurve(result.Live, path, typeof(GameObject), "m_IsActive", 0);
                    SetCurve(result.Frozen, path, typeof(GameObject), "m_IsActive", 0);
                    SetCurve(result.Hidden, path, typeof(GameObject), "m_IsActive", 0);
                    string constraintsPath = PathOf(avatarRoot, constraintHosts[i].transform);
                    SetCurve(result.PoseOn, constraintsPath, typeof(GameObject), "m_IsActive", 0);
                    SetCurve(result.Live, constraintsPath, typeof(GameObject), "m_IsActive", 1);
                    SetCurve(result.Frozen, constraintsPath, typeof(GameObject), "m_IsActive", 0);
                    SetCurve(result.Hidden, constraintsPath, typeof(GameObject), "m_IsActive", 0);
                }
                SaveClip(result.PoseOn, generatedFolder, $"clone-{cloneIndex}-posing-on.anim");
                SaveClip(result.Live, generatedFolder, $"clone-{cloneIndex}-posing-off.anim");
                SaveClip(result.Frozen, generatedFolder, $"clone-{cloneIndex}-posing-frozen.anim");
                SaveClip(result.Hidden, generatedFolder, $"clone-{cloneIndex}-posing-hidden.anim");
                return result;
            }
            catch
            {
                foreach (var host in hosts) if (host) UnityEngine.Object.DestroyImmediate(host);
                foreach (var host in constraintHosts) if (host) UnityEngine.Object.DestroyImmediate(host);
                throw;
            }
        }

        /// <summary>Moves remaining clone constraints to a native switchable host for whole-body pose freeze.</summary>
        public static FreezeResult ConfigureFreeze(Transform cloneRoot, Transform avatarRoot,
            string generatedFolder, int cloneIndex)
        {
            if (!cloneRoot || !avatarRoot || (cloneRoot != avatarRoot && !cloneRoot.IsChildOf(avatarRoot)))
                throw new InvalidOperationException("Whole-body pose freeze requires a clone beneath its avatar root.");
            if (string.IsNullOrWhiteSpace(generatedFolder) || !AssetDatabase.IsValidFolder(generatedFolder))
                throw new InvalidOperationException("Whole-body pose-freeze output folder must already exist in the Unity project.");

            var constraints = cloneRoot.GetComponentsInChildren<VRCRotationConstraint>(true)
                .Where(component => !IsOwnershipHost(component.transform))
                .Cast<Component>()
                .Concat(cloneRoot.GetComponentsInChildren<VRCPositionConstraint>(true)
                    .Where(component => !IsOwnershipHost(component.transform)).Cast<Component>())
                .ToArray();
            var result = new FreezeResult {
                Live = new AnimationClip { name = $"nxclone {cloneIndex} whole pose live" },
                Frozen = new AnimationClip { name = $"nxclone {cloneIndex} whole pose frozen" },
                ConstraintCount = constraints.Length,
                LayerName = $"nxclone {cloneIndex} whole pose freeze"
            };
            if (constraints.Length == 0)
            {
                UnityEngine.Object.DestroyImmediate(result.Live);
                UnityEngine.Object.DestroyImmediate(result.Frozen);
                result.Live = result.Frozen = null;
                return result;
            }

            var host = new GameObject("nxclone freeze constraints " + cloneIndex);
            host.transform.SetParent(cloneRoot, false);
            host.SetActive(false);
            result.ControlHost = host;
            try
            {
                var perBoneHosts = new Dictionary<Transform, GameObject>();
                foreach (var constraint in constraints)
                {
                    var target = GetConstraintTarget(constraint);
                    if (!target || !target.IsChildOf(cloneRoot))
                        throw new InvalidOperationException("Whole-body freeze found a constraint whose target is outside the clone; refusing to alter a source avatar.");
                    if (!perBoneHosts.TryGetValue(target, out var boneHost))
                    {
                        boneHost = NewConstraintTargetHost(host, target, perBoneHosts.Count);
                        perBoneHosts.Add(target, boneHost);
                    }
                    MoveConstraint(constraint, boneHost, target);
                }
                host.SetActive(true);
                string path = PathOf(avatarRoot, host.transform);
                SetCurve(result.Live, path, typeof(GameObject), "m_IsActive", 1);
                SetCurve(result.Frozen, path, typeof(GameObject), "m_IsActive", 0);
                SaveClip(result.Live, generatedFolder, $"clone-{cloneIndex}-whole-pose-live.anim");
                SaveClip(result.Frozen, generatedFolder, $"clone-{cloneIndex}-whole-pose-frozen.anim");
                return result;
            }
            catch
            {
                if (host) UnityEngine.Object.DestroyImmediate(host);
                if (result.Live) UnityEngine.Object.DestroyImmediate(result.Live);
                if (result.Frozen) UnityEngine.Object.DestroyImmediate(result.Frozen);
                throw;
            }
        }

        /// <summary>Adds the poser ownership layer after clone visibility and pose-freeze layers.</summary>
        public static void AddLayer(AnimatorController controller, Result result, string freezeParameter, bool writeDefaults,
            string visibleParameter = null)
        {
            if (!controller || result == null || !result.PoseOn || !result.Live || !result.Frozen || !result.Hidden)
                throw new InvalidOperationException("Interactive posing clips or FX controller are missing.");
            RequireBool(controller, result.ParameterName);
            if (string.IsNullOrEmpty(visibleParameter))
                visibleParameter = controller.parameters.Select(item => item.name)
                    .FirstOrDefault(name => name.StartsWith("nxclone_enabled_" + result.LayerName.Substring("nxclone ".Length,
                        result.LayerName.IndexOf(" interactive", StringComparison.Ordinal) - "nxclone ".Length), StringComparison.Ordinal));
            if (string.IsNullOrEmpty(visibleParameter))
                throw new InvalidOperationException("Interactive posing FX layer requires this clone's nxclone_enabled_N bool parameter.");
            RequireBool(controller, visibleParameter);
            if (!string.IsNullOrEmpty(freezeParameter)) RequireBool(controller, freezeParameter);

            controller.AddLayer(result.LayerName);
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;
            var machine = layer.stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            var posing = machine.AddState("Posing"); posing.motion = result.PoseOn; posing.writeDefaultValues = writeDefaults;
            var live = machine.AddState("Live"); live.motion = result.Live; live.writeDefaultValues = writeDefaults;
            var hidden = machine.AddState("Hidden"); hidden.motion = result.Hidden; hidden.writeDefaultValues = writeDefaults;
            var frozen = string.IsNullOrEmpty(freezeParameter) ? null : machine.AddState("Frozen");
            if (frozen) { frozen.motion = result.Frozen; frozen.writeDefaultValues = writeDefaults; }
            machine.defaultState = live;

            AddAnyState(machine, posing, AnimatorConditionMode.If, visibleParameter, AnimatorConditionMode.If, result.ParameterName);
            AddAnyState(machine, live, AnimatorConditionMode.If, visibleParameter, AnimatorConditionMode.IfNot, result.ParameterName,
                string.IsNullOrEmpty(freezeParameter) ? null : freezeParameter, false);
            if (frozen) AddAnyState(machine, frozen, AnimatorConditionMode.If, visibleParameter, AnimatorConditionMode.IfNot,
                result.ParameterName, freezeParameter, true);
            AddAnyState(machine, hidden, AnimatorConditionMode.IfNot, visibleParameter, null, null);
        }

        public static void AddFreezeLayer(AnimatorController controller, FreezeResult result, string freezeParameter,
            bool writeDefaults)
        {
            if (!controller || result == null || !result.Live || !result.Frozen || result.ConstraintCount <= 0)
                throw new InvalidOperationException("Whole-body freeze clips or FX controller are missing.");
            RequireBool(controller, freezeParameter);
            controller.AddLayer(result.LayerName);
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;
            var machine = layer.stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            var live = machine.AddState("Live"); live.motion = result.Live; live.writeDefaultValues = writeDefaults;
            var frozen = machine.AddState("Frozen"); frozen.motion = result.Frozen; frozen.writeDefaultValues = writeDefaults;
            machine.defaultState = live;
            AddAnyState(machine, live, AnimatorConditionMode.IfNot, freezeParameter, null, null);
            AddAnyState(machine, frozen, AnimatorConditionMode.If, freezeParameter, null, null);
        }

        static void ConfigurePhysBone(Component component, Transform root, Transform[] ignored)
        {
            var serialized = new SerializedObject(component);
            SetObject(serialized, "rootTransform", root);
            var ignore = serialized.FindProperty("ignoreTransforms");
            if (ignore == null || !ignore.isArray) throw new InvalidOperationException("Installed VRCPhysBone schema is missing ignoreTransforms; cannot isolate limb branches safely.");
            ignore.arraySize = ignored.Length;
            for (int i = 0; i < ignored.Length; i++) ignore.GetArrayElementAtIndex(i).objectReferenceValue = ignored[i];
            SetAdvancedBool(serialized, "allowGrabbing");
            SetAdvancedBool(serialized, "allowPosing");
            SetBoolean(serialized, "grabFilter.allowSelf", true);
            SetBoolean(serialized, "grabFilter.allowOthers", true);
            SetBoolean(serialized, "poseFilter.allowSelf", true);
            SetBoolean(serialized, "poseFilter.allowOthers", true);
            SetBoolean(serialized, "snapToHand", true);
            SetFloat(serialized, "grabMovement", 1f);
            SetFloat(serialized, "radius", 0.08f);
            SetFloat(serialized, "maxStretch", 0f);
            SetFloat(serialized, "maxSquish", 0f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void MoveConstraint(Component original, GameObject host, Transform target)
        {
            if (!original || !host || !target) throw new InvalidOperationException("Cannot move a clone limb constraint without its host and target.");
            var copy = host.AddComponent(original.GetType());
            EditorUtility.CopySerialized(original, copy);
            var targetField = copy.GetType().GetField("TargetTransform", BindingFlags.Instance | BindingFlags.Public);
            if (targetField == null || targetField.FieldType != typeof(Transform))
            {
                UnityEngine.Object.DestroyImmediate(copy);
                throw new InvalidOperationException("Installed VRChat constraint schema has no public Transform TargetTransform; cannot safely switch clone limb ownership.");
            }
            targetField.SetValue(copy, target);
            var apply = copy.GetType().GetMethod("ApplyConfigurationChanges", BindingFlags.Instance | BindingFlags.Public);
            if (apply == null || apply.GetParameters().Length != 0)
            {
                UnityEngine.Object.DestroyImmediate(copy);
                throw new InvalidOperationException("Installed VRChat constraint schema has no supported parameterless ApplyConfigurationChanges; cannot safely move clone limb constraints.");
            }
            apply.Invoke(copy, null);
            UnityEngine.Object.DestroyImmediate(original);
        }

        static GameObject NewConstraintTargetHost(GameObject owner, Transform target, int index)
        {
            var host = new GameObject($"bone-{index + 1:000} {target.name}");
            host.transform.SetParent(owner.transform, false);
            return host;
        }

        static Transform GetConstraintTarget(Component constraint)
        {
            var field = constraint.GetType().GetField("TargetTransform", BindingFlags.Instance | BindingFlags.Public);
            if (field == null || field.FieldType != typeof(Transform))
                throw new InvalidOperationException("Installed VRChat constraint schema has no public Transform TargetTransform; cannot safely set up pose freeze.");
            return (Transform)field.GetValue(constraint) ?? constraint.transform;
        }

        static bool IsOwnershipHost(Transform item)
        {
            for (var current = item; current; current = current.parent)
                if (current.name.StartsWith("nxclone constraints ", StringComparison.Ordinal) ||
                    current.name.StartsWith("nxclone freeze constraints ", StringComparison.Ordinal)) return true;
            return false;
        }

        static void SetAdvancedBool(SerializedObject serialized, string path)
        {
            var property = serialized.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.Enum)
                throw new InvalidOperationException("Installed VRCPhysBone schema is missing " + path + "; cannot safely enable grabbing/posing.");
            var names = property.enumNames;
            int index = Array.FindIndex(names, name => string.Equals(name, "True", StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException("Installed VRCPhysBone " + path + " enum has no explicit True value; update the helper for this SDK schema.");
            property.enumValueIndex = index;
        }

        static void SetObject(SerializedObject serialized, string path, UnityEngine.Object value)
        {
            var property = serialized.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                throw new InvalidOperationException("Installed VRCPhysBone schema is missing " + path + ".");
            property.objectReferenceValue = value;
        }

        static void SetBoolean(SerializedObject serialized, string path, bool value)
        {
            var property = serialized.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
                throw new InvalidOperationException("Installed VRCPhysBone schema is missing " + path + ".");
            property.boolValue = value;
        }

        static void SetFloat(SerializedObject serialized, string path, float value)
        {
            var property = serialized.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.Float)
                throw new InvalidOperationException("Installed VRCPhysBone schema is missing " + path + ".");
            property.floatValue = value;
        }

        static void AddAnyState(AnimatorStateMachine machine, AnimatorState destination,
            AnimatorConditionMode firstMode, string firstParameter, AnimatorConditionMode? secondMode, string secondParameter,
            string thirdParameter = null, bool thirdValue = false)
        {
            var transition = machine.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = 0;
            transition.canTransitionToSelf = false;
            transition.AddCondition(firstMode, 0, firstParameter);
            if (secondMode.HasValue) transition.AddCondition(secondMode.Value, 0, secondParameter);
            if (!string.IsNullOrEmpty(thirdParameter))
                transition.AddCondition(thirdValue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, thirdParameter);
        }

        static void RequireBool(AnimatorController controller, string parameter)
        {
            if (string.IsNullOrEmpty(parameter) || !controller.parameters.Any(item => item.name == parameter && item.type == AnimatorControllerParameterType.Bool))
                throw new InvalidOperationException("Interactive posing FX layer requires bool parameter '" + parameter + "'.");
        }

        static Transform Map(Transform sourceRoot, Transform cloneRoot, Transform sourceBone)
        {
            if (!sourceBone || !sourceBone.IsChildOf(sourceRoot)) return null;
            return cloneRoot.Find(PathOf(sourceRoot, sourceBone));
        }

        static Transform[] IgnoredBranches(Transform[] chain)
        {
            var ignored = new HashSet<Transform>();
            for (int i = 0; i < chain.Length; i++)
            {
                var next = i + 1 < chain.Length ? chain[i + 1] : null;
                foreach (Transform child in chain[i]) if (child != next) ignored.Add(child);
            }
            return ignored.ToArray();
        }

        static Transform[] PathNodes(Transform from, Transform to)
        {
            var path = new List<Transform> { to };
            for (var current = to; current && current != from; current = current.parent)
                if (current.parent && current.parent != from) path.Add(current.parent);
            path.Add(from);
            path.Reverse();
            return path.ToArray();
        }

        static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, false)).FirstOrDefault(type => type != null);

        static string PathOf(Transform root, Transform item)
        {
            if (root == item) return string.Empty;
            var parts = new Stack<string>();
            for (var current = item; current && current != root; current = current.parent) parts.Push(current.name);
            return string.Join("/", parts.ToArray());
        }

        static void SetCurve(AnimationClip clip, string path, Type type, string property, float value)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property),
                new AnimationCurve(new Keyframe(0, value), new Keyframe(1f / 60f, value)));
        }

        static void SaveClip(AnimationClip clip, string folder, string fileName)
        {
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName);
            AssetDatabase.CreateAsset(clip, path);
        }
    }
}
