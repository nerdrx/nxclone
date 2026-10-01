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

    static void RunSmoke()
    {
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

            Debug.Log($"NXCLONE_AVATAR_TOGGLES_OK finalSyncedBits={descriptor.expressionParameters.CalcTotalCost()} fxLayers={fx.layers.Length} fxParameters={fx.parameters.Length}: source toggles copied, baked SPS curves retained, GoGo Loco excluded.");
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
