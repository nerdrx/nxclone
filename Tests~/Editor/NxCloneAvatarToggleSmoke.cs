using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

public static class NxCloneAvatarToggleSmoke
{
    const string ScenePath = "Assets/NX.unity";
    const string AvatarName = "Nixomi cloned";
    const string OutputRoot = "Assets/nxclone-generated";

    public static void Run()
    {
        try
        {
            RunSmoke();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void RunSourceBaseline()
    {
        try
        {
            RunSourceRockItBaseline();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void RunSmoke()
    {
        var observedRockIt = RunSourceRockItBaseline();
        EditorSceneManager.OpenScene(ScenePath);
        var source = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true)
            .Single(descriptor => descriptor.name == AvatarName);
        if (source.GetComponent<NxCloneSetup>())
            throw new Exception("Fixture source avatar already has an nxclone setup.");

        var originalComponents = source.GetComponentsInChildren<Component>(true).Length;
        var originalFx = Fx(source);
        var originalFxLayerCount = originalFx ? originalFx.layers.Length : 0;
        var originalFxParameterCount = originalFx ? originalFx.parameters.Length : 0;
        var originalParameters = source.expressionParameters;
        var originalMenu = source.expressionsMenu;
        var originalAssetFolders = new HashSet<string>(AssetDatabase.GetSubFolders(OutputRoot), StringComparer.Ordinal);
        var window = ScriptableObject.CreateInstance<NxCloneWindow>();
        GameObject generated = null;
        GameObject uploadCopy = null;

        try
        {
            Set(window, "avatar", source);
            Set(window, "copyFxAnimations", true);
            Set(window, "independentCloneFx", true);
            Set(window, "excludeGoGoLoco", true);
            Set(window, "deferParameterBudgetToVrcfury", true);
            Set(window, "afterimages", false);
            Set(window, "worldDrop", true);
            Set(window, "poseFreeze", true);
            Set(window, "runtimePosition", true);
            Set(window, "runtimeRotation", true);
            Set(window, "runtimeScale", true);
            Set(window, "posing", true);

            typeof(NxCloneWindow).GetMethod("Generate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, null);
            generated = Selection.activeGameObject;
            var setup = generated ? generated.GetComponent<NxCloneSetup>() : null;
            if (!generated || generated == source.gameObject || !setup)
                throw new Exception("Generate did not create the expected one-clone preview and setup.");
            if (setup.slots == null || setup.slots.Count != 1 || setup.slots[0].source)
                throw new Exception("Fixture must use one root-sourced clone.");
            if (setup.afterimages)
                throw new Exception("Fixture unexpectedly enabled afterimages.");

            uploadCopy = UnityEngine.Object.Instantiate(generated);
            uploadCopy.name = "__nxclone avatar toggle smoke upload copy";
            var accepted = VRCBuildPipelineCallbacks.OnPreprocessAvatar(uploadCopy);
            if (!accepted)
                throw new Exception("VRChat SDK preprocessing rejected generated avatar.");
            if (uploadCopy.GetComponent<NxCloneSetup>())
                throw new Exception("Upload pipeline did not consume the generated setup.");

            var descriptor = uploadCopy.GetComponent<VRCAvatarDescriptor>();
            var fx = Fx(descriptor);
            if (!descriptor || !fx || !descriptor.expressionsMenu || !descriptor.expressionParameters)
                throw new Exception("Finalized avatar is missing descriptor FX, menu, or expression parameters.");
            var offLayers = fx.layers.Where(layer => States(layer.stateMachine).Any(state => !state.writeDefaultValues))
                .Select(layer => layer.name).ToArray();
            if (offLayers.Length > 0)
                throw new Exception("nxclone introduced Write Defaults Off into the supplied Force-On avatar: " + string.Join(", ", offLayers));
            var mainStates = fx.layers.Where(layer => !layer.name.StartsWith("nxclone", StringComparison.Ordinal))
                .SelectMany(layer => States(layer.stateMachine));
            if (mainStates.SelectMany(state => state.transitions).SelectMany(transition => transition.conditions)
                .Any(condition => condition.parameter.StartsWith("nxclone_clone", StringComparison.Ordinal)))
                throw new Exception("Clone merge rewrote a main-avatar transition to a clone parameter.");
            var directWeights = fx.layers.Where(layer => layer.name.StartsWith("nxclone 1 ", StringComparison.Ordinal))
                .SelectMany(layer => States(layer.stateMachine)).SelectMany(state => Trees(state.motion))
                .Where(tree => tree.blendType == BlendTreeType.Direct).SelectMany(tree => tree.children)
                .Select(child => child.directBlendParameter).Where(name => !string.IsNullOrEmpty(name)).ToArray();
            if (!directWeights.Any(name => name.StartsWith("nxclone_clone1_", StringComparison.Ordinal)) ||
                directWeights.Any(name => Regex.IsMatch(name, @"^VF\d+_")))
                throw new Exception("Baked clone Direct Blend Tree weights were not isolated.");

            var avatarTogglesControl = FindControl(descriptor.expressionsMenu,
                control => control.name == "Avatar toggles");
            if (avatarTogglesControl == null || !avatarTogglesControl.subMenu)
                throw new Exception("Final avatar menu lost the displayed Avatar toggles submenu.");
            var cloneMenu = avatarTogglesControl.subMenu;

            var cloneVisual = uploadCopy.transform.Find("nxclone/world/placement-1/clone-1");
            if (!cloneVisual)
                throw new Exception("Finalized avatar is missing the generated clone visual.");
            if (!cloneVisual.GetComponentsInChildren<Component>(true).Any(component => component &&
                    component.GetType().FullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver"))
                throw new Exception("Independent FX clone did not retain the source contact receivers.");

            var menuParameterNames = Menus(cloneMenu)
                .SelectMany(menu => menu.controls ?? new List<VRCExpressionsMenu.Control>())
                .SelectMany(control => new[] { control.parameter == null ? null : control.parameter.name }
                    .Concat(control.subParameters == null ? Enumerable.Empty<string>() : control.subParameters.Select(parameter => parameter == null ? null : parameter.name)))
                .Where(name => !string.IsNullOrEmpty(name))
                .ToArray();
            if (menuParameterNames.Any(name => !name.StartsWith("nxclone_clone1_", StringComparison.Ordinal)))
                throw new Exception("Copied menu has an output that is not isolated to the clone: " +
                    string.Join(", ", menuParameterNames.Where(name => !name.StartsWith("nxclone_clone1_", StringComparison.Ordinal)).Distinct()));

            foreach (string retained in new[] { "SPS", "Rock-it", "Dynamics", "Look" })
                if (!(cloneMenu.controls ?? new List<VRCExpressionsMenu.Control>()).Any(control =>
                    string.Equals(control.name, retained, StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("Copied clone menu lost source control '" + retained + "'.");

            foreach (string retainedToggle in new[] { "Wet Effect On", "Shoot", "Gold Stream", "Drip", "SPS On" })
            {
                var control = FindControl(cloneMenu, candidate => candidate.name == retainedToggle);
                if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.Toggle ||
                    control.parameter == null || string.IsNullOrEmpty(control.parameter.name))
                    throw new Exception("Copied clone menu lost source toggle '" + retainedToggle + "'.");
                if (!control.parameter.name.StartsWith("nxclone_clone1_", StringComparison.Ordinal))
                    throw new Exception("Source toggle '" + retainedToggle + "' kept its shared parameter name.");
                if (!fx.parameters.Any(parameter => parameter.name == control.parameter.name))
                    throw new Exception("Copied toggle parameter is missing from clone FX: " + control.parameter.name);
            }

            if (Menus(cloneMenu).SelectMany(menu => menu.controls ?? new List<VRCExpressionsMenu.Control>())
                .Any(control => IsGoGoName(control.name) || control.subMenu && IsGoGoName(control.subMenu.name)))
                throw new Exception("GoGo Loco menu controls remained in the independent clone menu.");

            var copiedGoLayers = fx.layers.Where(layer => layer.name.StartsWith("nxclone 1 ", StringComparison.Ordinal) &&
                (layer.name.IndexOf("Go/Beyond", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 layer.name.IndexOf("GoGo", StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            if (copiedGoLayers.Any(layer => States(layer.stateMachine).Any()))
                throw new Exception("GoGo Loco FX states remained in the clone controller.");
            if ((descriptor.expressionParameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                .Any(parameter => parameter.name.StartsWith("nxclone_clone1_Go/", StringComparison.Ordinal)))
                throw new Exception("GoGo Loco expression parameters remained in the clone parameter asset.");

            RunNativeToggleProbes(uploadCopy, descriptor, fx, cloneMenu, observedRockIt);

            var cloneClips = fx.layers.Where(layer => layer.name.StartsWith("nxclone 1 ", StringComparison.Ordinal))
                .SelectMany(layer => States(layer.stateMachine))
                .SelectMany(state => Clips(state.motion))
                .Distinct()
                .ToArray();
            var cloneBindings = cloneClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)).ToArray();
            if (!cloneBindings.Any(binding => binding.propertyName == "material._SPS_Enabled" &&
                    binding.path.Contains("/clone-1/") && binding.path.Contains("mesh_zwShaft")))
                throw new Exception("Copied SPS FX lost the baked clone mesh material enable curve.");
            if (!cloneBindings.Any(binding => binding.propertyName == "m_IsActive" &&
                    binding.path.Contains("/clone-1/") && binding.path.Contains("BakedSpsPlug")))
                throw new Exception("Copied SPS FX lost the baked clone plug GameObject enable curve.");

            if (source.GetComponentsInChildren<Component>(true).Length != originalComponents ||
                source.expressionParameters != originalParameters || source.expressionsMenu != originalMenu ||
                (originalFx && (originalFx.layers.Length != originalFxLayerCount || originalFx.parameters.Length != originalFxParameterCount)))
                throw new Exception("Smoke generation changed the original scene avatar or its controller assets.");

            Debug.Log($"NXCLONE_AVATAR_TOGGLES_OK finalSyncedBits={descriptor.expressionParameters.CalcTotalCost()} fxLayers={fx.layers.Length} fxParameters={fx.parameters.Length}: native main/clone toggles, baked SPS curves retained, GoGo Loco excluded.");
        }
        finally
        {
            if (uploadCopy) UnityEngine.Object.DestroyImmediate(uploadCopy);
            if (generated) UnityEngine.Object.DestroyImmediate(generated);
            if (window) UnityEngine.Object.DestroyImmediate(window);

            AssetDatabase.SaveAssets();
            foreach (var folder in AssetDatabase.GetSubFolders(OutputRoot))
                if (!originalAssetFolders.Contains(folder)) AssetDatabase.DeleteAsset(folder);
            AssetDatabase.SaveAssets();
        }
    }

    static AnimatorController Fx(VRCAvatarDescriptor descriptor) => descriptor && descriptor.baseAnimationLayers != null
        ? descriptor.baseAnimationLayers.FirstOrDefault(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault)
            .animatorController as AnimatorController
        : null;

    static IEnumerable<VRCExpressionsMenu> Menus(VRCExpressionsMenu root)
    {
        var queue = new Queue<VRCExpressionsMenu>();
        var visited = new HashSet<VRCExpressionsMenu>();
        if (root) queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var menu = queue.Dequeue();
            if (!menu || !visited.Add(menu)) continue;
            yield return menu;
            foreach (var control in menu.controls ?? new List<VRCExpressionsMenu.Control>())
                if (control.subMenu) queue.Enqueue(control.subMenu);
        }
    }

    static VRCExpressionsMenu.Control FindControl(VRCExpressionsMenu root,
        Func<VRCExpressionsMenu.Control, bool> match) => Menus(root)
            .SelectMany(menu => menu.controls ?? new List<VRCExpressionsMenu.Control>())
            .FirstOrDefault(match);

    static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        if (!machine) yield break;
        foreach (var state in machine.states)
            if (state.state) yield return state.state;
        foreach (var child in machine.stateMachines)
            foreach (var state in States(child.stateMachine)) yield return state;
    }

    static IEnumerable<AnimationClip> Clips(Motion motion, HashSet<BlendTree> visited = null)
    {
        if (motion is AnimationClip clip)
        {
            yield return clip;
            yield break;
        }
        if (!(motion is BlendTree tree)) yield break;
        visited = visited ?? new HashSet<BlendTree>();
        if (!visited.Add(tree)) yield break;
        foreach (var child in tree.children)
            foreach (var childClip in Clips(child.motion, visited)) yield return childClip;
    }

    static IEnumerable<BlendTree> Trees(Motion motion, HashSet<BlendTree> visited = null)
    {
        if (!(motion is BlendTree tree)) yield break;
        visited = visited ?? new HashSet<BlendTree>();
        if (!visited.Add(tree)) yield break;
        yield return tree;
        foreach (var child in tree.children)
            foreach (var nested in Trees(child.motion, visited)) yield return nested;
    }

    static void RunNativeToggleProbes(GameObject avatar, VRCAvatarDescriptor descriptor,
        AnimatorController fx, VRCExpressionsMenu cloneMenu, ISet<string> observedRockIt)
    {
        var animator = avatar.GetComponent<Animator>();
        if (!animator) throw new Exception("Native avatar toggle probe requires the supplied avatar Animator.");
        animator.runtimeAnimatorController = fx;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;
        animator.Rebind();
        animator.Update(0f);
        foreach (var parameter in fx.parameters.Where(parameter => parameter.type == AnimatorControllerParameterType.Bool &&
            (parameter.name == "IsLocal" || parameter.name.StartsWith("nxclone_visible", StringComparison.Ordinal) ||
             parameter.name.StartsWith("nxclone_enabled_", StringComparison.Ordinal))))
            animator.SetBool(parameter.name, true);
        Sample(animator);

        var probes = new List<(string name, bool requireMain)> {
            ("Chains", true), ("Warmers", true), ("Fem", true), ("TailDown", true),
            ("TailThin", false), ("Wet Effect On", true), ("Shoot", true),
            ("Gold Stream", true), ("Drip", true)
        };
        var rockIt = FindControl(cloneMenu, control => Normalize(control.name) == "rockit" && control.subMenu);
        if (rockIt != null)
        {
            var addedNames = new HashSet<string>(probes.Select(probe => Normalize(probe.name)), StringComparer.Ordinal);
            foreach (var control in Menus(rockIt.subMenu).SelectMany(menu => menu.controls ?? new List<VRCExpressionsMenu.Control>())
                         .Where(control => control != null && control.type == VRCExpressionsMenu.Control.ControlType.Toggle &&
                             control.parameter != null && !string.IsNullOrEmpty(control.parameter.name)))
                if (addedNames.Add(Normalize(control.name)))
                    probes.Add((control.name, FindToggle(descriptor.expressionsMenu, control.name, false) != null));
        }
        var failures = new List<string>();
        foreach (var probe in probes)
        {
            try
            {
            var cloneControl = FindToggle(cloneMenu, probe.name, true);
            var mainControl = FindToggle(descriptor.expressionsMenu, probe.name, false);
            if (cloneControl == null)
                throw new Exception("Native toggle probe could not find clone menu toggle '" + probe.name + "'.");
            if (mainControl == null && probe.requireMain)
                throw new Exception("Native toggle probe could not find main menu toggle '" + probe.name + "'.");

            string cloneParameter = ControlParameter(cloneControl);
            string mainParameter = ControlParameter(mainControl);
            bool coreProbe = new[] { "chains", "warmers", "fem", "taildown", "tailthin" }.Contains(Normalize(probe.name));
            if (!coreProbe && !observedRockIt.Contains(Normalize(probe.name)))
            {
                SetToggle(animator, fx, cloneControl, true);
                Sample(animator);
                LogReadback(animator, fx, probe.name, "inconclusive-source-baseline", mainControl, cloneControl);
                SetToggle(animator, fx, cloneControl, false);
                Sample(animator);
                Debug.Log("NXCLONE_NATIVE_TOGGLE_UNVERIFIED " + probe.name +
                    ": source-only avatar also has no observable native output; live SDK behavior remains untested.");
                continue;
            }
            var cloneBindings = ControlledBindings(fx, cloneParameter).ToArray();
            var mainBindings = string.IsNullOrEmpty(mainParameter) ? Array.Empty<EditorCurveBinding>() :
                ControlledBindings(fx, mainParameter).ToArray();
            Debug.Log("NXCLONE_NATIVE_TOGGLE_GRAPH " + probe.name + " main=" + DescribeParameterGraph(fx, mainParameter) +
                " clone=" + DescribeParameterGraph(fx, cloneParameter));
            if (cloneBindings.Length == 0 || (mainControl != null && mainBindings.Length == 0))
                throw new Exception("Native toggle '" + probe.name + "' has no FX animation bindings to observe: main=" +
                    mainBindings.Length + " clone=" + cloneBindings.Length);

            SetToggle(animator, fx, mainControl, false);
            SetToggle(animator, fx, cloneControl, false);
            Sample(animator);
            var mainOff = ReadBindings(avatar.transform, mainBindings);
            var cloneOff = ReadBindings(avatar.transform, cloneBindings);
            LogReadback(animator, fx, probe.name, "off/off", mainControl, cloneControl);

            SetToggle(animator, fx, mainControl, true);
            SetToggle(animator, fx, cloneControl, false);
            Sample(animator);
            var mainOn = ReadBindings(avatar.transform, mainBindings);
            var cloneStillOff = ReadBindings(avatar.transform, cloneBindings);
            LogReadback(animator, fx, probe.name, "main on/clone off", mainControl, cloneControl);
            if (mainControl != null) AssertBindingChange(probe.name, "main on", mainOff, mainOn, mainBindings);
            AssertBindingStable(probe.name, "clone while main on", cloneOff, cloneStillOff, cloneBindings);

            SetToggle(animator, fx, mainControl, false);
            SetToggle(animator, fx, cloneControl, true);
            Sample(animator);
            var mainStillOff = ReadBindings(avatar.transform, mainBindings);
            var cloneOn = ReadBindings(avatar.transform, cloneBindings);
            LogReadback(animator, fx, probe.name, "main off/clone on", mainControl, cloneControl);
            if (mainControl != null) AssertBindingStable(probe.name, "main while clone on", mainOff, mainStillOff, mainBindings);
            AssertBindingChange(probe.name, "clone on", cloneOff, cloneOn, cloneBindings);

            for (int cycle = 0; cycle < 2; cycle++)
            {
                SetToggle(animator, fx, mainControl, true);
                SetToggle(animator, fx, cloneControl, false);
                Sample(animator);
                if (mainControl != null) AssertBindingChange(probe.name, "main on cycle " + (cycle + 1), mainOff,
                    ReadBindings(avatar.transform, mainBindings), mainBindings);
                AssertBindingStable(probe.name, "clone off cycle " + (cycle + 1), cloneOff,
                    ReadBindings(avatar.transform, cloneBindings), cloneBindings);

                SetToggle(animator, fx, mainControl, false);
                SetToggle(animator, fx, cloneControl, true);
                Sample(animator);
                if (mainControl != null) AssertBindingStable(probe.name, "main off cycle " + (cycle + 1), mainOff,
                    ReadBindings(avatar.transform, mainBindings), mainBindings);
                AssertBindingChange(probe.name, "clone on cycle " + (cycle + 1), cloneOff,
                    ReadBindings(avatar.transform, cloneBindings), cloneBindings);
            }

            SetToggle(animator, fx, mainControl, false);
            SetToggle(animator, fx, cloneControl, false);
            Sample(animator);
            if (mainControl != null) AssertBindingStable(probe.name, "main off reset", mainOff,
                ReadBindings(avatar.transform, mainBindings), mainBindings);
            AssertBindingStable(probe.name, "clone off reset", cloneOff,
                ReadBindings(avatar.transform, cloneBindings), cloneBindings);

            Debug.Log("NXCLONE_NATIVE_TOGGLE " + probe.name + " main=" + (mainParameter ?? "absent") +
                " clone=" + cloneParameter + " mainBindings=" + mainBindings.Length + " cloneBindings=" + cloneBindings.Length +
                " mainCurveControlled=" + (!string.IsNullOrEmpty(mainParameter) && animator.IsParameterControlledByCurve(mainParameter)) +
                " cloneCurveControlled=" + animator.IsParameterControlledByCurve(cloneParameter));
            }
            catch (Exception error)
            {
                failures.Add(probe.name + ": " + error.Message);
                Debug.LogError("NXCLONE_NATIVE_TOGGLE_FAILED " + probe.name + ": " + error.Message);
            }
        }
        if (failures.Count > 0)
            throw new Exception("Native supplied-avatar toggle probe failures: " + string.Join(" || ", failures));
    }

    static VRCExpressionsMenu.Control FindToggle(VRCExpressionsMenu root, string name, bool clone)
    {
        string normalized = Normalize(name);
        return Menus(root).SelectMany(menu => menu.controls ?? new List<VRCExpressionsMenu.Control>())
            .Where(control => control != null &&
                (Normalize(control.name) == normalized ||
                    Normalize((ControlParameter(control) ?? "").Split('/').Last()).EndsWith(normalized, StringComparison.Ordinal)) &&
                (control.type == VRCExpressionsMenu.Control.ControlType.Toggle ||
                    control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet) &&
                !string.IsNullOrEmpty(ControlParameter(control)))
            .FirstOrDefault(control => ControlParameter(control).StartsWith("nxclone_clone1_", StringComparison.Ordinal) == clone);
    }

    static string ControlParameter(VRCExpressionsMenu.Control control) => control == null ? null :
        control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet
            ? control.subParameters?.FirstOrDefault()?.name : control.parameter?.name;

    static IEnumerable<EditorCurveBinding> ControlledBindings(AnimatorController fx, string parameter)
    {
        foreach (var layer in fx.layers)
        {
            var states = States(layer.stateMachine).ToArray();
            var targetClips = new HashSet<AnimationClip>();
            foreach (var state in states)
            {
                foreach (var transition in state.transitions)
                {
                    if (!transition.conditions.Any(condition => condition.parameter == parameter)) continue;
                    AddAllClips(state.motion, targetClips);
                    var stateTransition = transition as AnimatorStateTransition;
                    if (stateTransition && stateTransition.destinationState)
                        AddAllClips(stateTransition.destinationState.motion, targetClips);
                }
                AddTreeControlledClips(state.motion, parameter, targetClips);
            }
            foreach (var binding in targetClips.SelectMany(AnimationUtility.GetCurveBindings)
                         .Where(binding => binding.type != typeof(Animator)))
                yield return binding;
        }
    }

    static void AddTreeControlledClips(Motion motion, string parameter, HashSet<AnimationClip> clips,
        HashSet<BlendTree> visited = null)
    {
        if (!(motion is BlendTree tree)) return;
        visited = visited ?? new HashSet<BlendTree>();
        if (!visited.Add(tree)) return;
        var children = tree.children;
        bool direct = tree.blendType == BlendTreeType.Direct;
        bool treeUsesParameter = direct
            ? children.Any(child => child.directBlendParameter == parameter)
            : tree.blendParameter == parameter || tree.blendParameterY == parameter;
        foreach (var child in children)
        {
            bool childUsesParameter = direct && child.directBlendParameter == parameter;
            if (treeUsesParameter && (!direct || childUsesParameter)) AddAllClips(child.motion, clips);
            else if (child.motion is BlendTree) AddTreeControlledClips(child.motion, parameter, clips, visited);
        }
    }

    static void AddAllClips(Motion motion, HashSet<AnimationClip> clips,
        HashSet<BlendTree> visited = null)
    {
        if (motion is AnimationClip clip)
        {
            clips.Add(clip);
            return;
        }
        if (!(motion is BlendTree tree)) return;
        visited = visited ?? new HashSet<BlendTree>();
        if (!visited.Add(tree)) return;
        foreach (var child in tree.children) AddAllClips(child.motion, clips, visited);
    }

    static Dictionary<string, float> ReadBindings(Transform root, IEnumerable<EditorCurveBinding> bindings)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (TryReadBinding(root, binding, out var value))
                result[BindingKey(binding)] = value;
        }
        return result;
    }

    static bool TryReadBinding(Transform root, EditorCurveBinding binding, out float value)
    {
        if (binding.propertyName.StartsWith("material.", StringComparison.Ordinal) &&
            (binding.type == typeof(Renderer) || binding.type.IsSubclassOf(typeof(Renderer))))
        {
            var target = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
            var renderer = target ? target.GetComponent(binding.type) as Renderer : null;
            if (renderer)
            {
                string property = binding.propertyName.Substring("material.".Length);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                if (block.HasFloat(property))
                {
                    value = block.GetFloat(property);
                    return true;
                }
                var material = renderer.sharedMaterial;
                if (material && material.HasProperty(property))
                {
                    value = material.GetFloat(property);
                    return true;
                }
            }
        }
        if (AnimationUtility.GetFloatValue(root.gameObject, binding, out value)) return true;
        value = 0f;
        return false;
    }

    static void AssertBindingChange(string toggle, string phase,
        Dictionary<string, float> baseline, Dictionary<string, float> current, IEnumerable<EditorCurveBinding> bindings)
    {
        var keys = bindings.Select(BindingKey).Distinct(StringComparer.Ordinal);
        if (!keys.Any(key => baseline.TryGetValue(key, out var before) && current.TryGetValue(key, out var after) &&
                             !Mathf.Approximately(before, after)))
            throw new Exception("Native toggle '" + toggle + "' produced no observable target-binding change at " + phase + ".");
    }

    static void AssertBindingStable(string toggle, string phase,
        Dictionary<string, float> baseline, Dictionary<string, float> current, IEnumerable<EditorCurveBinding> bindings)
    {
        foreach (var key in bindings.Select(BindingKey).Distinct(StringComparer.Ordinal))
            if (baseline.TryGetValue(key, out var before) && current.TryGetValue(key, out var after) &&
                !Mathf.Approximately(before, after))
                throw new Exception("Native toggle '" + toggle + "' changed an independent target at " + phase + ": " + key);
    }

    static string BindingKey(EditorCurveBinding binding) =>
        binding.path + "|" + binding.type.FullName + "|" + binding.propertyName;

    static string Normalize(string value) => string.IsNullOrEmpty(value) ? string.Empty :
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    static void SetToggle(Animator animator, AnimatorController fx, VRCExpressionsMenu.Control control, bool enabled)
    {
        string parameter = ControlParameter(control);
        if (string.IsNullOrEmpty(parameter)) return;
        float value = control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet ? 1f : control.value;
        var type = fx.parameters.Single(item => item.name == parameter).type;
        switch (type)
        {
            case AnimatorControllerParameterType.Bool: animator.SetBool(parameter, enabled && value != 0f); break;
            case AnimatorControllerParameterType.Int: animator.SetInteger(parameter, enabled ? Mathf.RoundToInt(value) : 0); break;
            case AnimatorControllerParameterType.Float: animator.SetFloat(parameter, enabled ? value : 0f); break;
            default: throw new Exception("Unsupported native toggle parameter type for " + parameter + ": " + type);
        }
    }

    static void Sample(Animator animator)
    {
        for (int frame = 0; frame < 5; frame++) animator.Update(0.1f);
    }

    static HashSet<string> RunSourceRockItBaseline()
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        EditorSceneManager.OpenScene(ScenePath);
        var sourceDescriptor = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true)
            .Single(descriptor => descriptor.name == AvatarName);
        var sourceCopy = UnityEngine.Object.Instantiate(sourceDescriptor.gameObject);
        sourceCopy.name = "__nxclone supplied avatar source baseline";
        try
        {
            if (!VRCBuildPipelineCallbacks.OnPreprocessAvatar(sourceCopy))
                throw new Exception("VRChat SDK preprocessing rejected the source baseline copy.");
            var descriptor = sourceCopy.GetComponent<VRCAvatarDescriptor>();
            var fx = Fx(descriptor);
            var animator = sourceCopy.GetComponent<Animator>();
            if (!fx || !animator || !descriptor.expressionsMenu)
                throw new Exception("Source baseline requires the supplied avatar's finalized FX, Animator, and expressions menu.");
            var rockIt = FindControl(descriptor.expressionsMenu,
                control => Normalize(control.name) == "rockit" && control.subMenu);
            if (rockIt == null || !rockIt.subMenu)
                throw new Exception("Source baseline could not find the Rock-it submenu.");

            animator.runtimeAnimatorController = fx;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            foreach (var parameter in fx.parameters.Where(parameter => parameter.type == AnimatorControllerParameterType.Bool &&
                parameter.name == "IsLocal")) animator.SetBool(parameter.name, true);
            Sample(animator);

            var controls = Menus(rockIt.subMenu)
                .SelectMany(menu => menu.controls ?? new List<VRCExpressionsMenu.Control>())
                .Where(control => control != null &&
                    (control.type == VRCExpressionsMenu.Control.ControlType.Toggle ||
                     control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet) &&
                    !string.IsNullOrEmpty(ControlParameter(control)))
                .ToArray();
            if (controls.Length == 0) throw new Exception("Source Rock-it submenu has no parameter controls to probe.");

            foreach (var control in controls)
            {
                string parameter = ControlParameter(control);
                if (!fx.parameters.Any(item => item.name == parameter))
                {
                    Debug.LogError("NXCLONE_SOURCE_BASELINE " + control.name + " parameter=" + parameter + " absent-from-controller");
                    continue;
                }
                var bindings = ControlledBindings(fx, parameter).Distinct().ToArray();
                SetToggle(animator, fx, control, false);
                Sample(animator);
                var off = ReadBindings(sourceCopy.transform, bindings);
                var offValue = ParameterReadback(animator, fx, parameter);

                SetToggle(animator, fx, control, true);
                Sample(animator);
                var on = ReadBindings(sourceCopy.transform, bindings);
                var onValue = ParameterReadback(animator, fx, parameter);
                int changed = ChangedBindingCount(off, on);
                string layerWeights = LayerReadback(animator, fx, parameter);

                SetToggle(animator, fx, control, false);
                Sample(animator);
                var reset = ReadBindings(sourceCopy.transform, bindings);
                int resetDelta = ChangedBindingCount(off, reset);
                if (changed > 0 && resetDelta == 0) observed.Add(Normalize(control.name));
                string observation = changed > 0
                    ? "native-output-observed"
                    : "no-native-binding-change; SDK/VRCFury driver or gate may be required";
                Debug.Log("NXCLONE_SOURCE_ROCKIT " + control.name + " parameter=" + parameter +
                    " type=" + fx.parameters.Single(item => item.name == parameter).type +
                    " bindingCount=" + bindings.Length + " changedBindings=" + changed +
                    " resetDelta=" + resetDelta + " paramOff=" + offValue + " paramOn=" + onValue +
                    " curveControlled=" + animator.IsParameterControlledByCurve(parameter) +
                    " layerWeights=" + layerWeights + " observation=" + observation +
                    " graph=" + DescribeParameterGraph(fx, parameter));
            }
        }
        finally { if (sourceCopy) UnityEngine.Object.DestroyImmediate(sourceCopy); }
        return observed;
    }

    static void LogReadback(Animator animator, AnimatorController fx, string name, string phase,
        VRCExpressionsMenu.Control main, VRCExpressionsMenu.Control clone)
    {
        string mainParameter = ControlParameter(main);
        string cloneParameter = ControlParameter(clone);
        Debug.Log("NXCLONE_NATIVE_READBACK " + name + " phase=" + phase +
            " main=" + ParameterReadback(animator, fx, mainParameter) +
            " clone=" + ParameterReadback(animator, fx, cloneParameter) +
            " layers=" + LayerReadback(animator, fx, mainParameter) + " / " +
            LayerReadback(animator, fx, cloneParameter));
    }

    static string ParameterReadback(Animator animator, AnimatorController fx, string parameter)
    {
        if (string.IsNullOrEmpty(parameter)) return "absent";
        var definition = fx.parameters.FirstOrDefault(item => item.name == parameter);
        if (definition == null) return "missing";
        switch (definition.type)
        {
            case AnimatorControllerParameterType.Bool: return animator.GetBool(parameter).ToString();
            case AnimatorControllerParameterType.Int: return animator.GetInteger(parameter).ToString();
            case AnimatorControllerParameterType.Float: return animator.GetFloat(parameter).ToString("0.###");
            default: return definition.type.ToString();
        }
    }

    static string LayerReadback(Animator animator, AnimatorController fx, string parameter)
    {
        if (string.IsNullOrEmpty(parameter)) return "absent";
        var matching = new List<string>();
        var layers = fx.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            var states = States(layers[i].stateMachine).ToArray();
            bool transition = states.Any(state => state.transitions.Any(item =>
                item.conditions.Any(condition => condition.parameter == parameter)));
            bool blend = states.SelectMany(state => Trees(state.motion)).Any(tree =>
                tree.blendParameter == parameter || tree.blendParameterY == parameter ||
                tree.blendType == BlendTreeType.Direct && tree.children.Any(child => child.directBlendParameter == parameter));
            bool driver = states.SelectMany(state => state.behaviours ?? Array.Empty<StateMachineBehaviour>())
                .Any(behaviour => SerializedValuesMention(behaviour, parameter));
            if (transition || blend || driver)
                matching.Add(layers[i].name + "=" + animator.GetLayerWeight(i) +
                    (driver ? "{driver}" : string.Empty));
        }
        return matching.Count == 0 ? "none" : string.Join(",", matching);
    }

    static string DescribeParameterGraph(AnimatorController fx, string parameter)
    {
        if (string.IsNullOrEmpty(parameter)) return "absent";
        var matching = new List<string>();
        foreach (var layer in fx.layers)
        {
            var states = States(layer.stateMachine).ToArray();
            var transitionStates = states.Where(state => state.transitions.Any(item =>
                item.conditions.Any(condition => condition.parameter == parameter))).Select(state => state.name).ToArray();
            var trees = states.SelectMany(state => Trees(state.motion)).Where(tree =>
                tree.blendParameter == parameter || tree.blendParameterY == parameter ||
                tree.blendType == BlendTreeType.Direct && tree.children.Any(child => child.directBlendParameter == parameter))
                .Select(tree => tree.name + ":" + tree.blendType).Distinct().ToArray();
            var drivers = states.SelectMany(state => state.behaviours ?? Array.Empty<StateMachineBehaviour>())
                .Where(behaviour => SerializedValuesMention(behaviour, parameter))
                .Select(behaviour => behaviour.GetType().Name).Distinct().ToArray();
            if (transitionStates.Length > 0 || trees.Length > 0 || drivers.Length > 0)
                matching.Add(layer.name + "[transitions=" + string.Join("/", transitionStates) +
                    ";trees=" + string.Join("/", trees) + ";drivers=" + string.Join("/", drivers) + "]");
        }
        return matching.Count == 0 ? "no-transition/tree/state-driver-reference" : string.Join(";", matching);
    }

    static bool SerializedValuesMention(StateMachineBehaviour behaviour, string parameter)
    {
        if (!behaviour) return false;
        try
        {
            var serialized = new SerializedObject(behaviour);
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.String &&
                    property.stringValue == parameter) return true;
        }
        catch { }
        return false;
    }

    static int ChangedBindingCount(Dictionary<string, float> before, Dictionary<string, float> after) =>
        before.Count(pair => after.TryGetValue(pair.Key, out var value) && !Mathf.Approximately(pair.Value, value));

    static bool IsGoGoName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var plain = Regex.Replace(name, "<[^>]*>", string.Empty);
        var normalized = new string(plain.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized == "gogoloco" || normalized == "goloco" || normalized == "gogo";
    }

    static void Set(NxCloneWindow window, string name, object value) =>
        typeof(NxCloneWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
}
