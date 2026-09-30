using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using nxclone;

[InitializeOnLoad]
public static class NxCloneAfterimagePlacementSmoke
{
    const string RunningKey = "nxclone.afterimage-placement.running";
    const string AvatarName = "__nxclone afterimage placement smoke";
    static Transform avatar, armature, visual;
    static SkinnedMeshRenderer sourceRenderer, visualRenderer;
    static Mesh mesh;
    static readonly Vector3 initialArmaturePosition = new Vector3(0.17f, 0.58f, -0.12f);
    static readonly Quaternion initialArmatureRotation = Quaternion.Euler(9f, 4f, 13f);
    static readonly Vector3 initialArmatureScale = new Vector3(1.03f, 0.97f, 1.02f);
    static int stage, frames;
    static bool initialPoseExact, animatedAncestorFollowed, returnedPoseExact;

    static NxCloneAfterimagePlacementSmoke() => EditorApplication.playModeStateChanged += OnMode;

    public static void CountRealAvatarRig()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run from Edit Mode");
        EditorSceneManager.OpenScene("Assets/NX.unity");
        var source = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true).Single(x => x.name == "Nixomi cloned");
        var copy = UnityEngine.Object.Instantiate(source.gameObject);
        copy.name = "__nxclone afterimage rig count";
        int before = ConstraintCount(copy);
        int added = NxCloneAfterimageRig.Configure(source.transform, copy.transform, 0.25f);
        int renderers = copy.GetComponentsInChildren<Renderer>(true).Length;
        int bones = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(x => (x.bones ?? Array.Empty<Transform>()).Where(b => b).Distinct().Count());
        int after = ConstraintCount(copy);
        Debug.Log($"NXCLONE_AFTERIMAGE_REAL_AVATAR requiredTransforms={added / 3} addedConstraints={added} totalConstraints={after} preexistingConstraints={before} renderers={renderers} boneRefs={bones}");
        UnityEngine.Object.DestroyImmediate(copy);
        EditorApplication.Exit(0);
    }

    static int ConstraintCount(GameObject target) => target.GetComponentsInChildren<VRCRotationConstraint>(true).Length +
        target.GetComponentsInChildren<VRCPositionConstraint>(true).Length +
        target.GetComponentsInChildren<VRCScaleConstraint>(true).Length;

    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run from Edit Mode");
        SessionState.SetBool("nxclone.afterimage-placement.initial", false);
        SessionState.SetBool("nxclone.afterimage-placement.animated", false);
        SessionState.SetBool("nxclone.afterimage-placement.returned", false);
        avatar = new GameObject(AvatarName).transform;
        avatar.SetPositionAndRotation(new Vector3(4f, 1.1f, -3f), Quaternion.Euler(8f, 47f, -5f));
        avatar.localScale = new Vector3(1.35f, 1.35f, 1.35f);

        armature = Child(avatar, "Armature", new Vector3(0.17f, 0.58f, -0.12f), Quaternion.Euler(9f, 4f, 13f), new Vector3(1.03f, 0.97f, 1.02f));
        var hips = Child(armature, "Hips", new Vector3(0f, 0.06f, 0f), Quaternion.Euler(0f, 6f, 0f), Vector3.one);
        var chest = Child(hips, "Chest", new Vector3(0f, 0.52f, 0.02f), Quaternion.Euler(2f, 0f, -3f), Vector3.one);
        var head = Child(chest, "Head", new Vector3(0f, 0.36f, 0f), Quaternion.Euler(0f, -3f, 0f), Vector3.one);
        var meshHost = Child(chest, "BodyMesh", new Vector3(0.04f, 0.03f, 0.01f), Quaternion.Euler(0f, 0f, 4f), Vector3.one);
        sourceRenderer = meshHost.gameObject.AddComponent<SkinnedMeshRenderer>();
        mesh = MakeMesh(sourceRenderer, new[] { hips, chest, head });
        sourceRenderer.sharedMesh = mesh;
        sourceRenderer.bones = new[] { hips, chest, head };
        sourceRenderer.rootBone = hips;
        sourceRenderer.localBounds = new Bounds(Vector3.up * 0.45f, Vector3.one * 2f);

        var copy = UnityEngine.Object.Instantiate(avatar.gameObject);
        copy.name = "__nxclone afterimage visual";
        visual = copy.transform;
        visualRenderer = visual.Find("Armature/Hips/Chest/BodyMesh").GetComponent<SkinnedMeshRenderer>();

        var group = Child(avatar, "nxclone", Vector3.zero, Quaternion.identity, Vector3.one);
        var frame = Child(group, "world", Vector3.zero, Quaternion.identity, Vector3.one);
        NxClonePlacement.FreezeFrame(frame);
        var driver = NxClonePlacement.Delay(frame, avatar, "trail-1", 0.25f);
        visual.SetParent(driver, false);
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        visual.localScale = Vector3.one;
        int rigCount = NxCloneAfterimageRig.Configure(avatar, visual, 0.25f);
        if (rigCount < 4) throw new Exception($"Expected armature and bone transforms to be constrained; got {rigCount}");

        var runner = avatar.gameObject.AddComponent<Animator>();
        runner.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        SessionState.SetBool(RunningKey, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnMode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            avatar = GameObject.Find(AvatarName).transform;
            armature = avatar.Find("Armature");
            visual = GameObject.Find("__nxclone afterimage visual").transform;
            sourceRenderer = avatar.Find("Armature/Hips/Chest/BodyMesh").GetComponent<SkinnedMeshRenderer>();
            visualRenderer = visual.Find("Armature/Hips/Chest/BodyMesh").GetComponent<SkinnedMeshRenderer>();
            stage = frames = 0;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(RunningKey, false);
            bool success = SessionState.GetBool("nxclone.afterimage-placement.initial", false) &&
                SessionState.GetBool("nxclone.afterimage-placement.animated", false) &&
                SessionState.GetBool("nxclone.afterimage-placement.returned", false);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }

    static void Tick()
    {
        try
        {
            frames++;
            if (stage == 0 && frames >= 20)
            {
                initialPoseExact = SamePoseAndRender();
                SessionState.SetBool("nxclone.afterimage-placement.initial", initialPoseExact);
                Debug.Log($"AFTERIMAGE initial registered={initialPoseExact} avatarScale={avatar.lossyScale:F3} visualScale={visual.lossyScale:F3}");
                if (!initialPoseExact) throw new Exception("Afterimage did not register with the scaled/rotated source at rest.");
                armature.localPosition += new Vector3(0.28f, -0.13f, 0.19f);
                armature.localRotation = Quaternion.Euler(27f, 32f, -18f);
                armature.localScale = new Vector3(1.11f, 0.86f, 1.07f);
                stage = 1;
                frames = 0;
            }
            else if (stage == 1 && frames >= 12)
            {
                var visualArmature = visual.Find("Armature");
                animatedAncestorFollowed = Vector3.Distance(visualArmature.localPosition, initialArmaturePosition) > 0.03f &&
                    Quaternion.Angle(visualArmature.localRotation, initialArmatureRotation) > 2f &&
                    Vector3.Distance(visualArmature.localScale, armature.localScale) < 0.01f;
                SessionState.SetBool("nxclone.afterimage-placement.animated", animatedAncestorFollowed);
                Debug.Log($"AFTERIMAGE animated ancestor followed={animatedAncestorFollowed} sourcePos={armature.localPosition:F3} ghostPos={visualArmature.localPosition:F3} sourceRot={armature.localRotation.eulerAngles:F1} ghostRot={visualArmature.localRotation.eulerAngles:F1} sourceScale={armature.localScale:F3} ghostScale={visualArmature.localScale:F3}");
                if (!animatedAncestorFollowed) throw new Exception("Afterimage did not follow animated armature ancestor transforms.");
                armature.localPosition = initialArmaturePosition;
                armature.localRotation = initialArmatureRotation;
                armature.localScale = initialArmatureScale;
                stage = 2;
                frames = 0;
            }
            else if (stage == 2 && frames >= 120)
            {
                returnedPoseExact = SamePoseAndRender();
                SessionState.SetBool("nxclone.afterimage-placement.returned", returnedPoseExact);
                Debug.Log($"AFTERIMAGE returned registered={returnedPoseExact}");
                if (!returnedPoseExact) throw new Exception("Afterimage did not settle back onto source render shape.");
                Debug.Log("NXCLONE_AFTERIMAGE_PLACEMENT_DONE");
                EditorApplication.update -= Tick;
                UnityEngine.Object.Destroy(avatar.gameObject);
                UnityEngine.Object.Destroy(mesh);
                EditorApplication.ExitPlaymode();
            }
        }
        catch (Exception e)
        {
            EditorApplication.update -= Tick;
            Debug.LogException(e);
            SessionState.SetBool(RunningKey, false);
            EditorApplication.Exit(1);
        }
    }

    static bool SamePoseAndRender()
    {
        foreach (var path in new[] { "Armature", "Armature/Hips", "Armature/Hips/Chest", "Armature/Hips/Chest/Head", "Armature/Hips/Chest/BodyMesh" })
        {
            var a = avatar.Find(path);
            var b = visual.Find(path);
            if (!a || !b || Vector3.Distance(a.position, b.position) > 0.01f ||
                Quaternion.Angle(a.rotation, b.rotation) > 0.1f || Vector3.Distance(a.lossyScale, b.lossyScale) > 0.01f)
            {
                Debug.Log($"AFTERIMAGE transform mismatch path={path} source={(a ? $"local={a.localPosition:F3}/{a.localRotation.eulerAngles:F1}/{a.localScale:F3} world={a.position:F3}/{a.rotation.eulerAngles:F1}/{a.lossyScale:F3}" : "missing")} visual={(b ? $"local={b.localPosition:F3}/{b.localRotation.eulerAngles:F1}/{b.localScale:F3} world={b.position:F3}/{b.rotation.eulerAngles:F1}/{b.lossyScale:F3}" : "missing")}");
                return false;
            }
        }
        var aMesh = new Mesh();
        var bMesh = new Mesh();
        sourceRenderer.BakeMesh(aMesh);
        visualRenderer.BakeMesh(bMesh);
        var aVertices = aMesh.vertices;
        var bVertices = bMesh.vertices;
        bool same = aVertices.Length == bVertices.Length && aVertices.Select((v, i) =>
            Vector3.Distance(sourceRenderer.transform.TransformPoint(v), visualRenderer.transform.TransformPoint(bVertices[i])) <= 0.01f).All(x => x);
        if (!same && aVertices.Length == bVertices.Length)
        {
            float max = aVertices.Select((v, i) => Vector3.Distance(sourceRenderer.transform.TransformPoint(v), visualRenderer.transform.TransformPoint(bVertices[i]))).Max();
            Debug.Log($"AFTERIMAGE render mismatch maxVertexDelta={max:F4} sourceRenderer={sourceRenderer.transform.position:F3} visualRenderer={visualRenderer.transform.position:F3}");
        }
        UnityEngine.Object.Destroy(aMesh);
        UnityEngine.Object.Destroy(bMesh);
        return same;
    }

    static Transform Child(Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = position;
        child.localRotation = rotation;
        child.localScale = scale;
        return child;
    }

    static Mesh MakeMesh(SkinnedMeshRenderer renderer, Transform[] bones)
    {
        var result = new Mesh { name = "nxclone afterimage placement probe" };
        result.vertices = new[]
        {
            new Vector3(-0.18f, -0.06f, 0f), new Vector3(0.18f, -0.06f, 0f),
            new Vector3(-0.14f, 0.42f, 0f), new Vector3(0.14f, 0.42f, 0f),
            new Vector3(-0.09f, 0.77f, 0f), new Vector3(0.09f, 0.77f, 0f)
        };
        result.triangles = new[] { 0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5 };
        result.boneWeights = Enumerable.Range(0, 6).Select(i => new BoneWeight { boneIndex0 = Mathf.Min(i / 2, bones.Length - 1), weight0 = 1f }).ToArray();
        renderer.bones = bones;
        result.bindposes = bones.Select(bone => bone.worldToLocalMatrix * renderer.transform.localToWorldMatrix).ToArray();
        result.RecalculateBounds();
        return result;
    }
}
