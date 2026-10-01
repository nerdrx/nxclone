using System;
using System.Linq;
using System.Reflection;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;

public static class NxCloneToggleSmoke
{
    const string Folder = "Assets/nxclone-toggle-smoke";

    public static void Run()
    {
        try { RunTest(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void RunTest()
    {
        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-toggle-smoke");
        GameObject root = null;
        Avatar temporaryAvatar = null;
        NxCloneWindow window = null;
        Texture2D icon = null;
        try
        {
            var factory = typeof(NxClonePosingSmoke).GetMethod("CreateSyntheticHumanoid", BindingFlags.NonPublic | BindingFlags.Static);
            if (factory == null) throw new Exception("Synthetic humanoid factory missing.");
            var animator = (Animator)factory.Invoke(null, null);
            root = animator.gameObject;
            temporaryAvatar = animator.avatar;
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "BasicBody";
            body.transform.SetParent(root.transform, false);
            var clothes = new GameObject("Clothes");
            clothes.transform.SetParent(root.transform, false);
            GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(clothes.transform, false);
            var prop = new GameObject("Prop");
            prop.transform.SetParent(root.transform, false);
            var light = prop.AddComponent<Light>();
            light.intensity = 0;
            var effect = new GameObject("Effect");
            effect.transform.SetParent(root.transform, false);
            var particles = effect.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.GetComponent<ParticleSystemRenderer>().enabled = false;
            var maskedClothes = new GameObject("MaskedClothes");
            maskedClothes.transform.SetParent(root.transform, false);
            maskedClothes.SetActive(false);
            var goGoTest = new GameObject("GoGoTestObject");
            goGoTest.transform.SetParent(root.transform, false);
            var audioObject = new GameObject("Audio");
            audioObject.transform.SetParent(root.transform, false);
            audioObject.AddComponent<AudioSource>().playOnAwake = false;
            var contactReceiverObject = new GameObject("ContactReceiver");
            contactReceiverObject.transform.SetParent(root.transform, false);
            var sourceReceiver = contactReceiverObject.AddComponent<VRCContactReceiver>();
            SetSerializedString(new SerializedObject(sourceReceiver), "parameter", "effects");
            var contactSenderObject = new GameObject("ContactSender");
            contactSenderObject.transform.SetParent(root.transform, false);
            contactSenderObject.AddComponent<VRCContactSender>();

            var fx = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/source.controller");
            fx.AddParameter("clothing", AnimatorControllerParameterType.Bool);
            fx.AddParameter("effects", AnimatorControllerParameterType.Bool);
            fx.AddParameter("mixed", AnimatorControllerParameterType.Float);
            fx.AddParameter("curveValue", AnimatorControllerParameterType.Float);
            fx.AddParameter("gogo_walk", AnimatorControllerParameterType.Bool);
            fx.AddParameter("maskedClothing", AnimatorControllerParameterType.Bool);
            fx.AddParameter("curveProbe", AnimatorControllerParameterType.Bool);
            AddBoolLayer(fx, "Clothing", "clothing",
                ActiveClip("clothes-off", "Clothes", 0), ActiveClip("clothes-on", "Clothes", 1));
            AddBoolLayer(fx, "Effects", "effects",
                EffectClip("effects-off", 0, 0), EffectClip("effects-on", 4, 1));
            AddFloatLayer(fx, "Mixed expression", "mixed",
                ActiveClip("mixed-off", "Prop", 0), MixedPropClip());
            AddBoolLayer(fx, "Masked clothing", "maskedClothing",
                ActiveClip("masked-clothes-off", "MaskedClothes", 0), ActiveClip("masked-clothes-on", "MaskedClothes", 1));
            AddBoolLayer(fx, "Animator curve", "curveProbe",
                new AnimationClip { name = "curve-probe-off" }, AnimatorFloatClip("curve-probe-on", "curveValue", 0.75f));
            var sourceMask = new AvatarMask();
            sourceMask.AddTransformPath(maskedClothes.transform, false);
            sourceMask.SetTransformActive(0, true);
            AssetDatabase.AddObjectToAsset(sourceMask, fx);
            var maskedLayerIndex = Array.FindIndex(fx.layers, layer => layer.name == "Masked clothing");
            var maskedLayer = fx.layers[maskedLayerIndex];
            maskedLayer.avatarMask = sourceMask;
            var fxLayers = fx.layers;
            fxLayers[maskedLayerIndex] = maskedLayer;
            fx.layers = fxLayers;
            AddBoolLayer(fx, "[VF126] Go/Beyond", "gogo_walk",
                ActiveClip("gogo-off", "GoGoTestObject", 0), ActiveClip("gogo-on", "GoGoTestObject", 1));
            AddTrackingControlToEffects(fx);

            icon = new Texture2D(2, 2) { name = "nested menu icon" };
            AssetDatabase.CreateAsset(icon, Folder + "/icon.asset");
            var effectsMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            effectsMenu.name = "effects source menu";
            effectsMenu.controls.Add(new VRCExpressionsMenu.Control {
                name = "Effects", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "effects" }, value = 1, icon = icon
            });
            effectsMenu.controls.Add(new VRCExpressionsMenu.Control {
                name = "Mixed prop", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "mixed" }, value = 1, icon = icon
            });
            effectsMenu.controls.Add(new VRCExpressionsMenu.Control {
                name = "Animator curve", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "curveProbe" }, value = 1, icon = icon
            });
            AssetDatabase.CreateAsset(effectsMenu, Folder + "/effects-menu.asset");
            var sourceMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            sourceMenu.name = "source menu";
            var goGoMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            goGoMenu.name = "movement helpers";
            goGoMenu.controls.Add(new VRCExpressionsMenu.Control {
                name = "Walk", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "gogo_walk" }, value = 1
            });
            AssetDatabase.CreateAsset(goGoMenu, Folder + "/gogo-menu.asset");
            sourceMenu.controls.Add(new VRCExpressionsMenu.Control {
                name = "GoGo Loco", type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = goGoMenu
            });
            sourceMenu.controls.Add(new VRCExpressionsMenu.Control {
                name = "Clothes", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "clothing" }, value = 1
            });
            sourceMenu.controls.Add(new VRCExpressionsMenu.Control {
                name = "Masked clothing", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "maskedClothing" }, value = 1
            });
            sourceMenu.controls.Add(new VRCExpressionsMenu.Control {
                name = "Details", type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = effectsMenu, icon = icon
            });
            AssetDatabase.CreateAsset(sourceMenu, Folder + "/source-menu.asset");
            var sourceParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            sourceParameters.parameters = new[] {
                Expression("clothing", VRCExpressionParameters.ValueType.Bool),
                Expression("effects", VRCExpressionParameters.ValueType.Bool),
                // Deliberate mismatch: VRChat conversion metadata is Bool, source FX parameter is Float.
                Expression("mixed", VRCExpressionParameters.ValueType.Bool),
                Expression("curveValue", VRCExpressionParameters.ValueType.Float),
                Expression("gogo_walk", VRCExpressionParameters.ValueType.Bool),
                Expression("maskedClothing", VRCExpressionParameters.ValueType.Bool),
                Expression("curveProbe", VRCExpressionParameters.ValueType.Bool)
            };
            AssetDatabase.CreateAsset(sourceParameters, Folder + "/source-parameters.asset");
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer {
                type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = fx,
                isDefault = false, isEnabled = true
            } };
            descriptor.customExpressions = true;
            descriptor.expressionParameters = sourceParameters;
            descriptor.expressionsMenu = sourceMenu;

            window = ScriptableObject.CreateInstance<NxCloneWindow>();
            Set(window, "avatar", descriptor);
            Set(window, "slots", new System.Collections.Generic.List<NxCloneSlot> { new NxCloneSlot() });
            Set(window, "copyFxAnimations", true);
            Set(window, "independentCloneFx", true);
            Set(window, "afterimages", false);
            Set(window, "recording", false);
            Set(window, "wear", false);
            Set(window, "worldDrop", false);
            var build = typeof(NxCloneWindow).GetMethod("BuildVisuals", BindingFlags.Instance | BindingFlags.NonPublic);
            if (build == null) throw new Exception("BuildVisuals method missing.");
            build.Invoke(window, new object[] { descriptor, Folder, true });
            AssetDatabase.SaveAssets();

            var clone = root.transform.Find("nxclone/world/placement-1/clone-1");
            Assert(clone, "clone visual was not generated");
            Assert(clone.Find("BasicBody") && clone.Find("Clothes") && clone.Find("Prop") && clone.Find("Effect"),
                "clone visual omitted body, clothing, prop Light, or ParticleSystem");
            var cloneClothes = clone.Find("Clothes").gameObject;
            var cloneProp = clone.Find("Prop").gameObject;
            var cloneLight = clone.Find("Prop").GetComponent<Light>();
            var cloneParticles = clone.Find("Effect").GetComponent<ParticleSystem>();
            var cloneParticleRenderer = clone.Find("Effect").GetComponent<ParticleSystemRenderer>();
            Assert(cloneLight && cloneParticles && cloneParticleRenderer, "native effect components were not preserved in clone hierarchy");
            Assert(clone.Find("Audio") && clone.Find("Audio").GetComponent<AudioSource>(),
                "AudioSource component was not retained in clone hierarchy");
            var cloneReceiver = clone.Find("ContactReceiver").GetComponent<VRCContactReceiver>();
            Assert(cloneReceiver && clone.Find("ContactSender").GetComponent<VRCContactSender>(),
                "VRC contact receiver or sender was not retained in clone hierarchy");

            var generatedFx = (AnimatorController)descriptor.baseAnimationLayers.Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            var generatedExpressions = descriptor.expressionParameters.parameters;
            string Alias(string sourceName) => generatedFx.parameters
                .SingleOrDefault(parameter => parameter.name.StartsWith("nxclone_clone1_", StringComparison.Ordinal) &&
                    parameter.name.EndsWith("_" + sourceName, StringComparison.Ordinal))?.name;
            string clothingAlias = Alias("clothing");
            string effectsAlias = Alias("effects");
            string mixedAlias = Alias("mixed");
            string curveValueAlias = Alias("curveValue");
            string maskedClothingAlias = Alias("maskedClothing");
            string curveProbeAlias = Alias("curveProbe");
            Assert(!string.IsNullOrEmpty(clothingAlias) && !string.IsNullOrEmpty(effectsAlias) && !string.IsNullOrEmpty(mixedAlias),
                "clone source FX parameters were not isolated under clone aliases");
            Assert(!string.IsNullOrEmpty(maskedClothingAlias), "masked clothing parameter was not isolated");
            Assert(generatedFx.parameters.Single(parameter => parameter.name == mixedAlias).type == AnimatorControllerParameterType.Float &&
                   generatedExpressions.Single(parameter => parameter.name == mixedAlias).valueType == VRCExpressionParameters.ValueType.Bool,
                "mismatched Float controller and Bool expression metadata were not both preserved");
            var clonedClothingClip = (AnimationClip)generatedFx.layers.Single(layer => layer.name == "nxclone 1 Clothing")
                .stateMachine.states.Single(entry => entry.state.name == "On").state.motion;
            var clonedEffectClip = (AnimationClip)generatedFx.layers.Single(layer => layer.name == "nxclone 1 Effects")
                .stateMachine.states.Single(entry => entry.state.name == "On").state.motion;
            var clonedCurveClip = (AnimationClip)generatedFx.layers.Single(layer => layer.name == "nxclone 1 Animator curve")
                .stateMachine.states.Single(entry => entry.state.name == "On").state.motion;
            var clonedMixedClip = (AnimationClip)generatedFx.layers.Single(layer => layer.name == "nxclone 1 Mixed expression")
                .stateMachine.states.Single(entry => entry.state.name == "On").state.motion;
            Assert(AnimationUtility.GetCurveBindings(clonedClothingClip).Single().path ==
                   "nxclone/world/placement-1/clone-1/Clothes" &&
                   AnimationUtility.GetCurveBindings(clonedEffectClip).Any(binding => binding.path == "nxclone/world/placement-1/clone-1/Prop" && binding.type == typeof(Light)) &&
                   AnimationUtility.GetCurveBindings(clonedEffectClip).Any(binding => binding.path == "nxclone/world/placement-1/clone-1/Effect" && binding.type == typeof(ParticleSystemRenderer)),
                "source FX animation paths were not retargeted: clothing=" + string.Join(",", AnimationUtility.GetCurveBindings(clonedClothingClip).Select(binding => binding.path + ":" + binding.type.Name + ":" + binding.propertyName)) +
                " effects=" + string.Join(",", AnimationUtility.GetCurveBindings(clonedEffectClip).Select(binding => binding.path + ":" + binding.type.Name + ":" + binding.propertyName)));
            var animatorParameterCurve = AnimationUtility.GetCurveBindings(clonedCurveClip).Single(binding => binding.type == typeof(Animator));
            Assert(animatorParameterCurve.path == "" && animatorParameterCurve.propertyName == curveValueAlias,
                "Animator parameter curve was not remapped to clone-specific Float alias");
            Assert(AnimationUtility.GetCurveBindings(clonedMixedClip).Any(binding => binding.path == "__vrcf_length" &&
                    binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive"),
                "VRCFury clip-duration dummy track was not preserved");
            var clonedMaskedLayer = generatedFx.layers.Single(layer => layer.name == "nxclone 1 Masked clothing");
            Assert(clonedMaskedLayer.avatarMask && Enumerable.Range(0, clonedMaskedLayer.avatarMask.transformCount)
                    .Select(clonedMaskedLayer.avatarMask.GetTransformPath)
                    .Contains("nxclone/world/placement-1/clone-1/MaskedClothes"),
                "source AvatarMask transform path was not retargeted to the cloned clothing");
            var excludedGoLayer = generatedFx.layers.Single(layer => layer.name == "nxclone 1 [VF126] Go/Beyond");
            Assert(excludedGoLayer.stateMachine.name == "nxclone excluded GoGo Loco" &&
                   excludedGoLayer.defaultWeight == 0f && excludedGoLayer.stateMachine.states.Length == 0 &&
                   excludedGoLayer.stateMachine.stateMachines.Length == 0,
                "excluded Go/Beyond layer did not preserve its index as an empty FX placeholder");
            Assert(!generatedFx.layers.Where(layer => layer.name.StartsWith("nxclone 1 ", StringComparison.Ordinal))
                    .SelectMany(layer => States(layer.stateMachine)).SelectMany(state => state.behaviours)
                    .Any(behaviour => behaviour && behaviour.GetType().Name == "VRCAnimatorTrackingControl"),
                "avatar-global tracking behavior was copied into clone FX");
            var clonedAudioBehaviour = generatedFx.layers.Single(layer => layer.name == "nxclone 1 Effects")
                .stateMachine.states.Single(entry => entry.state.name == "On").state.behaviours
                .Single(behaviour => behaviour && behaviour.GetType().Name == "VRCAnimatorPlayAudio");
            var clonedAudioData = new SerializedObject(clonedAudioBehaviour);
            Assert(SerializedString(clonedAudioData, "SourcePath") == "nxclone/world/placement-1/clone-1/Audio" &&
                   SerializedString(clonedAudioData, "ParameterName") == effectsAlias,
                "VRCAnimatorPlayAudio source path or isolated parameter alias was not remapped");
            Assert(SerializedString(new SerializedObject(cloneReceiver), "parameter") == effectsAlias,
                "cloned VRCContactReceiver output was not routed to the clone-specific alias");

            var nxcloneMenu = descriptor.expressionsMenu.controls.Single(control => control.name == "nxclone").subMenu;
            var cloneMenu = nxcloneMenu.controls.Single(control => control.name == "Clone 1").subMenu;
            Assert(cloneMenu.controls.Single(control => control.name == "Enabled").parameter.name.StartsWith("nxclone_enabled_", StringComparison.Ordinal),
                "clone menu lost its enable control");
            var expressionMenu = cloneMenu.controls.Single(control => control.name == "Avatar toggles").subMenu;
            Assert(expressionMenu.controls.Single(control => control.name == "Clothes").parameter.name == clothingAlias,
                "cloned clothing toggle menu did not point at its isolated parameter");
            Assert(!generatedExpressions.Any(parameter => parameter.name.StartsWith("nxclone_clone1_", StringComparison.Ordinal) &&
                    parameter.name.EndsWith("_gogo_walk", StringComparison.Ordinal)),
                "excluded GoGo Loco parameter was exported for the clone");
            var detailsMenu = expressionMenu.controls.Single(control => control.name == "Details");
            Assert(detailsMenu.type == VRCExpressionsMenu.Control.ControlType.SubMenu && detailsMenu.icon == icon,
                "nested submenu type or icon was not preserved");
            Assert(detailsMenu.subMenu.controls.Single(control => control.name == "Effects").parameter.name == effectsAlias &&
                   detailsMenu.subMenu.controls.Single(control => control.name == "Effects").icon == icon &&
                   detailsMenu.subMenu.controls.Single(control => control.name == "Mixed prop").parameter.name == mixedAlias &&
                   detailsMenu.subMenu.controls.Single(control => control.name == "Mixed prop").icon == icon &&
                   detailsMenu.subMenu.controls.Single(control => control.name == "Animator curve").parameter.name == curveProbeAlias,
                "nested toggle aliases or icons were not preserved");
            Assert(expressionMenu.controls.Single(control => control.name == "Masked clothing").parameter.name == maskedClothingAlias,
                "masked clothing control was lost from the copied expression menu");
            Assert(!MenuTree(expressionMenu).SelectMany(menu => menu.controls).Any(control =>
                    control.name.IndexOf("GoGo", StringComparison.OrdinalIgnoreCase) >= 0),
                "generated clone menu must exclude GoGo controls");

            var mainClothes = root.transform.Find("Clothes").gameObject;
            var mainProp = root.transform.Find("Prop").gameObject;
            animator.runtimeAnimatorController = generatedFx;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            foreach (string layerName in new[] { "Clothing", "Effects", "nxclone 1 Clothing", "nxclone 1 Effects" })
            {
                int layerIndex = Array.FindIndex(generatedFx.layers, layer => layer.name == layerName);
                Assert(layerIndex >= 0 && Mathf.Approximately(animator.GetLayerWeight(layerIndex), 1f),
                    "Animator source layer has unexpected weight: " + layerName);
            }
            string masterAlias = generatedFx.parameters.Single(parameter => parameter.name.StartsWith("nxclone_visible", StringComparison.Ordinal)).name;
            animator.SetBool(masterAlias, true);
            animator.SetBool("clothing", true);
            animator.SetBool(clothingAlias, false);
            animator.SetBool(effectsAlias, false);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(mainClothes.activeSelf && !cloneClothes.activeSelf,
                "primary clothing isolation mismatch: main=" + mainClothes.activeSelf + " clone=" + cloneClothes.activeSelf);
            animator.SetBool(clothingAlias, true);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(mainClothes.activeSelf && cloneClothes.activeSelf,
                "clone clothing toggle failed while primary clothing stayed on");
            animator.SetBool("clothing", false);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(!mainClothes.activeSelf && cloneClothes.activeSelf,
                "primary clothing toggle changed the independently enabled clone");
            animator.SetBool(masterAlias, false);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(!clone.gameObject.activeInHierarchy && !mainClothes.activeSelf,
                "master visibility toggle failed or changed primary clothing state");
            animator.SetBool(masterAlias, true);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(clone.gameObject.activeInHierarchy && cloneClothes.activeSelf,
                "master visibility re-enable did not restore the independently enabled clone");

            animator.SetBool("effects", true);
            animator.SetBool(effectsAlias, false);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(Mathf.Approximately(root.transform.Find("Prop").GetComponent<Light>().intensity, 4f) &&
                   Mathf.Approximately(cloneLight.intensity, 0f) &&
                   root.transform.Find("Effect").GetComponent<ParticleSystemRenderer>().enabled && !cloneParticleRenderer.enabled,
                "Light and ParticleSystem source effects did not remain independent");
            animator.SetBool(effectsAlias, true);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(Mathf.Approximately(cloneLight.intensity, 4f) && cloneParticleRenderer.enabled,
                "clone Light or ParticleSystem source FX toggle failed");
            animator.SetBool("effects", false);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(Mathf.Approximately(root.transform.Find("Prop").GetComponent<Light>().intensity, 0f) &&
                   Mathf.Approximately(cloneLight.intensity, 4f),
                "primary effects toggle changed the independent clone");

            // VRChat converts the Bool expression toggle to Float 0/1 for the source controller.
            animator.SetFloat("mixed", 1f);
            animator.SetFloat(mixedAlias, 1f);
            animator.Update(0.1f); animator.Update(0.1f);
            int mixedLayerIndex = Array.FindIndex(generatedFx.layers, layer => layer.name == "nxclone 1 Mixed expression");
            Assert(cloneProp.activeSelf, "converted Float 1 did not activate cloned mixed-type prop: value=" + animator.GetFloat(mixedAlias) +
                " state=" + animator.GetCurrentAnimatorStateInfo(mixedLayerIndex).shortNameHash +
                " main=" + mainProp.activeSelf);
            animator.SetFloat("mixed", 0f);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(!mainProp.activeSelf && cloneProp.activeSelf,
                "primary mixed-type toggle changed the enabled clone prop");
            animator.SetFloat(mixedAlias, 0f);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(!cloneProp.activeSelf && !mainProp.activeSelf,
                "converted Float 0 did not disable only the cloned mixed-type prop");
            animator.SetBool(maskedClothingAlias, true);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(!maskedClothes.activeSelf && clone.Find("MaskedClothes").gameObject.activeSelf,
                "retargeted AvatarMask did not drive only cloned masked clothing");
            animator.SetBool(maskedClothingAlias, false);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(!clone.Find("MaskedClothes").gameObject.activeSelf,
                "retargeted AvatarMask did not turn cloned clothing off");
            animator.SetBool(curveProbeAlias, true);
            animator.Update(0.1f); animator.Update(0.1f);
            Assert(Mathf.Abs(animator.GetFloat(curveValueAlias) - 0.75f) < 0.01f,
                "native Animator did not apply the remapped clone Float parameter curve");

            Debug.Log("NXCLONE_TOGGLE_SMOKE_OK: independent source toggles, component retention, menu aliases/icons, and mixed expression/controller types");
        }
        finally
        {
            if (window) UnityEngine.Object.DestroyImmediate(window);
            if (root) UnityEngine.Object.DestroyImmediate(root);
            if (temporaryAvatar) UnityEngine.Object.DestroyImmediate(temporaryAvatar);
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.SaveAssets();
        }
    }

    static AnimationClip ActiveClip(string name, string path, float value)
    {
        var clip = new AnimationClip { name = name };
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"),
            AnimationCurve.Constant(0, 1, value));
        return clip;
    }

    static AnimationClip AnimatorFloatClip(string name, string parameter, float value)
    {
        var clip = new AnimationClip { name = name };
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), parameter),
            AnimationCurve.Constant(0, 1, value));
        return clip;
    }

    static AnimationClip MixedPropClip()
    {
        var clip = ActiveClip("mixed-on", "Prop", 1);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("__vrcf_length", typeof(GameObject), "m_IsActive"),
            AnimationCurve.Constant(0, 1, 1));
        return clip;
    }

    static AnimationClip EffectClip(string name, float intensity, float enabled)
    {
        var clip = new AnimationClip { name = name };
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Prop", typeof(Light), "m_Intensity"),
            AnimationCurve.Constant(0, 1, intensity));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Effect", typeof(ParticleSystemRenderer), "m_Enabled"),
            AnimationCurve.Constant(0, 1, enabled));
        return clip;
    }

    static void AddBoolLayer(AnimatorController controller, string name, string parameter, AnimationClip offClip, AnimationClip onClip)
    {
        AddLayer(controller, name, parameter, offClip, onClip, AnimatorConditionMode.If, AnimatorConditionMode.IfNot);
    }

    static void AddFloatLayer(AnimatorController controller, string name, string parameter, AnimationClip offClip, AnimationClip onClip)
    {
        AddLayer(controller, name, parameter, offClip, onClip, AnimatorConditionMode.Greater, AnimatorConditionMode.Less);
    }

    static void AddLayer(AnimatorController controller, string name, string parameter, AnimationClip offClip,
        AnimationClip onClip, AnimatorConditionMode toOn, AnimatorConditionMode toOff)
    {
        AssetDatabase.AddObjectToAsset(offClip, controller);
        AssetDatabase.AddObjectToAsset(onClip, controller);
        var machine = new AnimatorStateMachine { name = name };
        AssetDatabase.AddObjectToAsset(machine, controller);
        var off = machine.AddState("Off"); off.motion = offClip;
        var on = machine.AddState("On"); on.motion = onClip;
        machine.defaultState = off;
        var enable = off.AddTransition(on); enable.hasExitTime = false; enable.duration = 0;
        enable.AddCondition(toOn, toOn == AnimatorConditionMode.Greater ? 0.5f : 0, parameter);
        var disable = on.AddTransition(off); disable.hasExitTime = false; disable.duration = 0;
        disable.AddCondition(toOff, toOff == AnimatorConditionMode.Less ? 0.5f : 0, parameter);
        controller.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1, stateMachine = machine });
    }

    static void AddTrackingControlToEffects(AnimatorController controller)
    {
        var type = TypeCache.GetTypesDerivedFrom<StateMachineBehaviour>()
            .SingleOrDefault(candidate => candidate.Name == "VRCAnimatorTrackingControl");
        if (type == null) throw new Exception("VRCAnimatorTrackingControl SDK behavior is unavailable.");
        var behavior = ScriptableObject.CreateInstance(type) as StateMachineBehaviour;
        if (!behavior) throw new Exception("Could not create VRCAnimatorTrackingControl behavior.");
        AssetDatabase.AddObjectToAsset(behavior, controller);
        var audioType = TypeCache.GetTypesDerivedFrom<StateMachineBehaviour>()
            .SingleOrDefault(candidate => candidate.Name == "VRCAnimatorPlayAudio");
        if (audioType == null) throw new Exception("VRCAnimatorPlayAudio SDK behavior is unavailable.");
        var audio = ScriptableObject.CreateInstance(audioType) as StateMachineBehaviour;
        if (!audio) throw new Exception("Could not create VRCAnimatorPlayAudio behavior.");
        var audioData = new SerializedObject(audio);
        SetSerializedString(audioData, "SourcePath", "Audio");
        SetSerializedString(audioData, "ParameterName", "effects");
        AssetDatabase.AddObjectToAsset(audio, controller);
        var layers = controller.layers;
        var effects = layers.Single(layer => layer.name == "Effects");
        var on = effects.stateMachine.states.Single(entry => entry.state.name == "On").state;
        on.behaviours = new[] { behavior, audio };
    }

    static void SetSerializedString(SerializedObject serialized, string propertyName, string value)
    {
        var property = FindSerializedProperty(serialized, propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.String)
            throw new Exception("Missing serialized string field: " + propertyName);
        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static string SerializedString(SerializedObject serialized, string propertyName)
    {
        var property = FindSerializedProperty(serialized, propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.String)
            throw new Exception("Missing serialized string field: " + propertyName);
        return property.stringValue;
    }

    static SerializedProperty FindSerializedProperty(SerializedObject serialized, string propertyName)
    {
        var property = serialized.GetIterator();
        while (property.Next(true))
            if (property.name == propertyName) return property.Copy();
        return null;
    }

    static System.Collections.Generic.IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        foreach (var child in machine.states) yield return child.state;
        foreach (var nested in machine.stateMachines)
            foreach (var state in States(nested.stateMachine)) yield return state;
    }

    static VRCExpressionParameters.Parameter Expression(string name, VRCExpressionParameters.ValueType type) => new VRCExpressionParameters.Parameter {
        name = name, valueType = type, networkSynced = true, saved = false
    };

    static System.Collections.Generic.IEnumerable<VRCExpressionsMenu> MenuTree(VRCExpressionsMenu root)
    {
        var seen = new System.Collections.Generic.HashSet<int>();
        var pending = new System.Collections.Generic.Stack<VRCExpressionsMenu>();
        if (root) pending.Push(root);
        while (pending.Count > 0)
        {
            var menu = pending.Pop();
            if (!menu || !seen.Add(menu.GetInstanceID())) continue;
            yield return menu;
            foreach (var child in menu.controls.Where(control => control.subMenu).Select(control => control.subMenu)) pending.Push(child);
        }
    }

    static void Set(NxCloneWindow window, string name, object value) =>
        typeof(NxCloneWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("nxclone clone-toggle smoke: " + message);
    }
}
