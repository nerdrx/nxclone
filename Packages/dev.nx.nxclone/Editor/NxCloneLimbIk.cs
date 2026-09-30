using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;

namespace nxclone
{
    /// <summary>
    /// Builds optional SDK-whitelisted FinalIK LimbIK components. The installed SDK stub
    /// only validates serialization; it does not run the FinalIK solver in the editor.
    /// </summary>
    public static class NxCloneLimbIk
    {
        public sealed class Result
        {
            public string ParameterName;
            public string LayerName;
            public string SelectorLayerName;
            public AnimationClip On;
            public AnimationClip Off;
            public Component[] Solvers;
            public GameObject[] GoalHandles;
            public GameObject[] BendHints;
        }

        struct Chain
        {
            public string name;
            public HumanBodyBones top, mid, end;
            public string goal;
        }

        static readonly Chain[] Chains = {
            new Chain { name = "left arm", top = HumanBodyBones.LeftUpperArm, mid = HumanBodyBones.LeftLowerArm, end = HumanBodyBones.LeftHand, goal = "LeftHand" },
            new Chain { name = "right arm", top = HumanBodyBones.RightUpperArm, mid = HumanBodyBones.RightLowerArm, end = HumanBodyBones.RightHand, goal = "RightHand" },
            new Chain { name = "left leg", top = HumanBodyBones.LeftUpperLeg, mid = HumanBodyBones.LeftLowerLeg, end = HumanBodyBones.LeftFoot, goal = "LeftFoot" },
            new Chain { name = "right leg", top = HumanBodyBones.RightUpperLeg, mid = HumanBodyBones.RightLowerLeg, end = HumanBodyBones.RightFoot, goal = "RightFoot" }
        };

        /// <summary>Creates four dormant solvers and grabbable goal handles under the frozen placement frame.</summary>
        public static Result Configure(Animator sourceAnimator, Transform sourceRoot, Transform cloneRoot,
            Transform placementDriver, Transform avatarRoot, string generatedFolder, int cloneIndex,
            string parameterName)
        {
            if (!sourceAnimator || !sourceAnimator.avatar || !sourceAnimator.avatar.isHuman ||
                !sourceRoot || sourceAnimator.transform != sourceRoot || !cloneRoot || !placementDriver || !avatarRoot)
                throw new InvalidOperationException("Limb IK requires a humanoid source, clone, placement driver, and avatar animation root.");
            if (!cloneRoot.IsChildOf(placementDriver) || placementDriver == cloneRoot ||
                (placementDriver != avatarRoot && !placementDriver.IsChildOf(avatarRoot)))
                throw new InvalidOperationException("Limb IK clone and placement driver must be beneath the avatar animation root.");
            if (!IsFrozenPlacementDriver(placementDriver))
                throw new InvalidOperationException("Limb IK goal handles must be parented beneath the native frozen clone-placement frame.");
            if (string.IsNullOrWhiteSpace(generatedFolder) || !AssetDatabase.IsValidFolder(generatedFolder))
                throw new InvalidOperationException("Limb IK output folder must already exist in the Unity project.");
            if (string.IsNullOrWhiteSpace(parameterName)) throw new InvalidOperationException("Limb IK needs a unique FX bool parameter name.");

            var ikType = FindType("RootMotion.FinalIK.LimbIK");
            var physBoneType = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            if (ikType == null || !typeof(Behaviour).IsAssignableFrom(ikType))
                throw new InvalidOperationException("The SDK-whitelisted FinalIK LimbIK stub is unavailable. Install a compatible VRChat Avatars SDK.");
            if (physBoneType == null || !typeof(Behaviour).IsAssignableFrom(physBoneType))
                throw new InvalidOperationException("VRChat SDK PhysBone component is unavailable.");
            var mapped = Chains.Select(chain => {
                var top = Map(sourceAnimator, sourceRoot, cloneRoot, chain.top);
                var mid = Map(sourceAnimator, sourceRoot, cloneRoot, chain.mid);
                var end = Map(sourceAnimator, sourceRoot, cloneRoot, chain.end);
                if (!top || !mid || !end || !mid.IsChildOf(top) || !end.IsChildOf(mid))
                    throw new InvalidOperationException("Clone is missing a mapped, nested humanoid chain: " + chain.name + ".");
                return (chain, top, mid, end);
            }).ToArray();

            var solverHost = new GameObject("nxclone IK " + cloneIndex);
            solverHost.transform.SetParent(cloneRoot, false);
            solverHost.SetActive(false);
            var handles = new GameObject[Chains.Length];
            var hints = new GameObject[Chains.Length];
            var solvers = new Component[Chains.Length];
            try
            {
                for (int i = 0; i < mapped.Length; i++)
                {
                    var item = mapped[i];
                    var axis = (item.end.position - item.mid.position).normalized;
                    if (axis.sqrMagnitude < 1e-8f) axis = (item.end.position - item.top.position).normalized;
                    if (axis.sqrMagnitude < 1e-8f) axis = Vector3.forward;
                    var goal = NewHandle(cloneRoot, "nxclone IK goal " + cloneIndex + " " + item.chain.name,
                        item.top.position);
                    handles[i] = goal;
                    var anchor = goal.AddComponent<VRCPositionConstraint>();
                    anchor.Sources.Add(new VRCConstraintSource(item.top, 1f));
                    anchor.ActivateConstraint();
                    anchor.ApplyConfigurationChanges();
                    ConfigurePhysBone(goal.AddComponent(physBoneType), goal.transform);
                    var tip = new GameObject("target").transform;
                    tip.SetParent(goal.transform, false);
                    tip.position = item.end.position;

                    Vector3 bend = BendDirection(item.top.position, item.mid.position, item.end.position);
                    float scale = Mathf.Max(Vector3.Distance(item.top.position, item.mid.position),
                        Vector3.Distance(item.mid.position, item.end.position));
                    var hint = new GameObject("nxclone IK bend " + cloneIndex + " " + item.chain.name);
                    hint.transform.SetParent(cloneRoot, true);
                    hint.transform.position = item.mid.position + bend * Mathf.Max(0.15f, scale * 1.5f);
                    hint.SetActive(false);
                    hints[i] = hint;

                    solvers[i] = solverHost.AddComponent(ikType);
                    ConfigureSolver(solvers[i], item.top, item.mid, item.end, tip, hint.transform,
                        item.chain.goal, cloneRoot);
                }

                var result = new Result {
                    ParameterName = parameterName,
                    LayerName = "nxclone " + cloneIndex + " native limb IK",
                    SelectorLayerName = "nxclone " + cloneIndex + " native limb IK selector",
                    Solvers = solvers,
                    GoalHandles = handles,
                    BendHints = hints,
                    On = new AnimationClip { name = "nxclone " + cloneIndex + " IK on" },
                    Off = new AnimationClip { name = "nxclone " + cloneIndex + " IK off" }
                };
                SetActiveCurve(result.On, avatarRoot, solverHost.transform, true);
                SetActiveCurve(result.Off, avatarRoot, solverHost.transform, false);
                for (int i = 0; i < handles.Length; i++)
                {
                    SetActiveCurve(result.On, avatarRoot, handles[i].transform, true);
                    SetActiveCurve(result.Off, avatarRoot, handles[i].transform, false);
                    SetActiveCurve(result.On, avatarRoot, hints[i].transform, true);
                    SetActiveCurve(result.Off, avatarRoot, hints[i].transform, false);
                }
                // The IK solver must own these limb bones while enabled. With Write Defaults off,
                // omitting the gate curves from Off lets the existing posing layer restore its state.
                foreach (var host in cloneRoot.GetComponentsInChildren<Transform>(true)
                    .Where(transform => transform.name.StartsWith("nxclone constraints ", StringComparison.Ordinal) ||
                        transform.name.StartsWith("nxclone freeze constraints ", StringComparison.Ordinal) ||
                        transform.name.StartsWith("nxclone pose ", StringComparison.Ordinal)))
                    SetActiveCurve(result.On, avatarRoot, host, false);
                AssetDatabase.CreateAsset(result.On, AssetDatabase.GenerateUniqueAssetPath(generatedFolder + $"/clone-{cloneIndex}-ik-on.anim"));
                AssetDatabase.CreateAsset(result.Off, AssetDatabase.GenerateUniqueAssetPath(generatedFolder + $"/clone-{cloneIndex}-ik-off.anim"));
                return result;
            }
            catch
            {
                if (solverHost) UnityEngine.Object.DestroyImmediate(solverHost);
                foreach (var item in handles) if (item) UnityEngine.Object.DestroyImmediate(item);
                foreach (var item in hints) if (item) UnityEngine.Object.DestroyImmediate(item);
                throw;
            }
        }

        public static void AddLayer(AnimatorController controller, Result result, bool writeDefaults,
            string freezeParameter = null, string enabledParameter = null,
            string playbackParameter = null, string visibleParameter = null)
        {
            if (!controller || result == null || !result.On || !result.Off)
                throw new InvalidOperationException("Limb IK clips or FX controller are missing.");
            if (!controller.parameters.Any(item => item.name == result.ParameterName && item.type == AnimatorControllerParameterType.Bool))
                throw new InvalidOperationException("Limb IK FX layer requires bool parameter '" + result.ParameterName + "'.");
            foreach (var guard in new[] { freezeParameter, enabledParameter, playbackParameter, visibleParameter })
                if (!string.IsNullOrEmpty(guard) && !controller.parameters.Any(item => item.name == guard && item.type == AnimatorControllerParameterType.Bool))
                    throw new InvalidOperationException("Limb IK selector guard requires bool parameter '" + guard + "'.");
            controller.AddLayer(result.LayerName);
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 0;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;
            var machine = layer.stateMachine;
            // ponytail: keep Off unbound so earlier posing/freeze layers own the FK gates again.
            // ponytail: content remains unbound while Off; the SDK selector owns its weight.
            var off = machine.AddState("Off"); off.motion = result.Off; off.writeDefaultValues = false;
            var on = machine.AddState("On"); on.motion = result.On; on.writeDefaultValues = false;
            machine.defaultState = off;
            AddTransition(machine, on, AnimatorConditionMode.If, result.ParameterName);
            AddTransition(machine, off, AnimatorConditionMode.IfNot, result.ParameterName);

            int contentLayerIndex = Array.FindIndex(controller.layers, candidate => candidate.name == result.LayerName);
            var selector = new AnimatorStateMachine { name = result.SelectorLayerName };
            AssetDatabase.AddObjectToAsset(selector, controller);
            var selectorOff = selector.AddState("Off"); selector.defaultState = selectorOff; selectorOff.writeDefaultValues = false;
            SetLayerWeight(selectorOff.AddStateMachineBehaviour<VRCAnimatorLayerControl>(), contentLayerIndex, 0f);
            var selectorOn = selector.AddState("On"); selectorOn.writeDefaultValues = false;
            SetLayerWeight(selectorOn.AddStateMachineBehaviour<VRCAnimatorLayerControl>(), contentLayerIndex, 1f);
            var enabled = new List<(string parameter, bool value)> { (result.ParameterName, true) };
            if (!string.IsNullOrEmpty(freezeParameter)) enabled.Add((freezeParameter, false));
            if (!string.IsNullOrEmpty(enabledParameter)) enabled.Add((enabledParameter, true));
            if (!string.IsNullOrEmpty(playbackParameter)) enabled.Add((playbackParameter, false));
            if (!string.IsNullOrEmpty(visibleParameter)) enabled.Add((visibleParameter, true));
            AddGuardTransition(selectorOff, selectorOn, enabled);
            foreach (var guard in enabled) AddGuardTransition(selectorOn, selectorOff,
                new[] { (guard.parameter, !guard.value) });
            controller.AddLayer(new AnimatorControllerLayer { name = result.SelectorLayerName, defaultWeight = 1f, stateMachine = selector });
        }

        static void ConfigureSolver(Component component, Transform top, Transform mid, Transform end,
            Transform target, Transform bend, string goal, Transform root)
        {
            var serialized = new SerializedObject(component);
            SetObject(serialized, "solver.bone1.transform", top);
            SetObject(serialized, "solver.bone2.transform", mid);
            SetObject(serialized, "solver.bone3.transform", end);
            SetObject(serialized, "solver.target", target);
            SetObject(serialized, "solver.bendGoal", bend);
            SetEnum(serialized, "solver.goal", goal);
            // Some SDK versions serialize root; leave automatic solver root when absent.
            var solverRoot = serialized.FindProperty("solver.root");
            if (solverRoot != null && solverRoot.propertyType == SerializedPropertyType.ObjectReference)
                solverRoot.objectReferenceValue = root;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigurePhysBone(Component component, Transform root)
        {
            var serialized = new SerializedObject(component);
            SetObject(serialized, "rootTransform", root);
            SetEnum(serialized, "allowGrabbing", "True");
            SetEnum(serialized, "allowPosing", "True");
            SetBool(serialized, "grabFilter.allowSelf", true);
            SetBool(serialized, "grabFilter.allowOthers", true);
            SetBool(serialized, "poseFilter.allowSelf", true);
            SetBool(serialized, "poseFilter.allowOthers", true);
            SetBool(serialized, "snapToHand", true);
            SetFloat(serialized, "grabMovement", 1f);
            SetFloat(serialized, "radius", 0.08f);
            SetFloat(serialized, "maxStretch", 0f);
            SetFloat(serialized, "maxSquish", 0f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject NewHandle(Transform parent, string name, Vector3 position)
        {
            var handle = new GameObject(name);
            handle.transform.SetParent(parent, true);
            handle.transform.position = position;
            handle.SetActive(false);
            return handle;
        }

        static Vector3 BendDirection(Vector3 top, Vector3 mid, Vector3 end)
        {
            var axis = end - top;
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.forward;
            var projected = top + Vector3.Project(mid - top, axis);
            var bend = mid - projected;
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(axis.normalized, Vector3.up);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(axis.normalized, Vector3.right);
            return bend.normalized;
        }

        static Transform Map(Animator animator, Transform sourceRoot, Transform cloneRoot, HumanBodyBones bone)
        {
            var source = animator.GetBoneTransform(bone);
            if (!source || !source.IsChildOf(sourceRoot)) return null;
            return cloneRoot.Find(AnimationUtility.CalculateTransformPath(source, sourceRoot));
        }

        static bool IsFrozenPlacementDriver(Transform driver)
        {
            for (var current = driver.parent; current; current = current.parent)
            {
                var parentConstraint = current.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint>();
                var scaleConstraint = current.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCScaleConstraint>();
                if (parentConstraint && scaleConstraint && parentConstraint.FreezeToWorld && scaleConstraint.FreezeToWorld)
                    return true;
            }
            return false;
        }

        static void SetActiveCurve(AnimationClip clip, Transform root, Transform target, bool active)
        {
            var path = AnimationUtility.CalculateTransformPath(target, root);
            float value = active ? 1 : 0;
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"),
                new AnimationCurve(new Keyframe(0, value), new Keyframe(1f / 60f, value)));
        }

        static void AddTransition(AnimatorStateMachine machine, AnimatorState state, AnimatorConditionMode mode, string parameter)
        {
            var transition = machine.AddAnyStateTransition(state);
            transition.hasExitTime = false;
            transition.duration = 0;
            transition.canTransitionToSelf = false;
            transition.AddCondition(mode, 0, parameter);
        }

        static void AddGuardTransition(AnimatorState from, AnimatorState to, IEnumerable<(string parameter, bool value)> guards)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0;
            foreach (var guard in guards)
                transition.AddCondition(guard.value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, guard.parameter);
        }

        static void SetLayerWeight(VRCAnimatorLayerControl behavior, int layer, float weight)
        {
            behavior.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            behavior.layer = layer;
            behavior.goalWeight = weight;
            behavior.blendDuration = 0;
        }

        static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name, false)).FirstOrDefault(type => type != null);

        static void SetObject(SerializedObject so, string path, UnityEngine.Object value)
        {
            var property = so.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                throw new InvalidOperationException("Installed SDK schema is missing Transform field " + path + ".");
            property.objectReferenceValue = value;
        }

        static void SetEnum(SerializedObject so, string path, string name)
        {
            var property = so.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.Enum)
                throw new InvalidOperationException("Installed SDK schema is missing enum field " + path + ".");
            int index = Array.FindIndex(property.enumNames, value => value == name);
            if (index < 0) throw new InvalidOperationException("Installed SDK schema has no " + name + " value for " + path + ".");
            property.enumValueIndex = index;
        }

        static void SetBool(SerializedObject so, string path, bool value)
        {
            var property = so.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
                throw new InvalidOperationException("Installed PhysBone schema is missing bool field " + path + ".");
            property.boolValue = value;
        }

        static void SetFloat(SerializedObject so, string path, float value)
        {
            var property = so.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.Float)
                throw new InvalidOperationException("Installed PhysBone schema is missing float field " + path + ".");
            property.floatValue = value;
        }
    }
}
