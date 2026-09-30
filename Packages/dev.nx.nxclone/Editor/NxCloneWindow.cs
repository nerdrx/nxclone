using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace nxclone
{
    public sealed class NxCloneWindow : EditorWindow
    {
        const string Parameter = "nxclone_visible";
        const string OutputRoot = "Assets/nxclone-generated";
        VRCAvatarDescriptor avatar;
        VRCAvatarDescriptor cloneSource;
        int cloneCount = 1;
        Vector3 cloneOffset = new Vector3(0.8f, 0, 0);
        bool afterimages;
        int afterimageCount = 2;
        float damping = 0.25f;
        Color afterimageColor = new Color(0.25f, 0.8f, 1f, 0.3f);
        Vector2 scroll;

        [MenuItem("Tools/nxclone")]
        static void Open() => GetWindow<NxCloneWindow>("nxclone");

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("nxclone", EditorStyles.boldLabel);
            avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Root avatar", avatar, typeof(VRCAvatarDescriptor), true);
            cloneSource = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Clone source (optional)", cloneSource, typeof(VRCAvatarDescriptor), true);
            cloneCount = EditorGUILayout.IntSlider("Clones", cloneCount, 1, 4);
            cloneOffset = EditorGUILayout.Vector3Field("Offset per clone", cloneOffset);
            EditorGUILayout.Space();
            afterimages = EditorGUILayout.Toggle("Add afterimages", afterimages);
            if (afterimages)
            {
                afterimageCount = EditorGUILayout.IntSlider("Afterimage count", afterimageCount, 1, 4);
                damping = EditorGUILayout.Slider("Follow strength", damping, 0.05f, 0.8f);
                afterimageColor = EditorGUILayout.ColorField("Single color / alpha", afterimageColor);
                EditorGUILayout.HelpBox("Motion delay uses self-referencing VRChat constraints. It varies with frame rate, not fixed milliseconds. PC shader only.", MessageType.Info);
            }
            if (avatar && (cloneSource || avatar))
            {
                var rig = cloneSource ? cloneSource : avatar;
                int bones = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.bones ?? Array.Empty<Transform>()).Where(t => t).Distinct().Count();
                int estimated = (cloneCount + (afterimages ? afterimageCount : 0)) * bones + (afterimages ? afterimageCount : 0);
                if (estimated > 350)
                    EditorGUILayout.HelpBox($"About {estimated} new constraints. VRChat rates PC avatars above 350 constraints Very Poor; review performance before upload.", MessageType.Warning);
            }
            EditorGUILayout.Space();
            var issues = Preflight(avatar, cloneSource ? cloneSource : avatar, afterimages);
            EditorGUILayout.LabelField("Avatar check", EditorStyles.boldLabel);
            foreach (var issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Error);
            if (issues.Count == 0 && avatar)
                EditorGUILayout.HelpBox("Ready. Missing FX and expression assets will be created on the scene copy.", MessageType.Info);
            using (new EditorGUI.DisabledScope(issues.Count != 0))
                if (GUILayout.Button("Generate scene copy")) Generate();
            EditorGUILayout.EndScrollView();
        }

        static List<string> Preflight(VRCAvatarDescriptor root, VRCAvatarDescriptor source, bool ghosts)
        {
            var issues = new List<string>();
            if (!root) { issues.Add("Choose a scene avatar with a VRC Avatar Descriptor."); return issues; }
            if (EditorUtility.IsPersistent(root)) issues.Add("Root must be a scene object. Drag the avatar into a scene first.");
            if (!source) { issues.Add("Clone source is missing."); return issues; }
            if (EditorUtility.IsPersistent(source)) issues.Add("Clone source must be a scene object.");
            var rootAnimator = root.GetComponent<Animator>();
            var sourceAnimator = source.GetComponent<Animator>();
            if (!rootAnimator || !rootAnimator.avatar || !rootAnimator.avatar.isHuman)
                issues.Add("Root needs an Animator with a valid humanoid Avatar. Set model Rig to Humanoid and fix its mapping.");
            if (!sourceAnimator || !sourceAnimator.avatar || !sourceAnimator.avatar.isHuman)
                issues.Add("Clone source needs an Animator with a valid humanoid Avatar.");
            if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                issues.Add("Root needs at least one SkinnedMeshRenderer.");
            if (source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                issues.Add("Clone source needs at least one SkinnedMeshRenderer.");
            var rootPaths = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => PathOf(root.transform, t)));
            var sourceBones = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(r => r.bones ?? Array.Empty<Transform>()).Where(t => t).Distinct();
            int external = sourceBones.Count(t => !t.IsChildOf(source.transform));
            if (external > 0) issues.Add($"{external} clone bones reference objects outside the clone source. Rebind the SkinnedMeshRenderer bones.");
            int missing = sourceBones.Count(t => !rootPaths.Contains(PathOf(source.transform, t)));
            if (missing > 0) issues.Add($"{missing} clone bone paths are absent on root. Use the same rig/hierarchy or rename bones to match.");
            if (root.baseAnimationLayers != null)
                foreach (var layer in root.baseAnimationLayers)
                    if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault && layer.animatorController && !(layer.animatorController is AnimatorController))
                        issues.Add("FX layer uses an Animator Override Controller. Assign a regular Animator Controller before generating so its animations are preserved.");
            if (root.customExpressions)
            {
                if (root.expressionsMenu && root.expressionsMenu.controls != null && root.expressionsMenu.controls.Count >= 8)
                    issues.Add("Expressions Menu has 8 controls. Free one slot or move controls into a submenu.");
                if (root.expressionParameters && root.expressionParameters.CalcTotalCost() + 1 > VRCExpressionParameters.MAX_PARAMETER_COST)
                    issues.Add("Expression Parameters need one free bit for the nxclone toggle.");
            }
            if (ghosts && !Shader.Find("nxclone/solid translucent"))
                issues.Add("Afterimage shader has not imported. Reimport the nxclone package.");
            return issues;
        }

        static AnimatorController Fx(VRCAvatarDescriptor root)
        {
            if (!root || root.baseAnimationLayers == null) return null;
            foreach (var layer in root.baseAnimationLayers)
                if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault)
                    return layer.animatorController as AnimatorController;
            return null;
        }

        static string AvailableParameter(VRCAvatarDescriptor root)
        {
            var used = new HashSet<string>();
            var fx = Fx(root);
            if (fx) foreach (var p in fx.parameters) used.Add(p.name);
            if (root.customExpressions && root.expressionParameters && root.expressionParameters.parameters != null)
                foreach (var p in root.expressionParameters.parameters) used.Add(p.name);
            if (!used.Contains(Parameter)) return Parameter;
            for (int i = 2; ; i++)
                if (!used.Contains(Parameter + "_" + i)) return Parameter + "_" + i;
        }

        void Generate()
        {
            var source = cloneSource ? cloneSource : avatar;
            var issues = Preflight(avatar, source, afterimages);
            if (issues.Count > 0) { EditorUtility.DisplayDialog("nxclone check", string.Join("\n", issues), "OK"); return; }
            GameObject output = null;
            string folder = null;
            try
            {
                EnsureFolder(OutputRoot);
                folder = AssetDatabase.GenerateUniqueAssetPath($"{OutputRoot}/{SafeName(avatar.name)}");
                AssetDatabase.CreateFolder(OutputRoot, folder.Substring(OutputRoot.Length + 1));
                output = Instantiate(avatar.gameObject, avatar.transform.position + Vector3.right * 2, avatar.transform.rotation);
                output.name = avatar.name + "_nxclone";
                // A generated avatar must never reuse the source avatar's upload blueprint ID.
                foreach (var component in output.GetComponents<Component>())
                    if (component && component.GetType().Name == "PipelineManager") DestroyImmediate(component);
                var descriptor = output.GetComponent<VRCAvatarDescriptor>();
                var group = new GameObject("nxclone");
                group.transform.SetParent(output.transform, false);
                int built = 0;
                for (int i = 0; i < cloneCount; i++)
                {
                    var clone = BuildVisual(source.gameObject, group.transform, $"clone-{i + 1}");
                    clone.transform.localPosition = cloneOffset * (i + 1);
                    built += ConstrainBones(output.transform, source.transform, clone.transform, false, 1f);
                }
                if (afterimages)
                {
                    for (int i = 0; i < afterimageCount; i++)
                    {
                        var ghost = BuildVisual(source.gameObject, group.transform, $"afterimage-{i + 1}");
                        var color = afterimageColor;
                        color.a /= i + 1;
                        var material = new Material(Shader.Find("nxclone/solid translucent"));
                        material.color = color;
                        AssetDatabase.CreateAsset(material, $"{folder}/afterimage-{i + 1}.mat");
                        foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true))
                            renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                        built += ConstrainBones(output.transform, source.transform, ghost.transform, true, damping / (i + 1));
                        var position = ghost.AddComponent<VRCPositionConstraint>();
                        position.Sources.Add(new VRCConstraintSource(ghost.transform, 1f));
                        position.Sources.Add(new VRCConstraintSource(output.transform, damping / (i + 1)));
                        position.ActivateConstraint();
                        position.ApplyConfigurationChanges();
                    }
                }
                InstallToggle(descriptor, folder, group.transform);
                AssetDatabase.SaveAssets();
                Undo.RegisterCreatedObjectUndo(output, "Generate nxclone avatar");
                Selection.activeGameObject = output;
                EditorGUIUtility.PingObject(output);
                Debug.Log($"nxclone: generated {output.name}, {built} bone constraints, assets in {folder}", output);
            }
            catch (Exception ex)
            {
                if (output) DestroyImmediate(output);
                if (folder != null) AssetDatabase.DeleteAsset(folder);
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("nxclone failed", ex.Message + "\nSee Console for details. No generated avatar kept.", "OK");
            }
        }

        static GameObject BuildVisual(GameObject source, Transform parent, string name)
        {
            var visual = Instantiate(source);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            foreach (var component in visual.GetComponentsInChildren<Component>(true))
            {
                if (!component) continue;
                if (component is Transform || component is Renderer || component is MeshFilter) continue;
                DestroyImmediate(component);
            }
            return visual;
        }

        static int ConstrainBones(Transform root, Transform source, Transform visual, bool delayed, float weight)
        {
            int count = 0;
            var bones = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(r => r.bones ?? Array.Empty<Transform>()).Where(t => t).Distinct();
            foreach (var bone in bones)
            {
                var path = PathOf(visual, bone);
                var target = root.Find(path);
                if (!target) continue;
                var rotation = bone.gameObject.AddComponent<VRCRotationConstraint>();
                if (delayed) rotation.Sources.Add(new VRCConstraintSource(bone, 1f));
                rotation.Sources.Add(new VRCConstraintSource(target, weight));
                rotation.ActivateConstraint();
                rotation.ApplyConfigurationChanges();
                count++;
            }
            return count;
        }

        static void InstallToggle(VRCAvatarDescriptor descriptor, string folder, Transform group)
        {
            var oldFx = Fx(descriptor);
            string fxPath = $"{folder}/fx.controller";
            string parameter = AvailableParameter(descriptor);
            AnimatorController fx;
            if (oldFx)
            {
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(oldFx), fxPath))
                    throw new InvalidOperationException("Could not copy FX controller to generated folder.");
                fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(fxPath);
            }
            else fx = AnimatorController.CreateAnimatorControllerAtPath(fxPath);
            fx.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            var off = new AnimationClip { name = "nxclone off" };
            var on = new AnimationClip { name = "nxclone on" };
            string path = PathOf(descriptor.transform, group);
            SetActiveCurve(off, path, 0);
            SetActiveCurve(on, path, 1);
            AssetDatabase.CreateAsset(off, $"{folder}/off.anim");
            AssetDatabase.CreateAsset(on, $"{folder}/on.anim");
            var layer = new AnimatorControllerLayer { name = "nxclone", defaultWeight = 1f, stateMachine = new AnimatorStateMachine { name = "nxclone" } };
            AssetDatabase.AddObjectToAsset(layer.stateMachine, fx);
            var offState = layer.stateMachine.AddState("off");
            offState.motion = off;
            offState.writeDefaultValues = false;
            var onState = layer.stateMachine.AddState("on");
            onState.motion = on;
            onState.writeDefaultValues = false;
            layer.stateMachine.defaultState = offState;
            var toOn = offState.AddTransition(onState);
            toOn.hasExitTime = false;
            toOn.duration = 0;
            toOn.AddCondition(AnimatorConditionMode.If, 0, parameter);
            var toOff = onState.AddTransition(offState);
            toOff.hasExitTime = false;
            toOff.duration = 0;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0, parameter);
            fx.AddLayer(layer);
            var layers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            if (!layers.Any(item => item.type == VRCAvatarDescriptor.AnimLayerType.FX))
                layers = layers.Concat(new[] { new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX } }).ToArray();
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    layers[i].animatorController = fx;
                    layers[i].isDefault = false;
                    layers[i].isEnabled = true;
                }
            descriptor.baseAnimationLayers = layers;
            var parameters = descriptor.customExpressions && descriptor.expressionParameters
                ? Instantiate(descriptor.expressionParameters)
                : ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.name = "nxclone parameters";
            parameters.parameters = (parameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>()).Concat(new[] { new VRCExpressionParameters.Parameter {
                name = parameter, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 0, saved = true, networkSynced = true
            } }).ToArray();
            AssetDatabase.CreateAsset(parameters, $"{folder}/parameters.asset");
            descriptor.expressionParameters = parameters;
            var menu = descriptor.customExpressions && descriptor.expressionsMenu
                ? Instantiate(descriptor.expressionsMenu)
                : ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "nxclone menu";
            if (menu.controls == null) menu.controls = new List<VRCExpressionsMenu.Control>();
            menu.controls.Add(new VRCExpressionsMenu.Control {
                name = "nxclone", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }
            });
            AssetDatabase.CreateAsset(menu, $"{folder}/menu.asset");
            descriptor.expressionsMenu = menu;
            descriptor.customExpressions = true;
        }

        static void SetActiveCurve(AnimationClip clip, string path, float value)
        {
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"),
                new AnimationCurve(new Keyframe(0, value), new Keyframe(1f / 60f, value)));
        }

        static string PathOf(Transform root, Transform child)
        {
            if (root == child) return "";
            var parts = new List<string>();
            for (var current = child; current && current != root; current = current.parent) parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string SafeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            AssetDatabase.CreateFolder("Assets", "nxclone-generated");
        }
    }
}
