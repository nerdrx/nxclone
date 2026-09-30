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
        NxCloneWriteDefaults writeDefaults;
        bool worldDrop;
        bool poseFreeze;
        bool copyVisemes = true;
        bool copyFxAnimations = true;
        bool independentCloneFx;
        bool deferParameterBudgetToVrcfury;
        bool runtimeScale;
        bool runtimePosition;
        NxCloneAxes positionAxes = NxCloneAxes.Z;
        bool runtimeRotation;
        NxCloneAxes rotationAxes = NxCloneAxes.Y;
        bool posing;
        bool limbIk;
        bool limbContacts;
        bool wear;
        bool recording;
        int recordingSamples = 8;
        float recordingDuration = 5f;
        NxCloneGesture cloneGesture = NxCloneGesture.Disabled;
        NxCloneGestureHand cloneGestureHand;
        NxCloneGesture afterimageGesture = NxCloneGesture.Disabled;
        NxCloneGestureHand afterimageGestureHand = NxCloneGestureHand.Right;
        bool afterimages;
        int afterimageCount = 1;
        float damping = 0.25f;
        bool distinctAfterimageColors = true;
        List<Color> afterimageColors = new List<Color>();
        Color afterimageColor = new Color(0.25f, 0.8f, 1f, 0.3f);
        Vector2 scroll;

        [MenuItem("Tools/nxclone")]
        static void Open() => GetWindow<NxCloneWindow>("nxclone");

        void OnGUI()
        {
            minSize = new Vector2(260f, 300f);
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            var oldWideMode = EditorGUIUtility.wideMode;
            EditorGUIUtility.labelWidth = Mathf.Min(190f, position.width * 0.48f);
            EditorGUIUtility.wideMode = false;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("nxclone", EditorStyles.boldLabel);
            avatar = (VRCAvatarDescriptor)ObjectField("Root avatar", avatar, typeof(VRCAvatarDescriptor), true);
            if (slots == null || slots.Count == 0) slots = new List<NxCloneSlot> { new NxCloneSlot() };
            var nextPreset = (NxClonePreset)ObjectField("Layout preset", preset, typeof(NxClonePreset), false);
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
                slot.source = (VRCAvatarDescriptor)ObjectField("Source (optional)", slot.source, typeof(VRCAvatarDescriptor), true);
                slot.offset = EditorGUILayout.Vector3Field("Position from anchor (meters)", slot.offset);
                slot.rotation = EditorGUILayout.Vector3Field("Rotation from anchor (degrees)", slot.rotation);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Default placement")) { slot.offset = Vector3.forward; slot.rotation = new Vector3(0f, 180f, 0f); }
                    if (GUILayout.Button("At anchor")) { slot.offset = Vector3.zero; slot.rotation = Vector3.zero; }
                }
                slot.scale = EditorGUILayout.Vector3Field("Scale", slot.scale);
                slot.mirror = Toggle("Mirror X", slot.mirror);
                slot.attachTo = (NxAttachPoint)EnumField("Attach to", slot.attachTo);
                slot.anchor = (Transform)ObjectField("Custom anchor (optional)", slot.anchor, typeof(Transform), true);
                if (slot.attachTo == NxAttachPoint.Root && !slot.anchor)
                {
                    slot.movement = (NxCloneMovement)EnumField("Root movement", slot.movement);
                    if (slot.movement == NxCloneMovement.DancePartner)
                        EditorGUILayout.HelpBox("Copies movement in the clone’s facing direction. Turning rotates the clone in place. Hide and show to reset its starting point.", MessageType.Info);
                }
                slot.contactAnchor = Toggle("Attach to remote contacts", slot.contactAnchor);
                if (slot.contactAnchor)
                {
                    slot.contactTag = EditorGUILayout.TextField("Contact sender tag", slot.contactTag);
                    slot.contactAllowSelf = Toggle("Allow self contacts", slot.contactAllowSelf);
                    slot.contactAllowOthers = Toggle("Allow other-avatar contacts", slot.contactAllowOthers);
                    EditorGUILayout.HelpBox(NxCloneContactAnchor.SenderHelp(slot.contactTag), MessageType.Info);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(slots.Count >= 4))
                    if (GUILayout.Button("Add clone")) slots.Add(new NxCloneSlot());
                using (new EditorGUI.DisabledScope(slots.Count <= 1))
                    if (GUILayout.Button("Remove last")) slots.RemoveAt(slots.Count - 1);
            }
            writeDefaults = (NxCloneWriteDefaults)EnumField("Write Defaults", writeDefaults);
            worldDrop = Toggle("World drop controls", worldDrop);
            poseFreeze = Toggle("Freeze pose controls", poseFreeze);
            copyVisemes = Toggle("Copy visemes", copyVisemes);
            copyFxAnimations = Toggle("Copy clone expressions / FX", copyFxAnimations);
            using (new EditorGUI.DisabledScope(!copyFxAnimations))
                independentCloneFx = Toggle("Independent clone FX / expression recording", independentCloneFx);
            bool vrcfuryCompressorAvailable = HasVrcfuryParameterCompressor();
            using (new EditorGUI.DisabledScope(!vrcfuryCompressorAvailable))
                deferParameterBudgetToVrcfury = Toggle("Defer parameter limit to VRCFury", deferParameterBudgetToVrcfury);
            if (!vrcfuryCompressorAvailable)
                EditorGUILayout.HelpBox("Deferred parameter budgeting requires the VRCFury Parameter Compressor. Install and enable VRCFury to use this option.", MessageType.Info);
            else if (deferParameterBudgetToVrcfury)
                EditorGUILayout.HelpBox("VRCFury compresses the completed avatar FX and expression parameters after nxclone builds. Upload is stopped if the final synced cost still exceeds 256 bits.", MessageType.Info);
            runtimePosition = Toggle("In-game position dials", runtimePosition);
            if (runtimePosition)
                positionAxes = AxisChoices(positionAxes, "Left / Right (X)", "Down / Up (Y)", "Distance (Z)");
            runtimeRotation = Toggle("In-game rotation dials", runtimeRotation);
            if (runtimeRotation)
                rotationAxes = AxisChoices(rotationAxes, "Pitch (X)", "Yaw (Y)", "Roll (Z)");
            if (runtimePosition || runtimeRotation)
                EditorGUILayout.LabelField($"Placement controls: {8 * ((runtimePosition ? AxisCount(positionAxes) : 0) + (runtimeRotation ? AxisCount(rotationAxes) : 0))} bits per clone", EditorStyles.wordWrappedLabel);
            runtimeScale = Toggle("In-game scale dial", runtimeScale);
            posing = Toggle("Grabbable limb posing", posing);
            limbIk = Toggle("Limb IK controls", limbIk);
            using (new EditorGUI.DisabledScope(!limbIk))
                limbContacts = Toggle("Remote limb contact attachment", limbContacts);
            if (limbIk && limbContacts)
                EditorGUILayout.HelpBox("Adds four contact trackers per clone for remote hands/feet. Both users must enable avatar contacts; this increases contact and constraint counts.", MessageType.Info);
            wear = Toggle("Wear a clone", wear);
            recording = Toggle("Pose recording / playback", recording);
            if (recording)
            {
                recordingSamples = EditorGUILayout.IntSlider("Pose samples", recordingSamples, 2, RecordingSampleLimit());
                recordingDuration = EditorGUILayout.Slider("Recording seconds", recordingDuration, 1f, 30f);
                EditorGUILayout.HelpBox("Sampled body pose and movement interpolate during playback. Turn world drop off to play movement, or leave it on for in-place playback. Each sample adds bone constraints. " +
                    (independentCloneFx ? "Independent clone FX also records expression and gesture snapshots." : "Enable Independent clone FX to record expression and gesture snapshots."), MessageType.Info);
            }
            EditorGUILayout.Space();
            cloneGesture = (NxCloneGesture)EnumField("Clone visibility gesture", cloneGesture);
            if (cloneGesture != NxCloneGesture.Disabled)
                cloneGestureHand = (NxCloneGestureHand)EnumField("Clone gesture hand", cloneGestureHand);
            afterimages = Toggle("Add afterimages", afterimages);
            if (afterimages)
            {
                afterimageGesture = (NxCloneGesture)EnumField("Afterimage visibility gesture", afterimageGesture);
                if (afterimageGesture != NxCloneGesture.Disabled)
                    afterimageGestureHand = (NxCloneGestureHand)EnumField("Afterimage gesture hand", afterimageGestureHand);
                afterimageCount = EditorGUILayout.IntSlider("Afterimage count", afterimageCount, 1, 4);
                damping = EditorGUILayout.Slider("Follow strength", damping, 0.05f, 0.8f);
                afterimageColor = ColorField("Base colour / alpha", afterimageColor);
                distinctAfterimageColors = Toggle("Different colour for each afterimage", distinctAfterimageColors);
                if (distinctAfterimageColors)
                {
                    EnsureAfterimageColors();
                    for (int i = 0; i < afterimageCount; i++)
                        afterimageColors[i] = ColorField($"Afterimage {i + 1}", afterimageColors[i]);
                }
                EditorGUILayout.HelpBox("Motion delay uses self-referencing VRChat constraints. It varies with frame rate, not fixed milliseconds. PC shader only.", MessageType.Info);
            }
            if (avatar)
            {
                int estimated = slots.Sum(slot => (slot.source ? slot.source : avatar).GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.bones ?? Array.Empty<Transform>()).Where(t => t).Distinct().Count())
                    + slots.Count(slot => worldDrop || slot.attachTo != NxAttachPoint.Root);
                if (afterimages) estimated += afterimageCount * avatar.GetComponentsInChildren<Transform>(true).Length * 3 + 2 * afterimageCount;
                if (recording) estimated += EffectiveRecordingSamples() * slots.Sum(slot => (slot.source ? slot.source : avatar).GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.bones ?? Array.Empty<Transform>()).Where(t => t).Distinct().Count());
                estimated += 2 + 2 * slots.Count
                    + 3 * slots.Count(slot => slot.movement == NxCloneMovement.DancePartner && slot.attachTo == NxAttachPoint.Root && !slot.anchor);
                if (estimated > 350)
                    EditorGUILayout.HelpBox($"Up to {estimated} new constraints. VRChat rates PC avatars above 350 constraints Very Poor; review performance before upload.", MessageType.Warning);
            }
            EditorGUILayout.Space();
            var issues = Preflight(avatar, slots, afterimages, worldDrop, poseFreeze, runtimeScale, recording, posing, deferParameterBudgetToVrcfury, wear, limbIk, limbContacts, runtimePosition, runtimeRotation, positionAxes, rotationAxes);
            EditorGUILayout.LabelField("Avatar check", EditorStyles.boldLabel);
            foreach (var issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Error);
            if (issues.Count == 0 && avatar)
                EditorGUILayout.HelpBox("Ready. Hidden preview now; final clones and menu controls assemble during upload after avatar build tools.", MessageType.Info);
            using (new EditorGUI.DisabledScope(issues.Count != 0))
                if (GUILayout.Button("Generate scene copy")) Generate();
            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUIUtility.wideMode = oldWideMode;
        }

        bool Narrow => position.width < 430f;

        UnityEngine.Object ObjectField(string label, UnityEngine.Object value, Type type, bool scene)
        {
            if (!Narrow) return EditorGUILayout.ObjectField(label, value, type, scene);
            EditorGUILayout.LabelField(label, EditorStyles.wordWrappedLabel);
            return EditorGUILayout.ObjectField(value, type, scene);
        }

        Enum EnumField(string label, Enum value)
        {
            if (!Narrow) return EditorGUILayout.EnumPopup(label, value);
            EditorGUILayout.LabelField(label, EditorStyles.wordWrappedLabel);
            return EditorGUILayout.EnumPopup(value);
        }

        Color ColorField(string label, Color value)
        {
            if (!Narrow) return EditorGUILayout.ColorField(label, value);
            EditorGUILayout.LabelField(label, EditorStyles.wordWrappedLabel);
            return EditorGUILayout.ColorField(value);
        }

        static bool Toggle(string label, bool value)
        {
            var style = new GUIStyle(EditorStyles.label) { wordWrap = true };
            float height = Mathf.Max(EditorGUIUtility.singleLineHeight, style.CalcHeight(new GUIContent(label), EditorGUIUtility.currentViewWidth - 52f));
            return EditorGUILayout.ToggleLeft(label, value, style, GUILayout.Height(height));
        }

        static int AxisCount(NxCloneAxes axes) =>
            new[] { NxCloneAxes.X, NxCloneAxes.Y, NxCloneAxes.Z }.Count(axis => (axes & axis) != 0);

        static NxCloneAxes AxisChoices(NxCloneAxes axes, string x, string y, string z)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var bits = new[] { NxCloneAxes.X, NxCloneAxes.Y, NxCloneAxes.Z };
                var labels = new[] { x, y, z };
                for (int i = 0; i < bits.Length; i++)
                    axes = Toggle(labels[i], (axes & bits[i]) != 0) ? axes | bits[i] : axes & ~bits[i];
            }
            return axes;
        }

        void EnsureAfterimageColors()
        {
            if (afterimageColors == null) afterimageColors = new List<Color>();
            Color.RGBToHSV(afterimageColor, out float hue, out float saturation, out float value);
            while (afterimageColors.Count < afterimageCount)
            {
                var color = Color.HSVToRGB(Mathf.Repeat(hue + afterimageColors.Count * 0.17f, 1f), saturation, value);
                color.a = afterimageColor.a;
                afterimageColors.Add(color);
            }
        }

        void LoadPreset()
        {
            slots = preset.slots == null || preset.slots.Count == 0
                ? new List<NxCloneSlot> { new NxCloneSlot() }
                : preset.slots.Take(4).Select(slot => slot == null ? new NxCloneSlot() : new NxCloneSlot {
                    offset = slot.offset, rotation = slot.rotation, scale = slot.scale, mirror = slot.mirror, movement = slot.movement, attachTo = slot.attachTo,
                    contactAnchor = slot.contactAnchor, contactTag = slot.contactTag, contactAllowSelf = slot.contactAllowSelf, contactAllowOthers = slot.contactAllowOthers
                }).ToList();
            writeDefaults = preset.writeDefaults;
            worldDrop = preset.worldDrop;
            poseFreeze = preset.poseFreeze;
            copyVisemes = preset.copyVisemes;
            copyFxAnimations = preset.copyFxAnimations;
            independentCloneFx = preset.independentCloneFx;
            deferParameterBudgetToVrcfury = preset.deferParameterBudgetToVrcfury;
            runtimeScale = preset.runtimeScale;
            runtimePosition = preset.runtimePosition;
            positionAxes = preset.positionAxes;
            runtimeRotation = preset.runtimeRotation;
            rotationAxes = preset.rotationAxes;
            recordingDuration = preset.recordingDuration;
            recordingSamples = preset.recordingSamples;
            posing = preset.posing;
            limbIk = preset.limbIk;
            limbContacts = preset.limbContacts;
            wear = preset.wear;
            recordingSamples = EffectiveRecordingSamples();
            recording = preset.recording;
            cloneGesture = preset.cloneGesture;
            cloneGestureHand = preset.cloneGestureHand;
            afterimageGesture = preset.afterimageGesture;
            afterimageGestureHand = preset.afterimageGestureHand;
            afterimages = preset.afterimages;
            afterimageCount = preset.afterimageCount;
            damping = preset.afterimageFollowStrength;
            afterimageColor = preset.afterimageColor;
            distinctAfterimageColors = preset.distinctAfterimageColors;
            afterimageColors = new List<Color>(preset.afterimageColors ?? new List<Color>());
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
            preset.slots = slots.Select(slot => new NxCloneSlot { offset = slot.offset, rotation = slot.rotation, scale = slot.scale, mirror = slot.mirror, movement = slot.movement, attachTo = slot.attachTo, contactAnchor = slot.contactAnchor, contactTag = slot.contactTag, contactAllowSelf = slot.contactAllowSelf, contactAllowOthers = slot.contactAllowOthers }).ToList();
            preset.writeDefaults = writeDefaults;
            preset.worldDrop = worldDrop;
            preset.poseFreeze = poseFreeze;
            preset.copyVisemes = copyVisemes;
            preset.copyFxAnimations = copyFxAnimations;
            preset.independentCloneFx = independentCloneFx;
            preset.deferParameterBudgetToVrcfury = deferParameterBudgetToVrcfury;
            preset.runtimeScale = runtimeScale;
            preset.runtimePosition = runtimePosition;
            preset.positionAxes = positionAxes;
            preset.runtimeRotation = runtimeRotation;
            preset.rotationAxes = rotationAxes;
            preset.recordingDuration = recordingDuration;
            preset.recordingSamples = recordingSamples;
            preset.posing = posing;
            preset.limbIk = limbIk;
            preset.limbContacts = limbContacts;
            preset.wear = wear;
            preset.recording = recording;
            preset.cloneGesture = cloneGesture;
            preset.cloneGestureHand = cloneGestureHand;
            preset.afterimageGesture = afterimageGesture;
            preset.afterimageGestureHand = afterimageGestureHand;
            preset.afterimages = afterimages;
            preset.afterimageCount = afterimageCount;
            preset.afterimageFollowStrength = damping;
            preset.afterimageColor = afterimageColor;
            preset.distinctAfterimageColors = distinctAfterimageColors;
            preset.afterimageColors = new List<Color>(afterimageColors);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
        }

        int RecordingSampleLimit() => 15 - (slots.Any(slot => slot.contactAnchor) ? 1 : 0) - (wear ? 1 : 0);

        int EffectiveRecordingSamples() => Mathf.Min(recordingSamples, RecordingSampleLimit());

        static List<string> Preflight(VRCAvatarDescriptor root, List<NxCloneSlot> slots, bool ghosts, bool drop, bool freeze, bool scale, bool record = false, bool pose = false, bool deferParameterBudget = false, bool wearing = false, bool ik = false, bool remoteLimbs = false, bool positionControls = false, bool rotationControls = false, NxCloneAxes positionAxes = NxCloneAxes.All, NxCloneAxes rotationAxes = NxCloneAxes.All)
        {
            var issues = new List<string>();
            if (!root) { issues.Add("Choose a scene avatar with a VRC Avatar Descriptor."); return issues; }
            bool canDeferParameterBudget = deferParameterBudget && HasVrcfuryParameterCompressor();
            if (deferParameterBudget && !canDeferParameterBudget)
                issues.Add("Deferred parameter budgeting is enabled, but VRCFury's Parameter Compressor is not installed and enabled.");
            if (EditorUtility.IsPersistent(root)) issues.Add("Root must be a scene object. Drag the avatar into a scene first.");
            if (root.transform.Find("nxclone")) issues.Add("Root already contains nxclone output. Select the original avatar to avoid nesting clones.");
            var rootAnimator = root.GetComponent<Animator>();
            if (!rootAnimator || !rootAnimator.avatar || !rootAnimator.avatar.isHuman)
                issues.Add("Root needs an Animator with a valid humanoid Avatar. Set model Rig to Humanoid and fix its mapping.");
            if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                issues.Add("Root needs at least one SkinnedMeshRenderer.");
            if (slots == null || slots.Count < 1 || slots.Count > 4) issues.Add("Choose one to four clone slots.");
            else for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null) { issues.Add($"Clone {i + 1}: slot is empty. Remove it or add a new slot."); continue; }
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
                if (slot.contactAnchor && string.IsNullOrWhiteSpace(slot.contactTag))
                    issues.Add($"Clone {i + 1}: enter the tag used by the remote Contact Sender.");
                if (slot.contactAnchor && !slot.contactAllowSelf && !slot.contactAllowOthers)
                    issues.Add($"Clone {i + 1}: allow self contacts or other-avatar contacts.");
                if (slot.anchor && !slot.anchor.IsChildOf(root.transform) && slot.anchor != root.transform)
                    issues.Add($"Clone {i + 1}: custom anchor must belong to the root avatar so it exists after upload.");
                if (!slot.anchor && slot.attachTo != NxAttachPoint.Root && (!rootAnimator || !rootAnimator.avatar || !rootAnimator.avatar.isHuman || !rootAnimator.GetBoneTransform(AttachBone(slot.attachTo))))
                    issues.Add($"Clone {i + 1}: root rig has no {slot.attachTo} bone for attachment.");
                var sourceBones = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.bones ?? Array.Empty<Transform>()).Where(t => t).Distinct().ToArray();
                int external = sourceBones.Count(t => !t.IsChildOf(source.transform));
                if (external > 0) issues.Add($"Clone {i + 1}: {external} bones reference objects outside its source. Rebind the SkinnedMeshRenderer bones.");

            }
            if (root.baseAnimationLayers != null)
                foreach (var layer in root.baseAnimationLayers)
                    if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault && layer.animatorController && !(layer.animatorController is AnimatorController))
                        issues.Add("FX layer uses an Animator Override Controller. Assign a regular Animator Controller before generating so its animations are preserved.");
            if (!Fx(root) && !AssetDatabase.LoadAssetAtPath<AnimatorController>(DefaultFxPath))
                issues.Add("VRChat's default FX controller is missing. Reinstall or update the Avatars SDK package.");
            if ((positionAxes & ~NxCloneAxes.All) != 0 || (rotationAxes & ~NxCloneAxes.All) != 0)
                issues.Add("Placement axis selection is invalid. Choose X, Y and/or Z axes.");
            if (root.customExpressions)
            {
                int needed = 1 + slots.Count + (ghosts ? 1 : 0) + (drop ? slots.Count : 0) + (freeze ? slots.Count : 0) + (scale ? 8 : 0) + (record ? 10 * slots.Count : 0) + (pose || ik ? slots.Count : 0) + (wearing ? 8 : 0) + (ik ? slots.Count : 0) + slots.Count(slot => slot.contactAnchor) + (ik && remoteLimbs ? 4 * slots.Count : 0) + (positionControls ? 8 * AxisCount(positionAxes) * slots.Count : 0) + (rotationControls ? 8 * AxisCount(rotationAxes) * slots.Count : 0);
                if (!canDeferParameterBudget && root.expressionParameters && root.expressionParameters.CalcTotalCost() + needed > VRCExpressionParameters.MAX_PARAMETER_COST)
                    issues.Add($"Expression Parameters need {needed} free bits for nxclone controls.");

            }
            if (ghosts && (!Shader.Find("nxclone/solid translucent") || !Shader.Find("nxclone/silhouette mask")))
                issues.Add("Afterimage shader has not imported. Reimport the nxclone package.");
            return issues;
        }

        static bool UnderGeneratedMask(Transform target, Transform avatarRoot)
        {
            for (var parent = target; parent && parent != avatarRoot; parent = parent.parent)
                if (parent.name.StartsWith("__nxclone", StringComparison.Ordinal)) return true;
            return false;
        }

        static bool HasVrcfuryParameterCompressor() => AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => assembly.GetType("VF.Hooks.ParameterCompressorHook", false) != null);

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
            var issues = Preflight(avatar, slots, afterimages, worldDrop, poseFreeze, runtimeScale, recording, posing, deferParameterBudgetToVrcfury, wear, limbIk, limbContacts, runtimePosition, runtimeRotation, positionAxes, rotationAxes);
            if (issues.Count > 0) { EditorUtility.DisplayDialog("nxclone check", string.Join("\n", issues), "OK"); return; }
            GameObject output = null;
            string folder = null;
            var originalAvatar = avatar;
            var originalSources = slots.Select(slot => slot.source).ToArray();
            var originalAnchors = slots.Select(slot => slot.anchor).ToArray();
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
                    {
                        if (!originalSources[i] || originalSources[i] == originalAvatar) slots[i].source = null;
                        if (originalAnchors[i]) slots[i].anchor = originalAnchors[i] == originalAvatar.transform ? descriptor.transform : descriptor.transform.Find(PathOf(originalAvatar.transform, originalAnchors[i]));
                    }
                BuildVisuals(descriptor, folder, false);
                var setup = output.AddComponent<NxCloneSetup>();
                setup.slots = slots.Select(slot => new NxCloneSetupSlot {
                    source = slot.source, anchor = slot.anchor, offset = slot.offset, rotation = slot.rotation, scale = slot.scale, mirror = slot.mirror, movement = slot.movement,
                    attachTo = (NxCloneAttachPoint)(int)slot.attachTo,
                    contactAnchor = slot.contactAnchor, contactTag = slot.contactTag,
                    contactAllowSelf = slot.contactAllowSelf, contactAllowOthers = slot.contactAllowOthers
                }).ToList();
                setup.writeDefaults = writeDefaults;
                setup.worldDrop = worldDrop;
                setup.poseFreeze = poseFreeze;
                setup.copyVisemes = copyVisemes;
                setup.copyFxAnimations = copyFxAnimations;
                setup.independentCloneFx = independentCloneFx;
                setup.deferParameterBudgetToVrcfury = deferParameterBudgetToVrcfury;
                setup.runtimeScale = runtimeScale;
                setup.runtimePosition = runtimePosition;
                setup.positionAxes = positionAxes;
                setup.runtimeRotation = runtimeRotation;
                setup.rotationAxes = rotationAxes;
                setup.recordingDuration = recordingDuration;
                setup.recordingSamples = EffectiveRecordingSamples();
                setup.posing = posing;
                setup.limbIk = limbIk;
                setup.limbContacts = limbContacts;
                setup.wear = wear;
                setup.recording = recording;
                setup.cloneGesture = cloneGesture;
                setup.cloneGestureHand = cloneGestureHand;
                setup.afterimageGesture = afterimageGesture;
                setup.afterimageGestureHand = afterimageGestureHand;
                setup.afterimages = afterimages;
                setup.afterimageCount = afterimageCount;
                setup.damping = damping;
                setup.afterimageColor = afterimageColor;
                setup.distinctAfterimageColors = distinctAfterimageColors;
                setup.afterimageColors = new List<Color>(afterimageColors);
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
                for (int i = 0; i < slots.Count; i++) { slots[i].source = originalSources[i]; slots[i].anchor = originalAnchors[i]; }
            }
        }

        void BuildVisuals(VRCAvatarDescriptor descriptor, string folder, bool installControls)
        {
            var output = descriptor.gameObject;
            var group = new GameObject("nxclone");
            group.transform.SetParent(output.transform, false);
            var frame = new GameObject("world").transform;
            frame.SetParent(group.transform, false);
            NxClonePlacement.FreezeFrame(frame);
            int built = 0;
            var clones = new List<Transform>();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var source = slot.source ? slot.source : avatar;
                var anchor = slot.anchor ? slot.anchor : slot.attachTo == NxAttachPoint.Root
                    ? output.transform : output.GetComponent<Animator>().GetBoneTransform(AttachBone(slot.attachTo));
                bool dance = slot.movement == NxCloneMovement.DancePartner && slot.attachTo == NxAttachPoint.Root && !slot.anchor;
                var driver = dance
                    ? NxClonePlacement.Dance(frame, anchor, $"placement-{i + 1}", slot.offset, slot.rotation)
                    : NxClonePlacement.Follow(frame, anchor, $"placement-{i + 1}", slot.offset, slot.rotation);
                var clone = BuildVisual(source.gameObject, driver, $"clone-{i + 1}");
                clone.transform.localScale = Vector3.Scale(slot.scale, new Vector3(slot.mirror ? -1f : 1f, 1f, 1f));
                built += ConstrainBones(output.transform, source.transform, clone.transform, false, 1f);
                clone.SetActive(false);
                clones.Add(clone.transform);
            }
            if (afterimages)
            {
                var ghosts = new List<Transform>();
                for (int i = 0; i < afterimageCount; i++)
                {
                    var driver = NxClonePlacement.Delay(frame, output.transform, $"trail-{i + 1}", damping / (i + 1));
                    var ghost = BuildVisual(avatar.gameObject, driver, $"afterimage-{i + 1}");
                    EnsureAfterimageColors();
                    var color = distinctAfterimageColors ? afterimageColors[i] : afterimageColor;
                    var material = new Material(Shader.Find("nxclone/solid translucent"));
                    material.color = color;
                    int stencilBit = 2 << i;
                    material.SetFloat("_StencilBit", stencilBit);
                    // Older trails draw first; the fastest-following trail is newest.
                    material.renderQueue = 3050 + afterimageCount - 1 - i;
                    AssetDatabase.CreateAsset(material, $"{folder}/afterimage-{i + 1}.mat");
                    foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true))
                        renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    built += NxCloneAfterimageRig.Configure(avatar.transform, ghost.transform, damping / (i + 1));
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
                    source = slot.source, anchor = slot.anchor, offset = slot.offset, rotation = slot.rotation, scale = slot.scale, mirror = slot.mirror, movement = slot.movement,
                    attachTo = (NxAttachPoint)(int)slot.attachTo, contactAnchor = slot.contactAnchor, contactTag = slot.contactTag,
                    contactAllowSelf = slot.contactAllowSelf, contactAllowOthers = slot.contactAllowOthers
                }).ToList();
                window.writeDefaults = setup.writeDefaults;
                window.worldDrop = setup.worldDrop;
                window.poseFreeze = setup.poseFreeze;
                window.copyVisemes = setup.copyVisemes;
                window.copyFxAnimations = setup.copyFxAnimations;
                window.independentCloneFx = setup.independentCloneFx;
                window.deferParameterBudgetToVrcfury = setup.deferParameterBudgetToVrcfury;
                window.runtimeScale = setup.runtimeScale;
                window.runtimePosition = setup.runtimePosition;
                window.positionAxes = setup.positionAxes;
                window.runtimeRotation = setup.runtimeRotation;
                window.rotationAxes = setup.rotationAxes;
                window.recordingDuration = setup.recordingDuration;
                window.recordingSamples = setup.recordingSamples;
                window.posing = setup.posing;
                window.limbIk = setup.limbIk;
                window.limbContacts = setup.limbContacts;
                window.wear = setup.wear;
                window.recordingSamples = window.EffectiveRecordingSamples();
                window.recording = setup.recording;
                window.cloneGesture = setup.cloneGesture;
                window.cloneGestureHand = setup.cloneGestureHand;
                window.afterimageGesture = setup.afterimageGesture;
                window.afterimageGestureHand = setup.afterimageGestureHand;
                window.afterimages = setup.afterimages;
                window.afterimageCount = setup.afterimageCount;
                window.damping = setup.damping;
                window.afterimageColor = setup.afterimageColor;
                window.distinctAfterimageColors = setup.distinctAfterimageColors;
                window.afterimageColors = new List<Color>(setup.afterimageColors ?? new List<Color>());
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
                var issues = Preflight(descriptor, window.slots, window.afterimages, window.worldDrop, window.poseFreeze, window.runtimeScale, window.recording, window.posing, window.deferParameterBudgetToVrcfury, window.wear, window.limbIk, window.limbContacts, window.runtimePosition, window.runtimeRotation, window.positionAxes, window.rotationAxes);
                if (issues.Count != 0) throw new InvalidOperationException(string.Join("\n", issues));
                window.BuildVisuals(descriptor, folder, true);
                if (window.deferParameterBudgetToVrcfury)
                    descriptor.gameObject.AddComponent<NxCloneDeferredParameterBudget>();
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
                var sourceBone = source.Find(path);
                var target = sourceBone ? BoneTarget(root, source, sourceBone) : null;
                if (!target) { Debug.LogWarning($"nxclone: no matching root bone for {path}; clone keeps its source pose for this bone."); continue; }
                var rotation = bone.gameObject.AddComponent<VRCRotationConstraint>();
                if (delayed) rotation.Sources.Add(new VRCConstraintSource(bone, 1f));
                rotation.Sources.Add(new VRCConstraintSource(target, weight));
                rotation.SolveInLocalSpace = true;
                rotation.ActivateConstraint();
                rotation.ApplyConfigurationChanges();
                count++;
                var sourceAnimator = source.GetComponent<Animator>();
                if (sourceAnimator && sourceAnimator.isHuman && sourceBone == sourceAnimator.GetBoneTransform(HumanBodyBones.Hips))
                {
                    var position = bone.gameObject.AddComponent<VRCPositionConstraint>();
                    position.SolveInLocalSpace = true;
                    if (delayed) position.Sources.Add(new VRCConstraintSource(bone, 1f));
                    position.Sources.Add(new VRCConstraintSource(target, weight));
                    position.ActivateConstraint();
                    position.ApplyConfigurationChanges();
                    count++;
                }
            }
            return count;
        }

        static Transform BoneTarget(Transform root, Transform source, Transform sourceBone)
        {
            var exact = root.Find(PathOf(source, sourceBone));
            if (exact) return exact;
            var from = source.GetComponent<Animator>();
            var to = root.GetComponent<Animator>();
            if (!from || !to || !from.isHuman || !to.isHuman) return null;
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = (HumanBodyBones)i;
                var sourceHumanoid = from.GetBoneTransform(bone);
                if (!sourceHumanoid) continue;
                if (sourceBone == sourceHumanoid) return to.GetBoneTransform(bone);
            }
            // Non-humanoid chains keep their original shape when their names differ.
            return null;
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
            if (copyFxAnimations && oldFx && !independentCloneFx)
            {
                var sameRootClones = clones.Where((clone, i) => !slots[i].source || slots[i].source == avatar).ToArray();
                NxCloneFxMirror.MirrorFxCurves(fx, descriptor.transform, sameRootClones, folder);
            }
            if (ghosts.Count != 0)
                NxCloneFxMirror.MirrorFxCurves(fx, descriptor.transform, ghosts, folder, silhouetteOnly: true);
            var sourceFxResults = new NxCloneSourceFx.Result[clones.Count];
            var sourceControls = new List<VRCExpressionsMenu.Control>();
            var sourceParameters = new List<VRCExpressionParameters.Parameter>();
            if (copyFxAnimations) for (int i = 0; i < clones.Count; i++)
            {
                var source = slots[i].source ? slots[i].source : avatar;
                var sourceController = source ? Fx(source) : null;
                bool sameRoot = !slots[i].source || source == avatar || source == descriptor;
                if (sameRoot && !independentCloneFx) continue;
                if (!sourceController)
                {
                    if (independentCloneFx)
                    {
                        string alias = AvailableParameter(descriptor, $"nxclone_clone{i + 1}_Viseme", reserved);
                        reserved.Add(alias);
                        var input = fx.parameters.FirstOrDefault(p => p.name == "Viseme");
                        var type = input?.type ?? AnimatorControllerParameterType.Int;
                        fx.AddParameter(alias, type);
                        sourceFxResults[i] = new NxCloneSourceFx.Result {
                            parameters = Array.Empty<VRCExpressionParameters.Parameter>(),
                            expressionInputMappings = new[] { new NxCloneExpressionRecording.Mapping("Viseme", alias, type) }
                        };
                    }
                    continue;
                }
                var budget = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                budget.parameters = (descriptor.expressionParameters && descriptor.expressionParameters.parameters != null ? descriptor.expressionParameters.parameters : Array.Empty<VRCExpressionParameters.Parameter>())
                    .Concat(sourceParameters).ToArray();
                NxCloneSourceFx.Result merged;
                try { merged = NxCloneSourceFx.Merge(fx, sourceController, source.transform, descriptor.transform,
                    clones[i], i + 1, source.expressionParameters, source.expressionsMenu, budget, folder,
                    deferParameterBudgetCheck: deferParameterBudgetToVrcfury, isolateExpressionInputs: independentCloneFx); }
                finally { DestroyImmediate(budget); }
                sourceFxResults[i] = merged;
                sourceParameters.AddRange(merged.parameters);
                if (merged.menu) sourceControls.Add(new VRCExpressionsMenu.Control {
                    name = $"Clone {i + 1} expressions", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = merged.menu
                });
            }
            int controlsLayerStart = fx.layers.Length;
            fx.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            var cloneEnabledParameters = new List<string>();
            for (int i = 0; i < clones.Count; i++)
            {
                string enabled = AvailableParameter(descriptor, $"nxclone_enabled_{i + 1}", reserved);
                reserved.Add(enabled);
                cloneEnabledParameters.Add(enabled);
                fx.AddParameter(new AnimatorControllerParameter { name = enabled, type = AnimatorControllerParameterType.Bool, defaultBool = true });
                var cloneOff = new AnimationClip { name = $"nxclone {i + 1} hidden" };
                var cloneOn = new AnimationClip { name = $"nxclone {i + 1} visible" };
                string path = PathOf(descriptor.transform, clones[i]);
                SetCurve(cloneOff, path, typeof(GameObject), "m_IsActive", 0);
                SetCurve(cloneOn, path, typeof(GameObject), "m_IsActive", 1);
                var reference = NxClonePlacement.DanceReference(clones[i].parent);
                if (reference)
                {
                    string referencePath = PathOf(descriptor.transform, reference);
                    SetCurve(cloneOff, referencePath, typeof(VRCParentConstraint), "FreezeToWorld", 0);
                    SetCurve(cloneOn, referencePath, typeof(VRCParentConstraint), "FreezeToWorld", 1);
                }
                AssetDatabase.CreateAsset(cloneOff, $"{folder}/clone-{i + 1}-off.anim");
                AssetDatabase.CreateAsset(cloneOn, $"{folder}/clone-{i + 1}-on.anim");
                AddVisibleLayer(fx, $"nxclone {i + 1} visible", parameter, enabled, cloneOff, cloneOn);
            }

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
            var freezeParameters = Enumerable.Repeat<string>(null, clones.Count).ToList();
            if (worldDrop) for (int i = 0; i < clones.Count; i++)
            {
                string dropParameter = AvailableParameter(descriptor, $"nxclone_drop_{i + 1}", reserved);
                reserved.Add(dropParameter);
                fx.AddParameter(dropParameter, AnimatorControllerParameterType.Bool);
                var follow = new AnimationClip { name = $"nxclone {i + 1} follow" };
                var placed = new AnimationClip { name = $"nxclone {i + 1} world" };
                string path = PathOf(descriptor.transform, clones[i].parent);
                SetCurve(follow, path, typeof(VRCParentConstraint), "FreezeToWorld", 0);
                SetCurve(placed, path, typeof(VRCParentConstraint), "FreezeToWorld", 1);
                SetCurve(follow, path, typeof(VRCScaleConstraint), "FreezeToWorld", 0);
                SetCurve(placed, path, typeof(VRCScaleConstraint), "FreezeToWorld", 1);
                AssetDatabase.CreateAsset(follow, $"{folder}/clone-{i + 1}-follow.anim");
                AssetDatabase.CreateAsset(placed, $"{folder}/clone-{i + 1}-world.anim");
                AddBoolLayer(fx, $"nxclone {i + 1} placement", dropParameter, follow, placed);
                dropParameters.Add(dropParameter);
            }
            var positionControls = new List<NxClonePositionControls.Result>();
            if (runtimePosition && positionAxes != NxCloneAxes.None) for (int i = 0; i < clones.Count; i++)
            {
                var placement = NxClonePlacement.ControlAnchor(clones[i].parent);
                string prefix = $"nxclone_position_{i + 1}";
                for (int suffix = 2; new[] { "x", "y", "z" }.Any(axis => AvailableParameter(descriptor, prefix + "_" + axis, reserved) != prefix + "_" + axis); suffix++)
                    prefix = $"nxclone_position_{i + 1}_{suffix}";
                var controls = NxClonePositionControls.Configure(fx, descriptor.transform, placement, folder, prefix, enabledAxes: positionAxes);
                foreach (var name in controls.Parameters) reserved.Add(name);
                positionControls.Add(controls);
            }
            var rotationControls = new List<NxCloneRotationControls.Result>();
            if (runtimeRotation && rotationAxes != NxCloneAxes.None) for (int i = 0; i < clones.Count; i++)
            {
                string prefix = $"nxclone_rotation_{i + 1}";
                for (int suffix = 2; new[] { "x", "y", "z" }.Any(axis => AvailableParameter(descriptor, prefix + "_" + axis, reserved) != prefix + "_" + axis); suffix++)
                    prefix = $"nxclone_rotation_{i + 1}_{suffix}";
                var controls = NxCloneRotationControls.Configure(fx, descriptor.transform, clones[i].parent, folder, prefix, enabledAxes: rotationAxes);
                foreach (var name in controls.Parameters) reserved.Add(name);
                rotationControls.Add(controls);
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
            var poseControls = new List<NxClonePosing.Result>();
            if (posing || limbIk) for (int i = 0; i < clones.Count; i++)
            {
                string name = AvailableParameter(descriptor, $"nxclone_posing_{i + 1}", reserved);
                reserved.Add(name);
                fx.AddParameter(name, AnimatorControllerParameterType.Bool);
                var source = slots[i].source ? slots[i].source : avatar;
                var entry = NxClonePosing.Configure(source.GetComponent<Animator>(), source.transform, clones[i], folder, i + 1, name);
                poseControls.Add(entry);
            }
            if (poseFreeze) for (int i = 0; i < clones.Count; i++)
            {
                string freezeParameter = AvailableParameter(descriptor, $"nxclone_freeze_{i + 1}", reserved);
                reserved.Add(freezeParameter);
                freezeParameters[i] = freezeParameter;
                fx.AddParameter(freezeParameter, AnimatorControllerParameterType.Bool);
                var freeze = NxClonePosing.ConfigureFreeze(clones[i], descriptor.transform, folder, i + 1);
                if (freeze.ConstraintCount > 0)
                    NxClonePosing.AddFreezeLayer(fx, freeze, freezeParameter, false);
                else if (!posing && !limbIk)
                    throw new InvalidOperationException($"Clone {i + 1} has no native skeletal constraints to disable for pose freeze.");
            }
            for (int i = 0; i < poseControls.Count; i++)
                NxClonePosing.AddLayer(fx, poseControls[i], i < freezeParameters.Count ? freezeParameters[i] : null, false, cloneEnabledParameters[i]);
            var recordings = new List<NxCloneRecording.Result>();
            if (recording) for (int i = 0; i < clones.Count; i++)
            {
                string prefix = $"nxclone_pose_{i + 1}";
                while (fx.parameters.Any(p => p.name == prefix + "_record" || p.name == prefix + "_play" || p.name == prefix + "_speed")) prefix += "_copy";
                recordings.Add(NxCloneRecording.Configure(fx, descriptor.transform, clones[i], folder, prefix, EffectiveRecordingSamples(), recordingDuration));
            }
            var expressionRecordings = new List<NxCloneExpressionRecording.Result>();
            for (int i = 0; i < sourceFxResults.Length; i++)
            {
                var merge = sourceFxResults[i];
                if (merge == null || merge.expressionInputMappings.Length == 0) continue;
                var bodyRecording = recordings.Count > i ? recordings[i] : null;
                NxCloneSourceFx.AddExpressionInputCopyLayer(fx, merge, descriptor.transform, folder,
                    bodyRecording?.PlayParameter);
                if (bodyRecording == null) continue;
                var mappings = merge.expressionInputMappings.Concat(merge.parameters
                    .Where(p => p.name != merge.stateLockParameterName)
                    .Select(p => fx.parameters.Single(controllerParameter => controllerParameter.name == p.name))
                    .Select(p => new NxCloneExpressionRecording.Mapping(p.name, p.name, p.type))).ToArray();
                expressionRecordings.Add(NxCloneExpressionRecording.Configure(fx, bodyRecording,
                    descriptor.transform, folder, $"nxclone_expression_{i + 1}", mappings));
            }
            var ikControls = new List<NxCloneLimbIk.Result>();
            if (limbIk) for (int i = 0; i < clones.Count; i++)
            {
                var source = slots[i].source ? slots[i].source : avatar;
                string name = AvailableParameter(descriptor, $"nxclone_ik_{i + 1}", reserved);
                reserved.Add(name);
                fx.AddParameter(name, AnimatorControllerParameterType.Bool);
                var ik = NxCloneLimbIk.Configure(source.GetComponent<Animator>(), source.transform,
                    clones[i], clones[i].parent, descriptor.transform, folder, i + 1, name);
                NxCloneLimbIk.AddLayer(fx, ik, false, freezeParameters[i], cloneEnabledParameters[i],
                    recordings.Count > i ? recordings[i].PlayParameter : null, parameter);
                ikControls.Add(ik);
            }
            var contactAnchors = new List<NxCloneContactAnchor.Result>();
            for (int i = 0; i < clones.Count; i++)
            {
                var slot = slots[i];
                if (!slot.contactAnchor) continue;
                var contact = NxCloneContactAnchor.Configure(descriptor.transform, clones[i].parent, fx,
                    descriptor.expressionParameters, i + 1, folder, $"nxclone-{i + 1}", slot.contactTag,
                    slot.contactAllowSelf, slot.contactAllowOthers);
                contactAnchors.Add(contact);
                sourceParameters.AddRange(contact.parameters);
                sourceControls.Add(new VRCExpressionsMenu.Control {
                    name = $"Clone {i + 1} contact attach", type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = contact.menu
                });
            }
            var limbContactControls = new List<NxCloneLimbContacts.Result>();
            if (limbIk && limbContacts) for (int i = 0; i < clones.Count; i++)
            {
                var contacts = NxCloneLimbContacts.Configure(descriptor.transform, clones[i].parent,
                    ikControls[i], fx, descriptor.expressionParameters, i + 1, folder, $"nxclone-limbs-{i + 1}");
                limbContactControls.Add(contacts);
                foreach (var contact in contacts.Contacts)
                {
                    contactAnchors.Add(contact);
                    sourceParameters.AddRange(contact.parameters);
                }
            }
            if (copyVisemes) for (int i = 0; i < clones.Count; i++)
            {
                var source = slots[i].source ? slots[i].source : avatar;
                var visemeInput = sourceFxResults[i]?.expressionInputMappings.FirstOrDefault(mapping => mapping.Input == "Viseme").Target;
                if (!NxCloneVisemes.AddCloneVisemes(fx, source, descriptor.transform, clones[i], folder,
                    $"nxclone {i + 1} visemes", string.IsNullOrEmpty(visemeInput) ? "Viseme" : visemeInput))
                    Debug.LogWarning($"nxclone: clone {i + 1} has no compatible blendshape viseme mapping; viseme copying skipped.", descriptor);
            }
            if (copyVisemes) for (int i = 0; i < ghosts.Count; i++)
                NxCloneVisemes.AddCloneVisemes(fx, descriptor, descriptor.transform, ghosts[i], folder, $"nxclone afterimage {i + 1} visemes");
            NxCloneWear.Result wearControls = null;
            if (wear)
            {
                var originalRenderers = descriptor.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => !renderer.transform.IsChildOf(group) &&
                        !UnderGeneratedMask(renderer.transform, descriptor.transform))
                    .ToArray();
                var primaryMask = ghosts.FirstOrDefault(ghost => ghost.name == "__nxclone main silhouette mask");
                Transform[] wornMasks = null;
                if (primaryMask)
                {
                    var maskMaterial = primaryMask.GetComponentsInChildren<Renderer>(true).First().sharedMaterial;
                    wornMasks = clones.Select((clone, i) => {
                        var mask = NxCloneAfterimages.CreateMainSilhouetteMask(clone, descriptor.transform, maskMaterial);
                        mask.name = $"__nxclone worn silhouette mask {i + 1}";
                        mask.gameObject.SetActive(false);
                        NxCloneFxMirror.MirrorFxCurves(fx, descriptor.transform, new[] { mask }, folder,
                            silhouetteOnly: true, sourceVisual: clone);
                        return mask;
                    }).ToArray();
                }
                string wearParameter = AvailableParameter(descriptor, "nxclone_wear", reserved);
                reserved.Add(wearParameter);
                wearControls = NxCloneWear.Configure(fx, descriptor.transform, originalRenderers,
                    clones.Select((clone, i) => new NxCloneWear.Slot {
                        Name = $"clone {i + 1}", Driver = clone.parent, Visual = clone,
                        VisibleParameter = cloneEnabledParameters[i]
                    }).ToArray(), wearParameter, parameter, ghostParameter, primaryMask, wornMasks, folder);
                AssetDatabase.CreateAsset(wearControls.Menu, $"{folder}/wear-menu.asset");
            }
            var gestureLayers = new[] {
                NxCloneGestureControls.Configure(fx, parameter, cloneGesture, cloneGestureHand, "clones"),
                ghostParameter == null ? null : NxCloneGestureControls.Configure(fx, ghostParameter, afterimageGesture, afterimageGestureHand, "afterimages")
            };
            var originalStates = oldFx ? oldFx.layers.SelectMany(layer => States(layer.stateMachine)).ToArray() : Array.Empty<AnimatorState>();
            bool generatedDefaults = writeDefaults == NxCloneWriteDefaults.On ||
                writeDefaults == NxCloneWriteDefaults.Auto && originalStates.Length > 0 && originalStates.All(state => state.writeDefaultValues);
            var alwaysWriteDefaults = new HashSet<string>(contactAnchors.SelectMany(contact => contact.writeDefaultsOnLayers), StringComparer.Ordinal);
            var alwaysOffDefaults = new HashSet<string>(recordings.SelectMany(recording =>
                new[] { recording.CaptureLayerName, recording.PlaybackLayerName, recording.CommandLayerName }).Concat(gestureLayers.Where(name => name != null)), StringComparer.Ordinal);
            alwaysOffDefaults.UnionWith(positionControls.SelectMany(entry => entry.LayerNames));
            alwaysOffDefaults.UnionWith(rotationControls.SelectMany(entry => entry.LayerNames));
            alwaysOffDefaults.UnionWith(expressionRecordings.SelectMany(entry => new[] { entry.CaptureLayerName, entry.PlaybackLayerName }));
            alwaysOffDefaults.UnionWith(fx.layers.Where(layer => layer.name.StartsWith("nxclone expression input copy ", StringComparison.Ordinal)).Select(layer => layer.name));
            alwaysOffDefaults.UnionWith(ikControls.SelectMany(ik => new[] { ik.LayerName, ik.SelectorLayerName }));
            if (wearControls != null)
            {
                alwaysOffDefaults.UnionWith(wearControls.VisualLayerNames.Append(wearControls.SelectorLayerName));
                if (wearControls.BaselineLayerName != null) alwaysOffDefaults.Add(wearControls.BaselineLayerName);
            }
            foreach (var layer in fx.layers.Skip(controlsLayerStart))
                foreach (var state in States(layer.stateMachine))
                    state.writeDefaultValues = !alwaysOffDefaults.Contains(layer.name) &&
                        (alwaysWriteDefaults.Contains(layer.name) || generatedDefaults);
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
            foreach (var enabled in cloneEnabledParameters)
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = enabled, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 1, saved = false, networkSynced = true
                });
            foreach (var dropParameter in dropParameters)
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = dropParameter, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 0, saved = false, networkSynced = true
                });
            foreach (var freezeParameter in freezeParameters.Where(name => !string.IsNullOrEmpty(name)))
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = freezeParameter, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 0, saved = false, networkSynced = true
                });
            if (scaleParameter != null)
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = scaleParameter, valueType = VRCExpressionParameters.ValueType.Float, defaultValue = 0.5f, saved = true, networkSynced = true
                });
            foreach (var entry in positionControls.Select(entry => entry.Parameters).Concat(rotationControls.Select(entry => entry.Parameters)))
                foreach (var name in entry)
                    addedParameters.Add(new VRCExpressionParameters.Parameter {
                        name = name, valueType = VRCExpressionParameters.ValueType.Float,
                        defaultValue = 0.5f, saved = false, networkSynced = true
                    });
            foreach (var entry in poseControls)
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = entry.ParameterName, valueType = VRCExpressionParameters.ValueType.Bool, saved = false, networkSynced = true
                });
            foreach (var entry in ikControls) addedParameters.Add(new VRCExpressionParameters.Parameter {
                name = entry.ParameterName, valueType = VRCExpressionParameters.ValueType.Bool,
                defaultValue = 0, saved = false, networkSynced = true
            });
            foreach (var entry in recordings)
            {
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = entry.RecordParameter, valueType = VRCExpressionParameters.ValueType.Bool, saved = false, networkSynced = false
                });
                foreach (var name in new[] { entry.TakeParameter, entry.PlayParameter })
                    addedParameters.Add(new VRCExpressionParameters.Parameter {
                        name = name, valueType = VRCExpressionParameters.ValueType.Bool, saved = false, networkSynced = true
                    });
                addedParameters.Add(new VRCExpressionParameters.Parameter {
                    name = entry.SpeedParameter, valueType = VRCExpressionParameters.ValueType.Float, defaultValue = 0.5f, saved = false, networkSynced = true
                });
            }
            if (wearControls != null) addedParameters.Add(new VRCExpressionParameters.Parameter {
                name = wearControls.ParameterName, valueType = VRCExpressionParameters.ValueType.Int,
                defaultValue = 0, saved = false, networkSynced = true
            });
            parameters.parameters = (parameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>()).Concat(sourceParameters).Concat(addedParameters).ToArray();
            if (!deferParameterBudgetToVrcfury && parameters.CalcTotalCost() > VRCExpressionParameters.MAX_PARAMETER_COST)
                throw new InvalidOperationException($"nxclone and source menus use {parameters.CalcTotalCost()} synced bits; maximum is {VRCExpressionParameters.MAX_PARAMETER_COST}. Reduce controls or compress source parameters.");
            AssetDatabase.CreateAsset(parameters, $"{folder}/parameters.asset");
            descriptor.expressionParameters = parameters;
            var menu = descriptor.customExpressions && descriptor.expressionsMenu
                ? Instantiate(descriptor.expressionsMenu)
                : ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "nxclone menu";
            if (menu.controls == null) menu.controls = new List<VRCExpressionsMenu.Control>();
            if (menu.controls.Count >= 8)
            {
                AssetDatabase.CreateAsset(menu, $"{folder}/original-avatar-menu.asset");
                var wrapper = CreateInstance<VRCExpressionsMenu>();
                wrapper.name = "nxclone avatar menu";
                wrapper.controls = new List<VRCExpressionsMenu.Control> { new VRCExpressionsMenu.Control {
                    name = "Avatar", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu
                } };
                menu = wrapper;
            }
            var toggle = new VRCExpressionsMenu.Control {
                name = "nxclone", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 1
            };
            if (clones.Count == 0) menu.controls.Add(toggle);
            else
            {
                var subMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                subMenu.name = "nxclone controls";
                if (subMenu.controls == null) subMenu.controls = new List<VRCExpressionsMenu.Control>();
                subMenu.controls.Add(toggle);
                if (wearControls != null) subMenu.controls.Add(new VRCExpressionsMenu.Control {
                    name = "Wear a clone", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = wearControls.Menu
                });
                for (int i = 0; i < clones.Count; i++)
                {
                    var cloneMenu = CreateInstance<VRCExpressionsMenu>();
                    cloneMenu.name = $"nxclone {i + 1} controls";
                    cloneMenu.controls = new List<VRCExpressionsMenu.Control>();
                    cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Enabled", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = cloneEnabledParameters[i] }, value = 1
                    });
                    if (i < positionControls.Count) cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Position", type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = positionControls[i].Menu
                    });
                    if (i < rotationControls.Count) cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Rotation", type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = rotationControls[i].Menu
                    });
                    if (i < dropParameters.Count) cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "World drop", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = dropParameters[i] }, value = 1
                    });
                    if (i < freezeParameters.Count && !string.IsNullOrEmpty(freezeParameters[i])) cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Freeze pose", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = freezeParameters[i] }, value = 1
                    });
                    if (i < poseControls.Count) cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Grab limbs / pose", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = poseControls[i].ParameterName }, value = 1
                    });
                    if (i < limbContactControls.Count)
                    {
                        var contactsMenu = CreateInstance<VRCExpressionsMenu>();
                        contactsMenu.name = $"nxclone {i + 1} limb contacts";
                        var names = new[] { "Left hand", "Right hand", "Left foot", "Right foot" };
                        contactsMenu.controls = limbContactControls[i].Contacts.Select((contact, limb) => new VRCExpressionsMenu.Control {
                            name = names[limb], type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = contact.menu
                        }).ToList();
                        AssetDatabase.CreateAsset(contactsMenu, $"{folder}/clone-{i + 1}-limb-contacts-menu.asset");
                        cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                            name = "Attach limbs", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = contactsMenu
                        });
                    }
                    if (i < ikControls.Count) cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Limb IK", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = ikControls[i].ParameterName }, value = 1
                    });
                    if (i < recordings.Count)
                    {
                        var entry = recordings[i];
                        cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                            name = "Record pose", type = VRCExpressionsMenu.Control.ControlType.Button,
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = entry.RecordParameter }, value = 1
                        });
                        cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                            name = "Play recording", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = entry.PlayParameter }, value = 1
                        });
                        cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                            name = "Playback speed", type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                            subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = entry.SpeedParameter } }
                        });
                    }
                    var contactControl = sourceControls.FirstOrDefault(control => control.name == $"Clone {i + 1} contact attach");
                    if (contactControl != null) cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Contact attach", type = contactControl.type, subMenu = contactControl.subMenu
                    });
                    var expressionControl = sourceControls.FirstOrDefault(control => control.name == $"Clone {i + 1} expressions");
                    if (expressionControl != null) cloneMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = "Expressions", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = expressionControl.subMenu
                    });
                    Paginate(cloneMenu, folder);
                    AssetDatabase.CreateAsset(cloneMenu, $"{folder}/clone-{i + 1}-controls.asset");
                    subMenu.controls.Add(new VRCExpressionsMenu.Control {
                        name = $"Clone {i + 1}", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = cloneMenu
                    });
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
                Paginate(subMenu, folder);
                AssetDatabase.CreateAsset(subMenu, $"{folder}/controls.asset");
                menu.controls.Add(new VRCExpressionsMenu.Control {
                    name = "nxclone", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = subMenu
                });
            }
            AssetDatabase.CreateAsset(menu, $"{folder}/menu.asset");
            descriptor.expressionsMenu = menu;
            descriptor.customExpressions = true;
        }

        static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            if (!machine) yield break;
            foreach (var state in machine.states) yield return state.state;
            foreach (var child in machine.stateMachines)
                foreach (var state in States(child.stateMachine)) yield return state;
        }

        static void Paginate(VRCExpressionsMenu menu, string folder)
        {
            int page = 1;
            while (menu.controls.Count > 8)
            {
                var next = CreateInstance<VRCExpressionsMenu>();
                next.name = "nxclone more controls";
                next.controls = menu.controls.Skip(7).ToList();
                menu.controls = menu.controls.Take(7).ToList();
                menu.controls.Add(new VRCExpressionsMenu.Control {
                    name = "More", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = next
                });
                AssetDatabase.CreateAsset(next, AssetDatabase.GenerateUniqueAssetPath($"{folder}/controls-page-{page++}.asset"));
                menu = next;
            }
        }

        static void AddVisibleLayer(AnimatorController fx, string name, string master, string enabled, AnimationClip off, AnimationClip on)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var hidden = machine.AddState("hidden");
            hidden.motion = off;
            var visible = machine.AddState("visible");
            visible.motion = on;
            machine.defaultState = hidden;
            var enter = hidden.AddTransition(visible);
            enter.hasExitTime = false; enter.duration = 0;
            enter.AddCondition(AnimatorConditionMode.If, 0, master);
            enter.AddCondition(AnimatorConditionMode.If, 0, enabled);
            foreach (var parameter in new[] { master, enabled })
            {
                var exit = visible.AddTransition(hidden);
                exit.hasExitTime = false; exit.duration = 0;
                exit.AddCondition(AnimatorConditionMode.IfNot, 0, parameter);
            }
            fx.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1, stateMachine = machine });
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
