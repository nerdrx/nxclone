using System;
using System.Linq;
using System.Reflection;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDK3.Avatars.ScriptableObjects;

public static class NxCloneSourceFxSmoke
{
    public static void Run()
    {
        const string folder = "Assets/nxclone-source-fx-smoke";
        AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-source-fx-smoke");
        var sourceRoot = new GameObject("source-avatar");
        var targetRoot = new GameObject("root-avatar");
        try
        {
            var sourceFace = NewRenderer(sourceRoot.transform, "Face");
            var group = new GameObject("nxclone");
            group.transform.SetParent(targetRoot.transform, false);
            var cloneRoot = new GameObject("clone-1");
            cloneRoot.transform.SetParent(group.transform, false);
            NewRenderer(cloneRoot.transform, "Face");

            var sharedClip = new AnimationClip { name = "Shared source FX" };
            AnimationUtility.SetEditorCurve(sharedClip,
                EditorCurveBinding.FloatCurve("Face", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
            AnimationUtility.SetEditorCurve(sharedClip,
                EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.Smile"), AnimationCurve.Constant(0, 1, 100));
            AssetDatabase.CreateAsset(sharedClip, folder + "/shared.anim");

            var sourceFx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/source.controller");
            sourceFx.AddParameter("sourceToggle", AnimatorControllerParameterType.Bool);
            sourceFx.AddParameter(new AnimatorControllerParameter { name = "playbackSpeed", type = AnimatorControllerParameterType.Float });
            sourceFx.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
            var sourceMachine = sourceFx.layers[0].stateMachine;
            var off = sourceMachine.AddState("Off");
            off.writeDefaultValues = false;
            var on = sourceMachine.AddState("On");
            on.writeDefaultValues = true;
            var blended = sourceMachine.AddState("Blended");
            off.motion = sharedClip;
            on.motion = sharedClip;
            sourceMachine.defaultState = off;
            var transition = off.AddTransition(on);
            transition.hasExitTime = false;
            transition.AddCondition(AnimatorConditionMode.If, 0, "sourceToggle");
            transition.AddCondition(AnimatorConditionMode.Greater, 1, "GestureLeft");
            var anyTransition = sourceMachine.AddAnyStateTransition(on);
            anyTransition.hasExitTime = false;
            anyTransition.AddCondition(AnimatorConditionMode.If, 0, "sourceToggle");
            var nestedMachine = sourceMachine.AddStateMachine("Nested");
            var nestedState = nestedMachine.AddState("Nested On");
            var entryTransition = nestedMachine.AddEntryTransition(nestedState);
            entryTransition.AddCondition(AnimatorConditionMode.If, 0, "sourceToggle");
            var nestedAnyTransition = nestedMachine.AddAnyStateTransition(nestedState);
            nestedAnyTransition.hasExitTime = false;
            nestedAnyTransition.AddCondition(AnimatorConditionMode.If, 0, "sourceToggle");
            var machineTransition = sourceMachine.AddStateMachineTransition(nestedMachine, blended);
            machineTransition.AddCondition(AnimatorConditionMode.Greater, 0.2f, "playbackSpeed");

            var blendTree = new BlendTree { name = "Source speed tree", blendType = BlendTreeType.Simple1D, blendParameter = "playbackSpeed" };
            AssetDatabase.AddObjectToAsset(blendTree, sourceFx);
            blendTree.children = new[] { new ChildMotion { motion = sharedClip, threshold = 0 } };
            blended.motion = blendTree;

            var driverType = TypeCache.GetTypesDerivedFrom<StateMachineBehaviour>()
                .FirstOrDefault(type => !type.IsAbstract && type.Name.Contains("ParameterDriver", StringComparison.Ordinal));
            if (driverType == null) throw new Exception("VRChat Avatar Parameter Driver type is unavailable in this SDK.");
            var driver = ScriptableObject.CreateInstance(driverType) as StateMachineBehaviour;
            if (!driver) throw new Exception("Could not create VRChat Avatar Parameter Driver.");
            AssetDatabase.AddObjectToAsset(driver, sourceFx);
            var driverData = new SerializedObject(driver);
            var driverParameters = driverData.FindProperty("parameters");
            if (driverParameters == null || !driverParameters.isArray)
                throw new Exception("Avatar Parameter Driver has no serialized parameters array.");
            driverParameters.arraySize = 1;
            var driverParameterName = driverParameters.GetArrayElementAtIndex(0).FindPropertyRelative("name");
            if (driverParameterName == null) throw new Exception("Avatar Parameter Driver parameter has no name field.");
            driverParameterName.stringValue = "sourceToggle";
            driverData.ApplyModifiedPropertiesWithoutUndo();
            var sourceLayerControl = blended.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            sourceLayerControl.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            sourceLayerControl.layer = 0;
            on.behaviours = new[] { driver };

            var syncedLayer = new AnimatorControllerLayer { name = "Synced FX", syncedLayerIndex = 0, syncedLayerAffectsTiming = true };
            sourceFx.AddLayer(syncedLayer);
            AssetDatabase.SaveAssets();
            sourceFx = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/source.controller");
            sourceMachine = sourceFx.layers[0].stateMachine;
            off = sourceMachine.states.Single(s => s.state.name == "Off").state;
            on = sourceMachine.states.Single(s => s.state.name == "On").state;
            blended = sourceMachine.states.Single(s => s.state.name == "Blended").state;
            sharedClip = off.motion as AnimationClip;
            int originalLayerCount = sourceFx.layers.Length;
            int originalParameterCount = sourceFx.parameters.Length;

            var sourceParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            sourceParameters.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "sourceToggle", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "playbackSpeed", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true }
            };
            AssetDatabase.CreateAsset(sourceParameters, folder + "/source-parameters.asset");

            var childMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            childMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Playback speed", type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = "playbackSpeed" } }
            });
            AssetDatabase.CreateAsset(childMenu, folder + "/child-menu.asset");
            var sourceMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            sourceMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Toggle", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "sourceToggle" }, value = 1
            });
            sourceMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Settings", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = childMenu
            });
            AssetDatabase.CreateAsset(sourceMenu, folder + "/source-menu.asset");

            var rootParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            rootParameters.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
            AssetDatabase.CreateAsset(rootParameters, folder + "/root-parameters.asset");
            var targetFx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/root.controller");
            int rootLayerCount = targetFx.layers.Length;
            var result = NxCloneSourceFx.Merge(targetFx, sourceFx, sourceRoot.transform,
                targetRoot.transform, cloneRoot.transform, 1, sourceParameters, sourceMenu, rootParameters, folder);

            var map = result.parameterNames;
            Assert(map.ContainsKey("sourceToggle") && map.ContainsKey("playbackSpeed"), "Custom parameter map missing.");
            Assert(result.stateLockParameterName == map["nxclone_fx_state_lock"], "Per-clone FX transition-lock parameter was not namespaced.");
            Assert(!targetFx.parameters.Any(p => p.name == "GestureLeft"), "VRChat built-in parameter copied as custom.");
            Assert(targetFx.layers.Length == rootLayerCount + sourceFx.layers.Length, "Source layers were not merged.");
            Assert(targetFx.layers[rootLayerCount + 1].syncedLayerIndex == rootLayerCount, "Synced layer index was not rebased.");
            Assert(Mathf.Approximately(targetFx.layers[rootLayerCount].defaultWeight, 1f),
                "Copied source controller base layer must become full weight when appended to root FX.");

            var mergedMachine = targetFx.layers[rootLayerCount].stateMachine;
            var mergedStates = mergedMachine.states.ToDictionary(s => s.state.name, s => s.state);
            Assert(!mergedStates["Off"].writeDefaultValues && mergedStates["On"].writeDefaultValues, "Write Defaults values changed.");
            Assert(mergedStates["Off"].motion == mergedStates["On"].motion, "Shared clip reference was not preserved.");
            var mergedClip = mergedStates["Off"].motion as AnimationClip;
            Assert(mergedClip && mergedClip != sharedClip, "Source clip was not copied.");
            var activeBinding = AnimationUtility.GetCurveBindings(mergedClip).Single(b => b.type == typeof(GameObject));
            var shapeBinding = AnimationUtility.GetCurveBindings(mergedClip).Single(b => b.type == typeof(SkinnedMeshRenderer));
            Assert(activeBinding.path == "nxclone/clone-1/Face", "GameObject curve path not retargeted.");
            Assert(shapeBinding.path == "nxclone/clone-1/Face", "Blendshape curve path not retargeted.");
            var conditions = mergedStates["Off"].transitions.Single().conditions;
            Assert(conditions.Any(c => c.parameter == map["sourceToggle"]), "Transition custom parameter not namespaced.");
            Assert(conditions.Any(c => c.parameter == "GestureLeft"), "Transition built-in parameter was changed.");
            var mergedTree = mergedStates["Blended"].motion as BlendTree;
            Assert(mergedTree && mergedTree.blendParameter == map["playbackSpeed"], "BlendTree parameter not namespaced.");
            Assert(mergedTree.children[0].motion == mergedClip, "BlendTree did not reuse shared copied clip.");
            Assert(result.menu.controls[0].parameter.name == map["sourceToggle"], "Root menu parameter not namespaced.");
            Assert(result.menu.controls[1].subMenu.controls[0].subParameters[0].name == map["playbackSpeed"], "Nested menu parameter not namespaced.");
            Assert(result.syncedParameterCost == 10, "Synced parameter cost must include the one-bit FX lock.");
            var lockExpression = result.parameters.Single(parameter => parameter.name == result.stateLockParameterName);
            Assert(lockExpression.valueType == VRCExpressionParameters.ValueType.Bool && lockExpression.networkSynced &&
                   !lockExpression.saved && lockExpression.defaultValue == 0f,
                "The FX lock must be a one-bit, reset-off synced expression parameter.");
            var lockControl = result.menu.controls.Single(control => control.parameter != null &&
                control.parameter.name == result.stateLockParameterName);
            Assert(lockControl.type == VRCExpressionsMenu.Control.ControlType.Toggle && lockControl.value == 1,
                "Copied expression menu omitted the transition-lock toggle.");
            Assert(targetFx.parameters.Any(parameter => parameter.name == result.stateLockParameterName &&
                       parameter.type == AnimatorControllerParameterType.Bool && !parameter.defaultBool),
                "Root FX controller lacks a default-off lock bool.");
            Assert(Locked(mergedStates["Off"].transitions.Single(), result.stateLockParameterName) &&
                   Locked(mergedMachine.anyStateTransitions.Single(), result.stateLockParameterName),
                "State and Any State transitions were not gated by the lock.");
            var mergedNested = mergedMachine.stateMachines.Single(child => child.stateMachine.name == "Nested").stateMachine;
            Assert(Locked(mergedNested.entryTransitions.Single(), result.stateLockParameterName) &&
                   Locked(mergedNested.anyStateTransitions.Single(), result.stateLockParameterName),
                "Nested entry or Any State transitions were not gated by the lock.");
            Assert(Locked(mergedMachine.GetStateMachineTransitions(mergedNested).Single(), result.stateLockParameterName),
                "State-machine transition was not gated by the lock.");
            Assert(mergedStates["Off"].motion && mergedStates["On"].motion,
                "State lock removed current-state motions instead of only suppressing transitions.");
            Assert(!sourceMachine.anyStateTransitions.Single().conditions.Any(condition => condition.parameter == result.stateLockParameterName) &&
                   !sourceMachine.states.Single(state => state.state.name == "Off").state.transitions.Single().conditions
                       .Any(condition => condition.parameter == result.stateLockParameterName),
                "Lock gating mutated source controller transitions.");

            // Unity does not declare VRChat built-ins in the source merge; add this synthetic
            // built-in to emulate the parameter supplied by VRChat when running the merged FX.
            targetFx.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
            var animator = targetRoot.AddComponent<Animator>();
            animator.runtimeAnimatorController = targetFx;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            Assert(Mathf.Approximately(animator.GetLayerWeight(rootLayerCount), 1f),
                "Native Animator source FX base layer did not start at full weight without a manual override.");
            cloneRoot.transform.Find("Face").gameObject.SetActive(false);
            animator.SetInteger("GestureLeft", 2);
            animator.SetBool(map["sourceToggle"], true);
            animator.SetBool(result.stateLockParameterName, true);
            animator.Update(0.1f);
            animator.Update(0.1f);
            var sourceLayerIndex = rootLayerCount;
            Assert(cloneRoot.transform.Find("Face").gameObject.activeSelf,
                "Cloned source facial clip did not play at its default appended-layer weight.");
            Assert(animator.GetCurrentAnimatorStateInfo(sourceLayerIndex).IsName("Off"),
                "Runtime changed source FX state while its lock toggle was on.");
            float lockedTime = animator.GetCurrentAnimatorStateInfo(sourceLayerIndex).normalizedTime;
            animator.Update(0.2f);
            float nextLockedTime = animator.GetCurrentAnimatorStateInfo(sourceLayerIndex).normalizedTime;
            var currentClips = animator.GetCurrentAnimatorClipInfo(sourceLayerIndex);
            Assert(currentClips.Length == 1 && currentClips[0].clip == mergedClip && nextLockedTime > lockedTime,
                "Current source FX clip stopped advancing while transitions were locked.");
            animator.SetBool(result.stateLockParameterName, false);
            bool transitionedAfterUnlock = false;
            for (int step = 0; step < 8; step++)
            {
                animator.Update(0.1f);
                if (animator.GetCurrentAnimatorStateInfo(sourceLayerIndex).IsName("On"))
                {
                    transitionedAfterUnlock = true;
                    break;
                }
            }
            Assert(transitionedAfterUnlock,
                "Runtime did not transition after unlocking with its input parameter still active.");
            var wornMask = new GameObject("__nxclone worn mask").transform;
            wornMask.SetParent(targetRoot.transform, false);
            NewRenderer(wornMask, "Face");
            int maskCopied = NxCloneFxMirror.MirrorFxCurves(targetFx, targetRoot.transform, new[] { wornMask }, folder,
                silhouetteOnly: true, sourceVisual: cloneRoot.transform);
            Assert(maskCopied > 0, "Clone-rooted FX curves did not copy to the avatar-root afterimage mask.");
            var maskClip = mergedStates["Off"].motion as AnimationClip;
            Assert(maskClip && maskClip != mergedClip && AnimationUtility.GetCurveBindings(maskClip).Any(binding =>
                binding.path == "__nxclone worn mask/Face" && binding.propertyName == "blendShape.Smile"),
                "Worn mask did not receive the clone's blendshape FX binding.");
            Assert(AnimationUtility.GetCurveBindings(sharedClip).All(binding => binding.path == "Face"),
                "Mirroring clone FX onto the worn mask modified the original source clip.");
            Assert(sourceFx.layers.Length == originalLayerCount && sourceFx.parameters.Length == originalParameterCount,
                "Source controller structure changed.");
            Assert(AnimationUtility.GetCurveBindings(sharedClip).All(b => b.path == "Face"), "Source clip bindings changed.");
            var sourceDriver = sourceFx.layers[0].stateMachine.states.Single(s => s.state.name == "On").state.behaviours.Single();
            Assert(ReadDriverParameter(sourceDriver) == "sourceToggle", "Source Avatar Parameter Driver changed.");
            var mergedDriver = mergedStates["On"].behaviours.Single(behaviour => behaviour.GetType().Name.Contains("ParameterDriver", StringComparison.Ordinal));
            Assert(ReadDriverParameter(mergedDriver) == map["sourceToggle"], "Clone Avatar Parameter Driver was not namespaced.");
            var mergedLayerControl = mergedStates["Blended"].behaviours.OfType<VRCAnimatorLayerControl>().Single();
            Assert(mergedLayerControl.playable == VRC_AnimatorLayerControl.BlendableLayer.FX &&
                   mergedLayerControl.layer == rootLayerCount,
                "Copied source FX layer control did not retain its playable and shift its target layer index.");

            // VRCFury can leave layer state machines in a separate controller asset and
            // reference them from the baked FX controller. CopyAsset may preserve those
            // external references, so merge must not rewrite the imported source asset.
            var externalOwner = AnimatorController.CreateAnimatorControllerAtPath(folder + "/external-owner.controller");
            externalOwner.AddParameter("externalToggle", AnimatorControllerParameterType.Bool);
            var externalMachine = externalOwner.layers[0].stateMachine;
            externalMachine.name = "Externally owned machine";
            var externalOff = externalMachine.AddState("External Off");
            var externalOn = externalMachine.AddState("External On");
            var externalTransition = externalOff.AddTransition(externalOn);
            externalTransition.hasExitTime = false;
            externalTransition.AddCondition(AnimatorConditionMode.If, 0, "externalToggle");
            var externalSource = AnimatorController.CreateAnimatorControllerAtPath(folder + "/external-source.controller");
            externalSource.AddParameter("externalToggle", AnimatorControllerParameterType.Bool);
            var externalLayers = externalSource.layers;
            externalLayers[0].stateMachine = externalMachine;
            externalSource.layers = externalLayers;
            EditorUtility.SetDirty(externalSource);
            AssetDatabase.SaveAssets();
            externalSource = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/external-source.controller");
            externalOwner = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/external-owner.controller");
            externalMachine = externalOwner.layers[0].stateMachine;
            externalOff = externalMachine.states.Single(state => state.state.name == "External Off").state;
            externalTransition = externalOff.transitions.Single();
            Assert(externalSource.layers[0].stateMachine == externalMachine,
                "Fixture did not preserve the external state-machine reference.");
            var externalTarget = AnimatorController.CreateAnimatorControllerAtPath(folder + "/external-target.controller");
            NxCloneSourceFx.Merge(externalTarget, externalSource, sourceRoot.transform,
                targetRoot.transform, cloneRoot.transform, 6, null, null, rootParameters, folder);
            Assert(externalOff.transitions.Length == 1 &&
                   externalTransition.conditions.Length == 1 &&
                   externalTransition.conditions[0].parameter == "externalToggle" &&
                   externalTransition.conditions.All(condition => condition.parameter != "nxclone_clone1_externalToggle" &&
                       !condition.parameter.StartsWith("nxclone_clone1_nxclone_fx_state_lock", StringComparison.Ordinal)),
                "Merging FX mutated a state machine owned by an external source controller asset.");

            var fullParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            fullParameters.parameters = Enumerable.Range(0, VRCExpressionParameters.MAX_PARAMETER_COST)
                .Select(i => new VRCExpressionParameters.Parameter { name = "f" + i, valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true }).ToArray();
            var blockedFx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/blocked.controller");
            int blockedLayers = blockedFx.layers.Length;
            bool refused = false;
            try
            {
                NxCloneSourceFx.Merge(blockedFx, sourceFx, sourceRoot.transform, targetRoot.transform,
                    cloneRoot.transform, 2, sourceParameters, sourceMenu, fullParameters, folder);
            }
            catch (InvalidOperationException e) when (e.Message.Contains("synced parameter bits", StringComparison.Ordinal)) { refused = true; }
            Assert(refused && blockedFx.layers.Length == blockedLayers, "256-bit expression limit was not rejected before mutation.");

            var deferredFx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/deferred.controller");
            var deferred = NxCloneSourceFx.Merge(deferredFx, sourceFx, sourceRoot.transform, targetRoot.transform,
                cloneRoot.transform, 2, sourceParameters, sourceMenu, fullParameters, folder,
                deferParameterBudgetCheck: true);
            Assert(deferred.syncedParameterCost > 0 && deferredFx.layers.Length > 0,
                "Explicit deferred-budget mode did not merge source FX beyond the current 256-bit cost.");

            var rootFace = NewRenderer(targetRoot.transform, "Face");
            var rootGestureMachine = new AnimatorStateMachine { name = "Root gesture" };
            AssetDatabase.AddObjectToAsset(rootGestureMachine, targetFx);
            var rootOff = rootGestureMachine.AddState("Root Off");
            var rootOn = rootGestureMachine.AddState("Root On");
            rootGestureMachine.defaultState = rootOff;
            var rootGestureTransition = rootOff.AddTransition(rootOn);
            rootGestureTransition.hasExitTime = false;
            rootGestureTransition.AddCondition(AnimatorConditionMode.Greater, 1, "GestureLeft");
            targetFx.AddLayer(new AnimatorControllerLayer { name = "Root gesture", stateMachine = rootGestureMachine });

            var rootSourceClip = new AnimationClip { name = "Root source gesture clip" };
            AnimationUtility.SetEditorCurve(rootSourceClip,
                EditorCurveBinding.FloatCurve("Face", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
            AssetDatabase.CreateAsset(rootSourceClip, folder + "/root-source.anim");
            var rootSourceFx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/root-source.controller");
            var rootSourceMachine = rootSourceFx.layers[0].stateMachine;
            var rootSourceOff = rootSourceMachine.AddState("Root source Off");
            var rootSourceOn = rootSourceMachine.AddState("Root source On");
            rootSourceOff.motion = rootSourceClip;
            rootSourceMachine.defaultState = rootSourceOff;
            var rootSourceTransition = rootSourceOff.AddTransition(rootSourceOn);
            rootSourceTransition.hasExitTime = false;
            rootSourceTransition.AddCondition(AnimatorConditionMode.Greater, 1, "GestureLeft");
            var isolated = NxCloneSourceFx.Merge(targetFx, rootSourceFx, targetRoot.transform,
                targetRoot.transform, cloneRoot.transform, 3, null, null, rootParameters, folder,
                isolateExpressionInputs: true);
            var gestureAlias = isolated.parameterNames["GestureLeft"];
            Assert(isolated.parameterTypes["GestureLeft"] == AnimatorControllerParameterType.Int &&
                   isolated.expressionInputMappings.Any(mapping => mapping.Input == "GestureLeft" &&
                       mapping.Target == gestureAlias && mapping.Type == AnimatorControllerParameterType.Int),
                "Isolated built-in input mapping omitted its alias name or Animator type.");
            Assert(isolated.expressionInputMappings.Length == 6 && isolated.syncedParameterCost == 1 &&
                   isolated.parameters.Length == 1,
                "FX-only built-in aliases added synced expression parameter cost.");
            Assert(targetFx.parameters.Any(parameter => parameter.name == gestureAlias && parameter.type == AnimatorControllerParameterType.Int) &&
                   targetFx.parameters.Count(parameter => parameter.name == "GestureLeft") == 1,
                "Isolated built-in alias was missing, mistyped, or overwrote the root built-in.");
            var isolatedMachine = targetFx.layers.Last().stateMachine;
            var isolatedOff = isolatedMachine.states.Single(entry => entry.state.name == "Root source Off").state;
            Assert(isolatedOff.transitions.Single().conditions.Any(condition => condition.parameter == gestureAlias),
                "Root-source clone transition did not use its isolated built-in alias.");
            var isolatedClip = isolatedOff.motion as AnimationClip;
            Assert(isolatedClip && AnimationUtility.GetCurveBindings(isolatedClip).Single().path == "nxclone/clone-1/Face",
                "Root-source clip did not remap onto the generated clone hierarchy.");

            targetFx.AddParameter("nxclone_recording_play", AnimatorControllerParameterType.Bool);
            NxCloneSourceFx.AddExpressionInputCopyLayer(targetFx, isolated, targetRoot.transform, folder,
                "nxclone_recording_play");
            var declaredInputParameters = targetFx.parameters.ToDictionary(parameter => parameter.name);
            Assert(isolated.expressionInputMappings.All(mapping => declaredInputParameters.TryGetValue(mapping.Input, out var input) && input.type == mapping.Type) &&
                   isolated.parameters.All(parameter => !isolated.expressionInputMappings.Any(mapping => mapping.Input == parameter.name)),
                "Live input Copy did not declare FX built-in inputs without exporting them as expression parameters.");
            var copyLayer = targetFx.layers.Last();
            var copyMachine = copyLayer.stateMachine;
            var copyState = copyMachine.states.Single(entry => entry.state.name == "copy live inputs").state;
            var copyDriver = copyState.behaviours.Single(behaviour => behaviour.GetType().Name.Contains("ParameterDriver", StringComparison.Ordinal));
            var copyDriverData = new SerializedObject(copyDriver);
            var driverItems = copyDriverData.FindProperty("parameters");
            Assert(driverItems != null && driverItems.arraySize == isolated.expressionInputMappings.Length,
                "Live expression Copy layer does not copy every isolated input.");
            for (int i = 0; i < driverItems.arraySize; i++)
            {
                var item = driverItems.GetArrayElementAtIndex(i);
                string target = item.FindPropertyRelative("name").stringValue;
                string input = item.FindPropertyRelative("source").stringValue;
                int changeType = item.FindPropertyRelative("type").intValue;
                int copyType = Convert.ToInt32(VRC_AvatarParameterDriver.ChangeType.Copy);
                Assert(isolated.expressionInputMappings.Any(mapping => mapping.Input == input && mapping.Target == target) && changeType == copyType,
                    "Live expression Copy driver has an invalid input, alias, or operation.");
                Assert(target != input, "Live expression Copy driver writes back to a VRChat built-in.");
            }
            Assert(copyMachine.states.Single(entry => entry.state.name == "paused for expression playback").state
                       .transitions.Single().conditions.Any(condition => condition.parameter == "nxclone_recording_play"),
                "Live expression Copy layer is not gated by the recording Play parameter.");
            var timerBindings = AnimationUtility.GetCurveBindings(copyState.motion as AnimationClip);
            bool repeats = copyState.transitions.Any(transition => transition.destinationState &&
                transition.destinationState.name == "copy live inputs alternate" && transition.hasExitTime);
            Assert(repeats && timerBindings.Length > 0 && timerBindings.All(binding => !string.IsNullOrEmpty(binding.path)),
                $"Live expression Copy layer does not resample continuously on its hidden timer child (repeat={repeats}, bindings={timerBindings.Length}, path='{(timerBindings.Length == 0 ? "" : timerBindings[0].path)}').");

            animator.Rebind();
            animator.Update(0f);
            animator.SetInteger(gestureAlias, 0);
            animator.SetInteger("GestureLeft", 2);
            for (int step = 0; step < 3; step++) animator.Update(0.1f);
            int rootGestureIndex = targetFx.layers.Length - 3;
            int cloneGestureIndex = targetFx.layers.Length - 2;
            Assert(animator.GetCurrentAnimatorStateInfo(rootGestureIndex).IsName("Root On") &&
                   animator.GetCurrentAnimatorStateInfo(cloneGestureIndex).IsName("Root source Off"),
                "Native Animator did not keep root and clone gesture transitions independent.");
            animator.SetInteger(gestureAlias, 2);
            for (int step = 0; step < 3; step++) animator.Update(0.1f);
            Assert(animator.GetCurrentAnimatorStateInfo(cloneGestureIndex).IsName("Root source On"),
                "Native Animator did not transition the clone when its isolated gesture alias changed.");

            var generatedPathClip = new AnimationClip { name = "Generated nxclone path" };
            AnimationUtility.SetEditorCurve(generatedPathClip,
                EditorCurveBinding.FloatCurve("nxclone/clone-1/Face", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
            AssetDatabase.CreateAsset(generatedPathClip, folder + "/generated-path.anim");
            var unsafeSourceFx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/unsafe-root-source.controller");
            var unsafeState = unsafeSourceFx.layers[0].stateMachine.AddState("Unsafe");
            unsafeState.motion = generatedPathClip;
            var layersBeforeUnsafeMerge = targetFx.layers.Length;
            bool rejectedGeneratedPath = false;
            try
            {
                NxCloneSourceFx.Merge(targetFx, unsafeSourceFx, targetRoot.transform, targetRoot.transform,
                    cloneRoot.transform, 4, null, null, rootParameters, folder, isolateExpressionInputs: true);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("generated nxclone path", StringComparison.Ordinal))
            { rejectedGeneratedPath = true; }
            Assert(rejectedGeneratedPath && targetFx.layers.Length == layersBeforeUnsafeMerge,
                "Root-source merge accepted a generated nxclone path or mutated the controller on rejection.");

            var badLayerFx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/bad-layer-control.controller");
            var badLayerControl = badLayerFx.layers[0].stateMachine.AddState("Bad layer control")
                .AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            badLayerControl.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            badLayerControl.layer = 99;
            var layersBeforeBadControl = targetFx.layers.Length;
            bool rejectedBadLayerControl = false;
            try
            {
                NxCloneSourceFx.Merge(targetFx, badLayerFx, sourceRoot.transform, targetRoot.transform,
                    cloneRoot.transform, 5, null, null, rootParameters, folder);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("source FX layer 99", StringComparison.Ordinal))
            { rejectedBadLayerControl = true; }
            Assert(rejectedBadLayerControl && targetFx.layers.Length == layersBeforeBadControl,
                "Source FX layer control did not reject an out-of-range source index before mutation.");
            Debug.Log("NXCLONE_SOURCE_FX_SMOKE_OK");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceRoot);
            UnityEngine.Object.DestroyImmediate(targetRoot);
            AssetDatabase.DeleteAsset(folder);
            AssetDatabase.SaveAssets();
        }
    }

    static GameObject NewRenderer(Transform parent, string name)
    {
        var result = new GameObject(name);
        result.transform.SetParent(parent, false);
        result.AddComponent<SkinnedMeshRenderer>();
        return result;
    }

    static string ReadDriverParameter(StateMachineBehaviour behaviour)
    {
        var serialized = new SerializedObject(behaviour);
        var name = serialized.FindProperty("parameters")?.GetArrayElementAtIndex(0)?.FindPropertyRelative("name");
        return name?.stringValue;
    }

    static bool Locked(AnimatorTransitionBase transition, string parameter) => transition.conditions.Any(condition =>
        condition.parameter == parameter && condition.mode == AnimatorConditionMode.IfNot);

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
