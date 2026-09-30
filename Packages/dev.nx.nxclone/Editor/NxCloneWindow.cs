using System;
using System.Collections.Generic;
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
        const string DefaultFxPath = "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/Controllers/vrc_AvatarV3FaceLayer.controller";
        VRCAvatarDescriptor avatar;
        NxClonePreset preset;
        List<NxCloneSlot> slots = new List<NxCloneSlot> { new NxCloneSlot() };
        bool worldDrop;
        bool poseFreeze;
        bool copyVisemes = true;
        bool copyFxAnimations = true;
        bool runtimeScale;
        bool afterimages;
        int afterimageCount = 1;
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
            if (slots == null || slots.Count == 0) slots = new List<NxCloneSlot> { new NxCloneSlot() };
            var nextPreset = (NxClonePreset)EditorGUILayout.ObjectField("Layout preset", preset, typeof(NxClonePreset), false);
            if (nextPreset != preset) { preset = nextPreset; if (preset) LoadPreset(); }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load preset") && preset) LoadPreset();
                if (GUILayout.Button("Save preset")) SavePreset();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clones", EditorStyles.boldLabel);
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                EditorGUILayout.LabelField($"Clone {i + 1}", EditorStyles.miniBoldLabel);
                slot.source = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Source (optional)", slot.source, typeof(VRCAvatarDescriptor), true);
                slot.offset = EditorGUILayout.Vector3Field("Offset", slot.offset);
                slot.scale = EditorGUILayout.Vector3Field("Scale", slot.scale);
                slot.mirror = EditorGUILayout.Toggle("Mirror X", slot.mirror);
                slot.attachTo = (NxAttachPoint)EditorGUILayout.EnumPopup("Attach to", slot.attachTo);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(slots.Count >= 4))
                    if (GUILayout.Button("Add clone")) slots.Add(new NxCloneSlot { offset = new Vector3(0.8f * (slots.Count + 1), 0, 0) });
                using (new EditorGUI.DisabledScope(slots.Count <= 1))
                    if (GUILayout.Button("Remove last")) slots.RemoveAt(slots.Count - 1);
            }
            worldDrop = EditorGUILayout.Toggle("World drop controls", worldDrop);
            if (!worldDrop) poseFreeze = false;
            using (new EditorGUI.DisabledScope(!worldDrop))
                poseFreeze = EditorGUILayout.Toggle("Freeze pose controls", poseFreeze);
            copyVisemes = EditorGUILayout.Toggle("Copy visemes", copyVisemes);
            copyFxAnimations = EditorGUILayout.Toggle("Mirror root FX visuals", copyFxAnimations);
            runtimeScale = EditorGUILayout.Toggle("In-game scale dial", runtimeScale);
            EditorGUILayout.Space();
            afterimages = EditorGUILayout.Toggle("Add afterimages", afterimages);
            if (afterimages)
            {
                afterimageCount = EditorGUILayout.IntSlider("Afterimage count", afterimageCount, 1, 4);
                damping = EditorGUILayout.Slider("Follow strength", damping, 0.05f, 0.8f);
                afterimageColor = EditorGUILayout.ColorField("Single color / alpha", afterimageColor);
                EditorGUILayout.HelpBox("Motion delay uses self-referencing VRChat constraints. It varies with frame rate, not fixed milliseconds. PC shader only.", MessageType.Info);
            }
            if (avatar)
            {
                int estimated = slots.Sum(slot => (slot.source ? slot.source : avatar).GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.bones ?? Array.Empty<Transform>()).Where(t => t).Distinct().Count())
                    + slots.Count(slot => worldDrop || slot.attachTo != NxAttachPoint.Root);
                if (afterimages) estimated += afterimageCount * avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.bones ?? Array.Empty<Transform>()).Where(t => t).Distinct().Count() + afterimageCount;
                if (estimated > 350)
                    EditorGUILayout.HelpBox($"About {estimated} new constraints. VRChat rates PC avatars above 350 constraints Very Poor; review performance before upload.", MessageType.Warning);
            }
            EditorGUILayout.Space();
            var issues = Preflight(avatar, slots, afterimages, worldDrop, poseFreeze, runtimeScale);
            EditorGUILayout.LabelField("Avatar check", EditorStyles.boldLabel);
            foreach (var issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Error);
            if (issues.Count == 0 && avatar)
                EditorGUILayout.HelpBox("Ready. Hidden preview now; final clones and menu controls assemble during upload after avatar build tools.", MessageType.Info);
            using (new EditorGUI.DisabledScope(issues.Count != 0))
                if (GUILayout.Button("Generate scene copy")) Generate();
            EditorGUILayout.EndScrollView();
        }

        void LoadPreset()
        {
            slots = preset.slots == null || preset.slots.Count == 0
                ? new List<NxCloneSlot> { new NxCloneSlot() }
                : preset.slots.Take(4).Select(slot => slot == null ? new NxCloneSlot() : new NxCloneSlot {
                    offset = slot.offset, scale = slot.scale, mirror = slot.mirror, attachTo = slot.attachTo
                }).ToList();
            worldDrop = preset.worldDrop;
            poseFreeze = preset.poseFreeze && worldDrop;
            copyVisemes = preset.copyVisemes;
            copyFxAnimations = preset.copyFxAnimations;
            runtimeScale = preset.runtimeScale;
            afterimages = preset.afterimages;
            afterimageCount = preset.afterimageCount;
            damping = preset.afterimageFollowStrength;
            afterimageColor = preset.afterimageColor;
        }

        void SavePreset()
        {
            if (!preset)
            {
                string path = EditorUtility.SaveFilePanelInProject("Save nxclone setup", "nxclone setup", "asset", "Choose a preset asset path.");
                if (string.IsNullOrEmpty(path)) return;
                preset = CreateInstance<NxClonePreset>();
                AssetDatabase.CreateAsset(preset, path);
            }
            Undo.RecordObject(preset, "Save nxclone setup");
            preset.slots = slots.Select(slot => new NxCloneSlot { offset = slot.offset, scale = slot.scale, mirror = slot.mirror, attachTo = slot.attachTo }).ToList();
            preset.worldDrop = worldDrop;
            preset.poseFreeze = poseFreeze;
            preset.copyVisemes = copyVisemes;
            preset.copyFxAnimations = copyFxAnimations;
            preset.runtimeScale = runtimeScale;
            preset.afterimages = afterimages;
            preset.afterimageCount = afterimageCount;
            preset.afterimageFollowStrength = damping;
            preset.afterimageColor = afterimageColor;
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
        }

        static List<string> Preflight(VRCAvatarDescriptor root, List<NxCloneSlot> slots, bool ghosts, bool drop, bool freeze, bool scale)
        {
            var issues = new List<string>();
            if (!root) { issues.Add("Choose a scene avatar with a VRC Avatar Descriptor."); return issues; }
            if (EditorUtility.IsPersistent(root)) issues.Add("Root must be a scene object. Drag the avatar into a scene first.");
            if (root.transform.Find("nxclone")) issues.Add("Root already contains nxclone output. Select the original avatar to avoid nesting clones.");
            var rootAnimator = root.GetComponent<Animator>();
            if (!rootAnimator || !rootAnimator.avatar || !rootAnimator.avatar.isHuman)
                issues.Add("Root needs an Animator with a valid humanoid Avatar. Set model Rig to Humanoid and fix its mapping.");
            if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                issues.Add("Root needs at least one SkinnedMeshRenderer.");
            var rootPaths = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => PathOf(root.transform, t)));
            if (slots == null || slots.Count < 1 || slots.Count > 4) issues.Add("Choose one to four clone slots.");
            else for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var source = slot.source ? slot.source : root;
                if (EditorUtility.IsPersistent(source)) issues.Add($"Clone {i + 1} source must be a scene object.");
                if (source != root && source.transform.Find("nxclone")) issues.Add($"Clone {i + 1} source already contains nxclone output. Select its original avatar.");
                var sourceAnimator = source.GetComponent<Animator>();
                if (!sourceAnimator || !sourceAnimator.avatar || !sourceAnimator.avatar.isHuman)
                    issues.Add($"Clone {i + 1} needs an Animator with a valid humanoid Avatar.");
                if (source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                    issues.Add($"Clone {i + 1} needs at least one SkinnedMeshRenderer.");
                if (slot.scale.x <= 0 || slot.scale.y <= 0 || slot.scale.z <= 0)
                    issues.Add($"Clone {i + 1} scale must be greater than zero on every axis.");
                if (slot.attachTo != NxAttachPoint.Root && (!rootAnimator || !rootAnimator.avatar || !rootAnimator.avatar.isHuman || !rootAnimator.GetBoneTransform(AttachBone(slot.attachTo))))
                    issues.Add($"Clone {i + 1}: root rig has no {slot.attachTo} bone for attachment.");
                var sourceBones = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.bones ?? Array.Empty<Transform>()).Where(t => t).Distinct().ToArray();
                int external = sourceBones.Count(t => !t.IsChildOf(source.transform));
                if (external > 0) issues.Add($"Clone {i + 1}: {external} bones reference objects outside its source. Rebind the SkinnedMeshRenderer bones.");
                int missing = sourceBones.Count(t => !rootPaths.Contains(PathOf(source.transform, t)));
                if (missing > 0) issues.Add($"Clone {i + 1}: {missing} bone paths are absent on root. Use the same rig/hierarchy or rename bones to match.");
            }
            if (root.baseAnimationLayers != null)
                foreach (var layer in root.baseAnimationLayers)
                    if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault && layer.animatorController && !(layer.animatorController is AnimatorController))
                        issues.Add("FX layer uses an Animator Override Controller. Assign a regular Animator Controller before generating so its animations are preserved.");
            if (!Fx(root) && !AssetDatabase.LoadAssetAtPath<AnimatorController>(DefaultFxPath))
                issues.Add("VRChat's default FX controller is missing. Reinstall or update the Avatars SDK package.");
            if (root.customExpressions)
            {
                if (root.expressionsMenu && root.expressionsMenu.controls != null && root.expressionsMenu.controls.Count >= 8)
                    issues.Add("Expressions Menu has 8 controls. Free one slot or move controls into a submenu.");
                int needed = 1 + (ghosts ? 1 : 0) + (drop ? slots.Count : 0) + (freeze ? slots.Count : 0) + (scale ? 8 : 0);
                if (root.expressionParameters && root.expressionParameters.CalcTotalCost() + needed > VRCExpressionParameters.MAX_PARAMETER_COST)
                    issues.Add($"Expression Parameters need {needed} free bits for nxclone controls.");
                int submenuControls = 1 + (ghosts ? 1 : 0) + (drop ? slots.Count : 0) + (scale ? 1 : 0);
                if ((drop || scale || ghosts) && submenuControls > 8)
                    issues.Add($"nxclone controls need {submenuControls} submenu slots. Reduce clone controls or disable afterimages.");
            }
            if (ghosts && (!Shader.Find("nxclone/solid translucent") || !Shader.Find("nxclone/silhouette mask")))
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

        static string AvailableParameter(VRCAvatarDescriptor root, string requested, HashSet<string> reserved = null)
        {
            var used = reserved == null ? new HashSet<string>() : new HashSet<string>(reserved);
            var fx = Fx(root);
            if (fx) foreach (var p in fx.parameters) used.Add(p.name);
            if (root.customExpressions && root.expressionParameters && root.expressionParameters.parameters != null)
                foreach (var p in root.expressionParameters.parameters) used.Add(p.name);
            if (!used.Contains(requested)) return requested;
            for (int i = 2; ; i++)
                if (!used.Contains(requested + "_" + i)) return requested + "_" + i;
        }

        void Generate()
        {
            var issues = Preflight(avatar, slots, afterimages, worldDrop, poseFreeze, runtimeScale);
            if (issues.Count > 0) { EditorUtility.DisplayDialog("nxclone check", string.Join("\n", issues), "OK"); return; }
            GameObject output = null;
            string folder = null;
            var originalAvatar = avatar;
            var originalSources = slots.Select(slot => slot.source).ToArray();
            try
            {
                EnsureFolder(OutputRoot);
                folder = AssetDatabase.GenerateUniqueAssetPath($"{OutputRoot}/{SafeName(avatar.name)}");
                AssetDatabase.CreateFolder(OutputRoot, folder.Substring(OutputRoot.Length + 1));
                output = Instantiate(avatar.gameObject, avatar.transform.position + Vector3.right * 2, avatar.transform.rotation);
                output.name = avatar.name + "_nxclone_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                // A generated avatar must never reuse the source avatar's upload blueprint ID.
                foreach (var component in output.GetComponents<Component>())
                    if (component && component.GetType().Name == "PipelineManager") DestroyImmediate(component);
                var descriptor = output.GetComponent<VRCAvatarDescriptor>();
                avatar = descriptor;
                for (int i = 0; i < slots.Count; i++)
                    if (!originalSources[i] || originalSources[i] == originalAvatar) slots[i].source = null;
                BuildVisuals(descriptor, folder, false);
                var setup = output.AddComponent<NxCloneSetup>();
                setup.slots = slots.Select(slot => new NxCloneSetupSlot {
                    source = slot.source, offset = slot.offset, scale = slot.scale, mirror = slot.mirror,
                    attachTo = (NxCloneAttachPoint)(int)slot.attachTo
                }).ToList();
                setup.worldDrop = worldDrop;
                setup.poseFreeze = poseFreeze;
                setup.copyVisemes = copyVisemes;
                setup.copyFxAnimations = copyFxAnimations;
                setup.runtimeScale = runtimeScale;
                setup.afterimages = afterimages;
                setup.afterimageCount = afterimageCount;
                setup.damping = damping;
                setup.afterimageColor = afterimageColor;
                setup.generatedFolder = folder;
                output.name = originalAvatar.name + "_nxclone";
                AssetDatabase.SaveAssets();
                Undo.RegisterCreatedObjectUndo(output, "Generate nxclone avatar");
                Selection.activeGameObject = output;
                EditorGUIUtility.PingObject(output);
                Debug.Log($"nxclone: generated hidden preview {output.name}; final clones and controls assemble during upload after avatar build tools.", output);
            }
            catch (Exception ex)
            {
                if (output) DestroyImmediate(output);
                if (folder != null) AssetDatabase.DeleteAsset(folder);
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("nxclone failed", ex.Message + "\nSee Console for details. No generated avatar kept.", "OK");
            }
            finally
            {
                avatar = originalAvatar;
                for (int i = 0; i < slots.Count; i++) slots[i].source = originalSources[i];
            }
        }

        void BuildVisuals(VRCAvatarDescriptor descriptor, string folder, bool installControls)
        {
            var output = descriptor.gameObject;
            var group = new GameObject("nxclone");
            group.transform.SetParent(output.transform, false);
            int built = 0;
            var clones = new List<Transform>();
            for (int i = 0; i < slots.Count; i++)
            {
                var source = slots[i].source ? slots[i].source : avatar;
                var clone = BuildVisual(source.gameObject, group.transform, $"clone-{i + 1}");
                clone.transform.localPosition = slots[i].offset;
                clone.transform.localScale = Vector3.Scale(slots[i].scale, new Vector3(slots[i].mirror ? -1f : 1f, 1f, 1f));
                built += ConstrainBones(output.transform, source.transform, clone.transform, false, 1f);
                if (worldDrop || slots[i].attachTo != NxAttachPoint.Root)
                {
                    Transform anchor = output.transform;
                    if (slots[i].attachTo != NxAttachPoint.Root)
                    {
                        var bone = output.GetComponent<Animator>().GetBoneTransform(AttachBone(slots[i].attachTo));
                        var target = new GameObject($"nxclone anchor {i + 1}");
                        target.transform.SetParent(bone, false);
                        target.transform.localPosition = slots[i].offset;
                        anchor = target.transform;
                        clone.transform.position = anchor.position;
                        clone.transform.rotation = anchor.rotation;
                    }
                    var placement = clone.AddComponent<VRCParentConstraint>();
                    placement.Sources.Add(new VRCConstraintSource(anchor, 1f));
                    placement.ActivateConstraint();
                    placement.ApplyConfigurationChanges();
                }
                clone.SetActive(false);
                clones.Add(clone.transform);
            }
            if (afterimages)
            {
                var ghosts = new List<Transform>();
                for (int i = 0; i < afterimageCount; i++)
                {
                    var ghost = BuildVisual(avatar.gameObject, group.transform, $"afterimage-{i + 1}");
                    var color = afterimageColor;
                    var material = new Material(Shader.Find("nxclone/solid translucent"));
                    material.color = color;
                    AssetDatabase.CreateAsset(material, $"{folder}/afterimage-{i + 1}.mat");
                    foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true))
                        renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    built += ConstrainBones(output.transform, avatar.transform, ghost.transform, true, damping / (i + 1));
                    var position = ghost.AddComponent<VRCPositionConstraint>();
                    position.Sources.Add(new VRCConstraintSource(ghost.transform, 1f));
                    position.Sources.Add(new VRCConstraintSource(output.transform, damping / (i + 1)));
                    position.ActivateConstraint();
                    position.ApplyConfigurationChanges();
                    ghost.SetActive(false);
                    ghosts.Add(ghost.transform);
                }
                if (installControls)
                {
                    var maskMaterial = new Material(Shader.Find("nxclone/silhouette mask"));
                    AssetDatabase.CreateAsset(maskMaterial, $"{folder}/main-silhouette-mask.mat");
                    var mask = NxCloneAfterimages.CreateMainSilhouetteMask(descriptor.transform, descriptor.transform, maskMaterial);
                    mask.gameObject.SetActive(false);
                    ghosts.Add(mask);
                    InstallToggle(descriptor, folder, group.transform, clones, ghosts);
                }
            }
            else if (installControls) InstallToggle(descriptor, folder, group.transform, clones, new List<Transform>());
            if (installControls) Debug.Log($"nxclone: {built} bone constraints added; afterimages include an additional primary silhouette mask.", descriptor);
        }

        public static void RemovePreview(Transform root)
        {
            var preview = root.Find("nxclone");
            if (preview) DestroyImmediate(preview.gameObject);
            var mask = root.Find("__nxclone main silhouette mask");
            if (mask) DestroyImmediate(mask.gameObject);
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (transform && transform.name.StartsWith("nxclone anchor ", StringComparison.Ordinal)) DestroyImmediate(transform.gameObject);
        }

        public static void BuildForUpload(NxCloneSetup setup)
        {
            var descriptor = setup.GetComponent<VRCAvatarDescriptor>();
            if (!descriptor) throw new InvalidOperationException("nxclone setup has no avatar descriptor.");
            var window = CreateInstance<NxCloneWindow>();
            var temporarySources = new List<GameObject>();
            try
            {
                window.avatar = descriptor;
                window.slots = setup.slots.Select(slot => new NxCloneSlot {
                    source = slot.source, offset = slot.offset, scale = slot.scale, mirror = slot.mirror,
                    attachTo = (NxAttachPoint)(int)slot.attachTo
                }).ToList();
                window.worldDrop = setup.worldDrop;
                window.poseFreeze = setup.poseFreeze;
                window.copyVisemes = setup.copyVisemes;
                window.copyFxAnimations = setup.copyFxAnimations;
                window.runtimeScale = setup.runtimeScale;
                window.afterimages = setup.afterimages;
                window.afterimageCount = setup.afterimageCount;
                window.damping = setup.damping;
                window.afterimageColor = setup.afterimageColor;
                RemovePreview(descriptor.transform);
                EnsureFolder(OutputRoot);
                string folder = AssetDatabase.GenerateUniqueAssetPath($"{OutputRoot}/{SafeName(descriptor.name)}-upload");
                AssetDatabase.CreateFolder(OutputRoot, folder.Substring(OutputRoot.Length + 1));
                foreach (var slot in window.slots)
                {
                    if (!slot.source || slot.source == descriptor) { slot.source = null; continue; }
                    var source = Instantiate(slot.source.gameObject);
                    source.name += "_nxclone_source_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    temporarySources.Add(source);
                    NxCloneBuildSource.Bake(source, folder);
                    slot.source = source.GetComponent<VRCAvatarDescriptor>();
                }
                var issues = Preflight(descriptor, window.slots, window.afterimages, window.worldDrop, window.poseFreeze, window.runtimeScale);
                if (issues.Count != 0) throw new InvalidOperationException(string.Join("\n", issues));
                window.BuildVisuals(descriptor, folder, true);
                AssetDatabase.SaveAssets();
                Debug.Log("nxclone: assembled linked visuals and controls after avatar preprocessing.", descriptor);
            }
            finally
            {
                foreach (var source in temporarySources) if (source) DestroyImmediate(source);
                DestroyImmediate(window);
            }
        }

        static GameObject BuildVisual(GameObject source, Transform parent, string name)
        {
            GameObject visual;
            var generatedGroup = source.transform.Find("nxclone");
            int sibling = generatedGroup ? generatedGroup.GetSiblingIndex() : 0;
            if (generatedGroup) generatedGroup.SetParent(null, true);
            try { visual = Instantiate(source); }
            finally
            {
                if (generatedGroup)
                {
                    generatedGroup.SetParent(source.transform, true);
                    generatedGroup.SetSiblingIndex(sibling);
                }
            }
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

        void InstallToggle(VRCAvatarDescriptor descriptor, string folder, Transform group, List<Transform> clones, List<Transform> ghosts)
        {
            var oldFx = Fx(descriptor);
            string fxPath = $"{folder}/fx.controller";
            var reserved = new HashSet<string>();
            string parameter = AvailableParameter(descriptor, Parameter, reserved);
            reserved.Add(parameter);
            string ghostParameter = ghosts.Count == 0 ? null : AvailableParameter(descriptor, "nxclone_afterimages", reserved);
            if (ghostParameter != null) reserved.Add(ghostParameter);
            AnimatorController fx;
            var sourceFx = oldFx ? oldFx : AssetDatabase.LoadAssetAtPath<AnimatorController>(DefaultFxPath);
            if (!sourceFx || !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sourceFx), fxPath))
                throw new InvalidOperationException("Could not copy the avatar FX controller to the generated folder.");
            fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(fxPath);
            if (copyFxAnimations && oldFx)
            {
                var sameRootClones = clones.Where((clone, i) => !slots[i].source || slots[i].source == avatar).ToArray();
                NxCloneFxMirror.MirrorFxCurves(fx, descriptor.transform, sameRootClones, folder);
            }
            if (ghosts.Count != 0)
                NxCloneFxMirror.MirrorFxCurves(fx, descriptor.transform, ghosts, folder, silhouetteOnly: true);
            fx.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            var off = new AnimationClip { name = "nxclone off" };
            var on = new AnimationClip { name = "nxclone on" };
            foreach (var clone in clones)
            {
                string path = PathOf(descriptor.transform, clone);
                SetCurve(off, path, typeof(GameObject), "m_IsActive", 0);
                SetCurve(on, path, typeof(GameObject), "m_IsActive", 1);
            }
            AssetDatabase.CreateAsset(off, $"{folder}/off.anim");
            AssetDatabase.CreateAsset(on, $"{folder}/on.anim");
            AddBoolLayer(fx, "nxclone visible", parameter, off, on);
            if (ghostParameter != null)
            {
                fx.AddParameter(ghostParameter, AnimatorControllerParameterType.Bool);
                var ghostOff = new AnimationClip { name = "nxclone afterimages off" };
                var ghostOn = new AnimationClip { name = "nxclone afterimages on" };
                foreach (var ghost in ghosts)
                {
                    string path = PathOf(descriptor.transform, ghost);
                    SetCurve(ghostOff, path, typeof(GameObject), "m_IsActive", 0);
                    SetCurve(ghostOn, path, typeof(GameObject), "m_IsActive", 1);
                }
                AssetDatabase.CreateAsset(ghostOff, $"{folder}/afterimages-off.anim");
                AssetDatabase.CreateAsset(ghostOn, $"{folder}/afterimages-on.anim");
                AddBoolLayer(fx, "nxclone afterimages", ghostParameter, ghostOff, ghostOn);
            }
            var dropParameters = new List<string>();
            var freezeParameters = new List<string>();
            if (worldDrop) for (int i = 0; i < clones.Count; i++)
            {
                string dropParameter = AvailableParameter(descriptor, $"nxclone_drop_{i + 1}", reserved);
                reserved.Add(dropParameter);
                fx.AddParameter(dropParameter, AnimatorControllerParameterType.Bool);
                var follow = new AnimationClip { name = $"nxclone {i + 1} follow" };
                var placed = new AnimationClip { name = $"nxclone {i + 1} world" };
                string path = PathOf(descriptor.transform, clones[i]);
                SetCurve(follow, path, typeof(VRCParentConstraint), "FreezeToWorld", 0);
                SetCurve(placed, path, typeof(VRCParentConstraint), "FreezeToWorld", 1);
                AssetDatabase.CreateAsset(follow, $"{folder}/clone-{i + 1}-follow.anim");
                AssetDatabase.CreateAsset(placed, $"{folder}/clone-{i + 1}-world.anim");
                AddBoolLayer(fx, $"nxclone {i + 1} placement", dropParameter, follow, placed);
                dropParameters.Add(dropParameter);
                if (poseFreeze)
                {
                    string freezeParameter = AvailableParameter(descriptor, $"nxclone_freeze_{i + 1}", reserved);
                    reserved.Add(freezeParameter);
                    fx.AddParameter(freezeParameter, AnimatorControllerParameterType.Bool);
                    var live = new AnimationClip { name = $"nxclone {i + 1} live pose" };
                    var frozen = new AnimationClip { name = $"nxclone {i + 1} frozen pose" };
                    foreach (var bone in clones[i].GetComponentsInChildren<VRCRotationConstraint>(true))
                    {
                        string bonePath = PathOf(descriptor.transform, bone.transform);
                        SetCurve(live, bonePath, typeof(VRCRotationConstraint), "FreezeToWorld", 0);
                        SetCurve(frozen, bonePath, typeof(VRCRotationConstraint), "FreezeToWorld", 1);
                    }
                    AssetDatabase.CreateAsset(live, $"{folder}/clone-{i + 1}-live-pose.anim");
                    AssetDatabase.CreateAsset(frozen, $"{folder}/clone-{i + 1}-frozen-pose.anim");
                    AddBoolLayer(fx, $"nxclone {i + 1} pose", freezeParameter, live, frozen);
                    freezeParameters.Add(freezeParameter);
                }
            }
            string scaleParameter = null;
            if (runtimeScale)
            {
                scaleParameter = AvailableParameter(descriptor, "nxclone_scale", reserved);
                reserved.Add(scaleParameter);
                fx.AddParameter(new AnimatorControllerParameter {
                    name = scaleParameter, type = AnimatorControllerParameterType.Float, defaultFloat = 0.5f
                });
                AddScaleLayer(fx, scaleParameter, descriptor.transform, clones, folder);
            }
            if (copyVisemes) for (int i = 0; i < clones.Count; i++)
            {
                var source = slots[i].source ? slots[i].source : avatar;
                if (!NxCloneVisemes.AddCloneVisemes(fx, source, descriptor.transform, clones[i], folder, $"nxclone {i + 1} visemes"))
                    Debug.LogWarning($"nxclone: clone {i + 1} has no compatible blendshape viseme mapping; viseme copying skipped.", descriptor);
            }
            if (copyVisemes) for (int i = 0; i < ghosts.Count; i++)
                NxCloneVisemes.AddCloneVisemes(fx, descriptor, descriptor.transform, ghosts[i], folder, $"nxclone afterimage {i + 1} visemes");
            var layers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            descriptor.customizeAnimationLayers = true;
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
            var addedParameters = new List<VRCExpressionParameters.Parameter> { new VRCExpressionParameters.Parameter {
                name = parameter, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 0, saved = false, networkSynced = true
            } };
            if (ghostParameter != null)
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = ghostParameter, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 0, saved = false, networkSynced = true
                });
            foreach (var dropParameter in dropParameters)
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = dropParameter, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 0, saved = false, networkSynced = true
                });
            foreach (var freezeParameter in freezeParameters)
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = freezeParameter, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 0, saved = false, networkSynced = true
                });
            if (scaleParameter != null)
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = scaleParameter, valueType = VRCExpressionParameters.ValueType.Float, defaultValue = 0.5f, saved = true, networkSynced = true
                });
            parameters.parameters = (parameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>()).Concat(addedParameters).ToArray();
            AssetDatabase.CreateAsset(parameters, $"{folder}/parameters.asset");
            descriptor.expressionParameters = parameters;
            var menu = descriptor.customExpressions && descriptor.expressionsMenu
                ? Instantiate(descriptor.expressionsMenu)
                : ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "nxclone menu";
            if (menu.controls == null) menu.controls = new List<VRCExpressionsMenu.Control>();
            var toggle = new VRCExpressionsMenu.Control {
                name = "nxclone", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 1
            };
            if (dropParameters.Count == 0 && scaleParameter == null && ghostParameter == null) menu.controls.Add(toggle);
            else
            {
                var subMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                subMenu.name = "nxclone controls";
                if (subMenu.controls == null) subMenu.controls = new List<VRCExpressionsMenu.Control>();
                subMenu.controls.Add(toggle);
                for (int i = 0; i < dropParameters.Count; i++)
                {
                    var dropControl = new VRCExpressionsMenu.Control {
                        name = $"Clone {i + 1} world drop", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = dropParameters[i] }, value = 1
                    };
                    if (poseFreeze)
                    {
                        var cloneMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                        cloneMenu.name = $"nxclone {i + 1} controls";
                        if (cloneMenu.controls == null) cloneMenu.controls = new List<VRCExpressionsMenu.Control>();
                        cloneMenu.controls.Add(dropControl);
                        cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                            name = "Freeze pose", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = freezeParameters[i] }, value = 1
                        });
                        AssetDatabase.CreateAsset(cloneMenu, $"{folder}/clone-{i + 1}-controls.asset");
                        subMenu.controls.Add(new VRCExpressionsMenu.Control {
                            name = $"Clone {i + 1}", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = cloneMenu
                        });
                    }
                    else subMenu.controls.Add(dropControl);
                }
                if (scaleParameter != null)
                    subMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Clone scale", type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                        subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = scaleParameter } }
                    });
                if (ghostParameter != null)
                    subMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Afterimages", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = ghostParameter }, value = 1
                    });
                AssetDatabase.CreateAsset(subMenu, $"{folder}/controls.asset");
                menu.controls.Add(new VRCExpressionsMenu.Control {
                    name = "nxclone", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = subMenu
                });
            }
            AssetDatabase.CreateAsset(menu, $"{folder}/menu.asset");
            descriptor.expressionsMenu = menu;
            descriptor.customExpressions = true;
        }

        static void AddBoolLayer(AnimatorController fx, string name, string parameter, AnimationClip off, AnimationClip on)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var layer = new AnimatorControllerLayer { name = name, defaultWeight = 1f, stateMachine = machine };
            var offState = machine.AddState("off");
            offState.motion = off;
            offState.writeDefaultValues = false;
            var onState = machine.AddState("on");
            onState.motion = on;
            onState.writeDefaultValues = false;
            machine.defaultState = offState;
            var toOn = offState.AddTransition(onState);
            toOn.hasExitTime = false;
            toOn.duration = 0;
            toOn.AddCondition(AnimatorConditionMode.If, 0, parameter);
            var toOff = onState.AddTransition(offState);
            toOff.hasExitTime = false;
            toOff.duration = 0;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0, parameter);
            fx.AddLayer(layer);
        }

        static void AddScaleLayer(AnimatorController fx, string parameter, Transform root, List<Transform> clones, string folder)
        {
            var tree = new BlendTree { name = "nxclone scale", blendType = BlendTreeType.Simple1D, blendParameter = parameter };
            AssetDatabase.CreateAsset(tree, $"{folder}/scale.asset");
            float[] amounts = { 0.5f, 1f, 2f };
            float[] thresholds = { 0f, 0.5f, 1f };
            for (int i = 0; i < amounts.Length; i++)
            {
                var clip = new AnimationClip { name = $"nxclone scale {amounts[i]}" };
                foreach (var clone in clones)
                {
                    string path = PathOf(root, clone);
                    Vector3 value = clone.localScale * amounts[i];
                    SetCurve(clip, path, typeof(Transform), "m_LocalScale.x", value.x);
                    SetCurve(clip, path, typeof(Transform), "m_LocalScale.y", value.y);
                    SetCurve(clip, path, typeof(Transform), "m_LocalScale.z", value.z);
                }
                AssetDatabase.CreateAsset(clip, $"{folder}/scale-{i}.anim");
                tree.AddChild(clip, thresholds[i]);
            }
            var machine = new AnimatorStateMachine { name = "nxclone scale" };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var state = machine.AddState("scale");
            state.motion = tree;
            state.writeDefaultValues = false;
            machine.defaultState = state;
            fx.AddLayer(new AnimatorControllerLayer { name = "nxclone scale", defaultWeight = 1f, stateMachine = machine });
        }

        static void SetCurve(AnimationClip clip, string path, Type type, string property, float value)
        {
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, type, property),
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

        static HumanBodyBones AttachBone(NxAttachPoint point)
        {
            switch (point)
            {
                case NxAttachPoint.Head: return HumanBodyBones.Head;
                case NxAttachPoint.Chest: return HumanBodyBones.Chest;
                case NxAttachPoint.Hips: return HumanBodyBones.Hips;
                case NxAttachPoint.LeftHand: return HumanBodyBones.LeftHand;
                case NxAttachPoint.RightHand: return HumanBodyBones.RightHand;
                case NxAttachPoint.LeftFoot: return HumanBodyBones.LeftFoot;
                case NxAttachPoint.RightFoot: return HumanBodyBones.RightFoot;
                default: return HumanBodyBones.Hips;
            }
        }

        static string SafeName(string name)
        {
            const string invalid = "<>:\"/\\|?*";
            string safe = new string(name.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd(' ', '.');
            if (string.IsNullOrEmpty(safe)) return "avatar";
            string stem = safe.Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
                .Contains(stem, StringComparer.OrdinalIgnoreCase)) safe = "_" + safe;
            return safe.Substring(0, Math.Min(48, safe.Length)).TrimEnd(' ', '.');
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            AssetDatabase.CreateFolder("Assets", "nxclone-generated");
        }
    }
}
