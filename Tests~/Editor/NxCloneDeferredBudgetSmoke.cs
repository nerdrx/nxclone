using System;
using System.Linq;
using System.Reflection;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

public static class NxCloneDeferredBudgetSmoke
{
    public static void Run()
    {
        CheckBoundary();
        CheckVrcfuryCompressionPipeline();
        Debug.Log("NXCLONE_DEFERRED_BUDGET_SMOKE_OK");
    }

    static void CheckBoundary()
    {
        var root = new GameObject("nxclone deferred budget boundary test");
        var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        try
        {
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            descriptor.customExpressions = true;
            descriptor.expressionParameters = parameters;
            parameters.parameters = Parameters(256);
            root.AddComponent<NxCloneDeferredParameterBudget>();
            Assert(new NxCloneDeferredBudgetValidation().OnPreprocessAvatar(root), "exactly 256 synced bits should pass");
            Assert(!root.GetComponent<NxCloneDeferredParameterBudget>(), "successful validation must remove its marker");

            parameters.parameters = Parameters(257);
            root.AddComponent<NxCloneDeferredParameterBudget>();
            Assert(!new NxCloneDeferredBudgetValidation().OnPreprocessAvatar(root), "257 synced bits must stop upload");
            Assert(!root.GetComponent<NxCloneDeferredParameterBudget>(), "failed validation must remove its marker");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(parameters);
        }
    }

    static void CheckVrcfuryCompressionPipeline()
    {
        Assert(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("VF.Hooks.ParameterCompressorHook", false) != null),
            "VRCFury Parameter Compressor must be loaded in the integration project");
        EditorSceneManager.OpenScene("Assets/NX.unity");
        var source = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true).Single(x => x.name == "Nixomi cloned");
        var originalParameters = source.expressionParameters;
        bool originalCustomExpressions = source.customExpressions;
        var paddedParameters = originalParameters
            ? UnityEngine.Object.Instantiate(originalParameters)
            : ScriptableObject.CreateInstance<VRCExpressionParameters>();
        string generatedFolder = null;
        GameObject uploadCopy = null;
        NxCloneWindow window = null;
        const string compressorPreference = "com.vrcfury.parameterCompressor";
        int previousCompressorPreference = EditorPrefs.GetInt(compressorPreference, 0);
        try
        {
            var original = (paddedParameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                .Select(p => new VRCExpressionParameters.Parameter {
                    name = p.name, valueType = p.valueType, defaultValue = p.defaultValue,
                    saved = p.saved, networkSynced = false
                });
            var padding = Enumerable.Range(0, 234).Select(i => new VRCExpressionParameters.Parameter {
                name = "nxclone_budget_padding_" + i,
                valueType = VRCExpressionParameters.ValueType.Bool,
                networkSynced = true
            });
            paddedParameters.parameters = original.Concat(padding).ToArray();
            source.customExpressions = true;
            source.expressionParameters = paddedParameters;

            window = ScriptableObject.CreateInstance<NxCloneWindow>();
            Set(window, "avatar", source);
            Set(window, "worldDrop", true);
            Set(window, "poseFreeze", true);
            Set(window, "runtimeScale", true);
            Set(window, "deferParameterBudgetToVrcfury", true);
            typeof(NxCloneWindow).GetMethod("Generate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            var generated = Selection.activeGameObject;
            Assert(generated && generated.GetComponent<NxCloneSetup>(), "deferred clone setup should be generated");
            generatedFolder = generated.GetComponent<NxCloneSetup>().generatedFolder;
            uploadCopy = UnityEngine.Object.Instantiate(generated);
            NxCloneDeferredBudgetProbe.Target = uploadCopy;
            NxCloneDeferredBudgetProbe.Captured = false;
            EditorPrefs.SetInt(compressorPreference, 0);
            Assert(VRCBuildPipelineCallbacks.OnPreprocessAvatar(uploadCopy), "full SDK avatar callback pipeline should accept the compressed avatar");
            Assert(NxCloneDeferredBudgetProbe.Captured, "probe should run after nxclone generation and before VRCFury compression");
            Assert(NxCloneDeferredBudgetProbe.MarkerPresent, "deferred marker should reach the compressor stage");
            Assert(NxCloneDeferredBudgetProbe.Cost > VRCExpressionParameters.MAX_PARAMETER_COST,
                $"compressor stage should receive an over-budget avatar (got {NxCloneDeferredBudgetProbe.Cost})");
            var before = NxCloneDeferredBudgetProbe.NetworkedNxCloneParameters;
            Assert(before.Length > 0, "nxclone parameters should be synced at the compressor stage");
            var descriptor = uploadCopy.GetComponent<VRCAvatarDescriptor>();
            int finalCost = descriptor.expressionParameters.CalcTotalCost();
            var compressed = descriptor.expressionParameters.parameters
                .Where(p => before.Contains(p.name) && !p.networkSynced).Select(p => p.name).ToArray();
            Assert(finalCost <= VRCExpressionParameters.MAX_PARAMETER_COST,
                $"VRCFury should reduce final cost to the 256-bit limit (got {finalCost})");
            Assert(compressed.Length > 0, "VRCFury should unsync at least one generated nxclone parameter");
            var fx = descriptor.baseAnimationLayers.First(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            var compressorLayers = fx ? fx.layers.Where(layer => layer.name.IndexOf("Parameter Compressor", StringComparison.OrdinalIgnoreCase) >= 0).ToArray() : Array.Empty<AnimatorControllerLayer>();
            Debug.Log($"NXCLONE_VRCFURY_LAYERS: {string.Join(", ", fx ? fx.layers.Select(layer => layer.name) : Array.Empty<string>())}");
            Assert(compressorLayers.Length > 0,
                "VRCFury should add its compressor layer to the generated nxclone FX controller");
            Assert(!uploadCopy.GetComponent<NxCloneDeferredParameterBudget>(), "final budget gate should remove the upload marker");
            Debug.Log($"NXCLONE_VRCFURY_DEFERRED_BUDGET_OK: {before.Length} nxclone params, compressed {compressed.Length}, {finalCost}/256 bits, compressor layer present");
        }
        finally
        {
            NxCloneDeferredBudgetProbe.Target = null;
            EditorPrefs.SetInt(compressorPreference, previousCompressorPreference);
            source.customExpressions = originalCustomExpressions;
            source.expressionParameters = originalParameters;
            if (uploadCopy) UnityEngine.Object.DestroyImmediate(uploadCopy);
            if (window) UnityEngine.Object.DestroyImmediate(window);
            var generated = Selection.activeGameObject;
            if (generated && generated != source.gameObject && generated.GetComponent<NxCloneSetup>())
                UnityEngine.Object.DestroyImmediate(generated);
            if (!string.IsNullOrEmpty(generatedFolder)) AssetDatabase.DeleteAsset(generatedFolder);
            UnityEngine.Object.DestroyImmediate(paddedParameters);
            AssetDatabase.SaveAssets();
        }
    }

    static VRCExpressionParameters.Parameter[] Parameters(int count) => Enumerable.Range(0, count)
        .Select(i => new VRCExpressionParameters.Parameter {
            name = "budget_" + i,
            valueType = VRCExpressionParameters.ValueType.Bool,
            networkSynced = true
        }).ToArray();

    static void Set(NxCloneWindow window, string name, object value) =>
        typeof(NxCloneWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("nxclone deferred budget smoke: " + message);
    }
}

public sealed class NxCloneDeferredBudgetProbe : IVRCSDKPreprocessAvatarCallback
{
    public static GameObject Target;
    public static bool Captured;
    public static bool MarkerPresent;
    public static int Cost;
    public static string[] NetworkedNxCloneParameters = Array.Empty<string>();

    public int callbackOrder => int.MaxValue - 199;

    public bool OnPreprocessAvatar(GameObject avatar)
    {
        if (avatar != Target) return true;
        var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
        Cost = descriptor && descriptor.expressionParameters ? descriptor.expressionParameters.CalcTotalCost() : 0;
        NetworkedNxCloneParameters = descriptor && descriptor.expressionParameters
            ? descriptor.expressionParameters.parameters.Where(p => p.name.StartsWith("nxclone_", StringComparison.Ordinal) && p.networkSynced).Select(p => p.name).ToArray()
            : Array.Empty<string>();
        MarkerPresent = avatar.GetComponent<NxCloneDeferredParameterBudget>();
        Captured = true;
        return true;
    }
}
