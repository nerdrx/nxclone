using System;
using System.Linq;
using System.Reflection;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;

public static class NxCloneSmoke
{
    public static void Run()
    {
        AssetDatabase.DeleteAsset("Assets/nxclone-smoke");
        AssetDatabase.DeleteAsset("Assets/nxclone-source");
        AssetDatabase.DeleteAsset("Assets/nxclone-smoke-fx");
        var avatar = new GameObject("smoke-avatar");
        var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
        var group = new GameObject("nxclone");
        group.transform.SetParent(avatar.transform);
        var clone = new GameObject("clone-1");
        clone.transform.SetParent(group.transform);
        var cloneRenderer = clone.AddComponent<SkinnedMeshRenderer>();
        cloneRenderer.enabled = false;
        var ghost = new GameObject("afterimage-1");
        ghost.transform.SetParent(group.transform);
        ghost.AddComponent<SkinnedMeshRenderer>();
        clone.AddComponent<VRCParentConstraint>();
        var smokeBone = new GameObject("bone");
        smokeBone.transform.SetParent(clone.transform);
        smokeBone.AddComponent<VRCRotationConstraint>();
        clone.SetActive(false);
        ghost.SetActive(false);
        AssetDatabase.CreateFolder("Assets", "nxclone-smoke");
        var window = ScriptableObject.CreateInstance<NxCloneWindow>();
        typeof(NxCloneWindow).GetField("avatar", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, descriptor);
        typeof(NxCloneWindow).GetField("worldDrop", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, true);
        typeof(NxCloneWindow).GetField("afterimages", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, true);
        typeof(NxCloneWindow).GetField("copyVisemes", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, false);
        typeof(NxCloneWindow).GetField("runtimeScale", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, true);
        typeof(NxCloneWindow).GetField("poseFreeze", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, true);
        var method = typeof(NxCloneWindow).GetMethod("InstallToggle", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(window, new object[] { descriptor, "Assets/nxclone-smoke", group.transform, new System.Collections.Generic.List<Transform> { clone.transform }, new System.Collections.Generic.List<Transform> { ghost.transform } });
        AssetDatabase.SaveAssets();
        var fx = descriptor.baseAnimationLayers.First(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
        if (!descriptor.customizeAnimationLayers) throw new Exception("Custom animation layers disabled");
        if (clone.activeSelf || ghost.activeSelf) throw new Exception("Visuals spawn visible");
        foreach (var control in descriptor.expressionsMenu.controls[0].subMenu.controls)
            if (control.type == VRCExpressionsMenu.Control.ControlType.Toggle && control.value != 1)
                throw new Exception("Toggle on-value is not one: " + control.name);
        if (descriptor.expressionParameters.parameters.Where(x => x.name == "nxclone_visible" || x.name == "nxclone_afterimages").Any(x => x.saved || x.defaultValue != 0))
            throw new Exception("Visibility must reset off");
        var animator = avatar.AddComponent<Animator>();
        animator.runtimeAnimatorController = fx;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0.1f);
        animator.SetBool("nxclone_visible", true);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (!clone.activeSelf || ghost.activeSelf || cloneRenderer.enabled) throw new Exception("Clone toggle failed or enabled hidden outfit");
        animator.SetBool("nxclone_afterimages", true);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (!ghost.activeSelf) throw new Exception("Afterimages toggle failed");
        animator.SetBool("nxclone_visible", false);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (clone.activeSelf || !ghost.activeSelf) throw new Exception("Clone/afterimage controls not independent");
        animator.SetBool("nxclone_afterimages", false);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (ghost.activeSelf) throw new Exception("Afterimages toggle off failed");
        if (!fx || fx.layers.Length < 2 || fx.parameters.Length < 2) throw new Exception("FX controller or controls missing");
        if (!descriptor.customExpressions || !descriptor.expressionParameters || !descriptor.expressionsMenu) throw new Exception("Expression assets missing");
        if (descriptor.expressionsMenu.controls.Count != 1 || !descriptor.expressionsMenu.controls[0].subMenu) throw new Exception("Submenu missing");
        if (!AnimationUtility.GetCurveBindings(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/nxclone-smoke/clone-1-frozen-pose.anim")).Any(x => x.propertyName == "FreezeToWorld" && x.type == typeof(VRCRotationConstraint))) throw new Exception("Freeze pose curve missing");
        if (!fx.parameters.Any(x => x.name.StartsWith("nxclone_scale") && x.type == AnimatorControllerParameterType.Float)) throw new Exception("Scale parameter missing");
        if (!AnimationUtility.GetCurveBindings(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/nxclone-smoke/scale-1.anim")).Any(x => x.propertyName == "m_LocalScale.x")) throw new Exception("Scale clip missing");
        var bindings = AnimationUtility.GetCurveBindings(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/nxclone-smoke/clone-1-world.anim"));
        if (!bindings.Any(x => x.propertyName == "FreezeToWorld")) throw new Exception("World drop animation missing");
        var second = new GameObject("source-avatar");
        var secondDescriptor = second.AddComponent<VRCAvatarDescriptor>();
        var body = new GameObject("body");
        body.transform.SetParent(second.transform);
        body.AddComponent<SkinnedMeshRenderer>();
        var sourceClip = new AnimationClip();
        AnimationUtility.SetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve("body", typeof(SkinnedMeshRenderer), "blendShape.Smile"),
            AnimationCurve.Constant(0, 1, 100));
        AssetDatabase.CreateFolder("Assets", "nxclone-source");
        AssetDatabase.CreateAsset(sourceClip, "Assets/nxclone-source/smile.anim");
        var swappedMaterial = new Material(Shader.Find("Standard"));
        AssetDatabase.CreateAsset(swappedMaterial, "Assets/nxclone-source/swapped.mat");
        AnimationUtility.SetObjectReferenceCurve(sourceClip,
            EditorCurveBinding.PPtrCurve("body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"),
            new[] { new ObjectReferenceKeyframe { time = 0, value = swappedMaterial } });
        var sourceFx = AnimatorController.CreateAnimatorControllerAtPath("Assets/nxclone-source/fx.controller");
        sourceFx.layers[0].stateMachine.AddState("smile").motion = sourceClip;
        secondDescriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer {
            type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, isEnabled = true, animatorController = sourceFx
        } };
        var secondGroup = new GameObject("nxclone");
        secondGroup.transform.SetParent(second.transform);
        var secondClone = new GameObject("clone-1");
        secondClone.transform.SetParent(secondGroup.transform);
        var cloneBody = new GameObject("body");
        cloneBody.transform.SetParent(secondClone.transform);
        cloneBody.AddComponent<SkinnedMeshRenderer>();
        AssetDatabase.CreateFolder("Assets", "nxclone-smoke-fx");
        typeof(NxCloneWindow).GetField("avatar", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, secondDescriptor);
        typeof(NxCloneWindow).GetField("worldDrop", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, false);
        typeof(NxCloneWindow).GetField("runtimeScale", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, false);
        typeof(NxCloneWindow).GetField("poseFreeze", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, false);
        typeof(NxCloneWindow).GetField("afterimages", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, false);
        method.Invoke(window, new object[] { secondDescriptor, "Assets/nxclone-smoke-fx", secondGroup.transform, new System.Collections.Generic.List<Transform> { secondClone.transform }, new System.Collections.Generic.List<Transform>() });
        AssetDatabase.SaveAssets();
        var mirroredFx = secondDescriptor.baseAnimationLayers[0].animatorController as AnimatorController;
        var mirroredClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/nxclone-smoke-fx/smile-fx.anim");
        if (!mirroredFx.animationClips.Contains(mirroredClip)) throw new Exception("FX controller lost mirrored clip");
        if (!mirroredClip || !AnimationUtility.GetCurveBindings(mirroredClip).Any(x => x.path == "nxclone/clone-1/body"))
            throw new Exception("FX visual mirroring failed");
        if (AnimationUtility.GetCurveBindings(sourceClip).Any(x => x.path == "nxclone/clone-1/body"))
            throw new Exception("Source FX asset mutated");
        var silhouette = new GameObject("silhouette");
        silhouette.transform.SetParent(second.transform);
        var silhouetteBody = new GameObject("body");
        silhouetteBody.transform.SetParent(silhouette.transform);
        silhouetteBody.AddComponent<SkinnedMeshRenderer>();
        NxCloneFxMirror.MirrorFxCurves(mirroredFx, second.transform, new[] { silhouette.transform }, "Assets/nxclone-smoke-fx", silhouetteOnly: true);
        if (mirroredFx.animationClips.SelectMany(AnimationUtility.GetObjectReferenceCurveBindings).Any(x => x.path.StartsWith("silhouette/")))
            throw new Exception("Silhouette material overwritten by avatar material swaps");
        if (!mirroredFx.animationClips.SelectMany(AnimationUtility.GetCurveBindings).Any(x => x.path == "silhouette/body" && x.propertyName == "blendShape.Smile"))
            throw new Exception("Silhouette blendshape missing");
        AssetDatabase.DeleteAsset("Assets/nxclone-viseme-smoke");
        AssetDatabase.CreateFolder("Assets", "nxclone-viseme-smoke");
        var visemeSource = new GameObject("viseme-source");
        var visemeDescriptor = visemeSource.AddComponent<VRCAvatarDescriptor>();
        var sourceFace = new GameObject("face");
        sourceFace.transform.SetParent(visemeSource.transform);
        var sourceFaceRenderer = sourceFace.AddComponent<SkinnedMeshRenderer>();
        var mesh = new Mesh();
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        var names = Enumerable.Range(0, 15).Select(x => "vrc-" + x).ToArray();
        foreach (var name in names) mesh.AddBlendShapeFrame(name, 100f, new Vector3[3], new Vector3[3], new Vector3[3]);
        sourceFaceRenderer.sharedMesh = mesh;
        visemeDescriptor.VisemeSkinnedMesh = sourceFaceRenderer;
        visemeDescriptor.VisemeBlendShapes = names;
        var visemeAvatar = new GameObject("viseme-avatar");
        var visual = new GameObject("visual");
        visual.transform.SetParent(visemeAvatar.transform);
        var visualFace = new GameObject("face");
        visualFace.transform.SetParent(visual.transform);
        visualFace.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
        var visemeFx = AnimatorController.CreateAnimatorControllerAtPath("Assets/nxclone-viseme-smoke/fx.controller");
        if (!NxCloneVisemes.AddCloneVisemes(visemeFx, visemeDescriptor, visemeAvatar.transform, visual.transform, "Assets/nxclone-viseme-smoke"))
            throw new Exception("Viseme setup failed");
        var visemeClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/nxclone-viseme-smoke/nxclone-viseme-01.anim");
        if (!visemeClip || !AnimationUtility.GetCurveBindings(visemeClip).Any(x => x.path == "visual/face" && x.propertyName == "blendShape.vrc-1"))
            throw new Exception("Viseme curve missing");
        Debug.Log("NXCLONE_SMOKE_OK");
    }
}
