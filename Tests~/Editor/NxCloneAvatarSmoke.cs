using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using nxclone;
using VRC.SDK3.Dynamics.Constraint.Components;

public static class NxCloneAvatarSmoke
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/NX.unity");
        var source = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true).Single(x => x.name == "Nixomi cloned");
        int componentsBefore = source.GetComponentsInChildren<Component>(true).Length;
        var window = ScriptableObject.CreateInstance<NxCloneWindow>();
        Set(window, "avatar", source);
        Set(window, "worldDrop", true);
        Set(window, "poseFreeze", true);
        Set(window, "runtimeScale", true);
        Set(window, "afterimages", true);
        Set(window, "afterimageCount", 2);
        typeof(NxCloneWindow).GetMethod("Generate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
        var output = Selection.activeGameObject;
        if (!output || !output.name.EndsWith("_nxclone") || output == source.gameObject) throw new Exception("Generation failed");
        if (componentsBefore != source.GetComponentsInChildren<Component>(true).Length) throw new Exception("Source changed");
        var uploadCopy = UnityEngine.Object.Instantiate(output);
        if (!VRCBuildPipelineCallbacks.OnPreprocessAvatar(uploadCopy)) throw new Exception("Upload preprocessing rejected output");
        output = uploadCopy;
        if (output.GetComponent<NxCloneSetup>()) throw new Exception("Authoring component not removed");
        var descriptor = output.GetComponent<VRCAvatarDescriptor>();
        if (!descriptor.customizeAnimationLayers) throw new Exception("Custom FX disabled");
        var group = output.transform.Find("nxclone");
        if (!group || group.childCount != 3 || group.Cast<Transform>().Any(x => x.gameObject.activeSelf)) throw new Exception("Visuals not initially hidden");
        var clone = group.Find("clone-1");
        var mainMeshes = output.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => !x.transform.IsChildOf(group) && !x.transform.IsChildOf(output.transform.Find("__nxclone main silhouette mask"))).ToArray();
        foreach (var original in mainMeshes)
        {
            var path = AnimationUtility.CalculateTransformPath(original.transform, output.transform);
            var counterpart = clone.Find(path)?.GetComponent<SkinnedMeshRenderer>();
            if (!counterpart || original.sharedMesh != counterpart.sharedMesh) throw new Exception("Baked clone mesh mismatch: " + path);
            for (int i = 0; i < original.bones.Length; i++)
            {
                if (!original.bones[i]) continue;
                var bonePath = AnimationUtility.CalculateTransformPath(original.bones[i], output.transform);
                if (!counterpart.bones[i] || AnimationUtility.CalculateTransformPath(counterpart.bones[i], clone) != bonePath)
                    throw new Exception("Armature link bone mismatch: " + path);
            }
        }
        var fx = descriptor.baseAnimationLayers.First(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
        output.SetActive(true);
        var animator = output.GetComponent<Animator>();
        animator.runtimeAnimatorController = fx;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0.1f);
        animator.SetBool("nxclone_visible", true);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (!clone.gameObject.activeSelf || group.Find("afterimage-1").gameObject.activeSelf) throw new Exception("Real clone toggle failed");
        animator.SetBool("nxclone_afterimages", true);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (!group.Find("afterimage-1").gameObject.activeSelf || !output.transform.Find("__nxclone main silhouette mask").gameObject.activeSelf) throw new Exception("Real afterimages or silhouette mask toggle failed");
        animator.SetBool("nxclone_drop_1", true);
        animator.SetBool("nxclone_freeze_1", true);
        animator.SetFloat("nxclone_scale", 1f);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (!clone.GetComponent<VRCParentConstraint>().FreezeToWorld) throw new Exception("Real world-drop toggle failed");
        if (clone.GetComponentsInChildren<VRCRotationConstraint>(true).Any(x => !x.FreezeToWorld)) throw new Exception("Real pose-freeze toggle failed");
        if (Mathf.Abs(clone.localScale.x - 2f) > 0.01f) throw new Exception("Real scale dial failed");
        animator.SetBool("nxclone_visible", false);
        animator.Update(0.1f);
        animator.Update(0.1f);
        if (clone.gameObject.activeSelf || !group.Find("afterimage-1").gameObject.activeSelf) throw new Exception("Real controls not independent");


        UnityEngine.Object.DestroyImmediate(uploadCopy);
        Debug.Log("NXCLONE_REAL_AVATAR_SMOKE_OK: " + mainMeshes.Length + " linked meshes, hidden roots, executed toggles/drop/freeze/scale, upload preprocessing");
    }
    static void Set(NxCloneWindow window, string name, object value) => typeof(NxCloneWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
}
