using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using nxclone;

public static class NxCloneRenderSmoke
{
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var shader = Shader.Find("nxclone/solid translucent");
        var maskShader = Shader.Find("nxclone/silhouette mask");
        if (!shader || !maskShader || !shader.isSupported || !maskShader.isSupported) throw new Exception("Silhouette shaders unsupported");
        var blue = new Material(shader) { color = new Color(0, 0, 1, 0.5f) };
        var red = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
        var primary = new GameObject("primary");
        Sphere(primary.transform, "body", new Vector3(0.35f, 0, 0.8f), 0.65f, red);
        NxCloneAfterimages.CreateMainSilhouetteMask(primary.transform, primary.transform, new Material(maskShader));
        Sphere(null, "ghost", new Vector3(-0.3f, 0, 0), 1.5f, blue);
        Sphere(null, "overlapping ghost", new Vector3(-0.3f, 0, -0.1f), 1.4f, blue);
        var camera = new GameObject("camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -5);
        camera.orthographic = true;
        camera.orthographicSize = 1.5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.renderingPath = RenderingPath.Forward;
        var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(256, 256, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "nxclone-flat-afterimages.png"), image.EncodeToPNG());
        var center = image.GetPixel(95, 128);
        var edge = image.GetPixel(70, 128);
        var main = image.GetPixel(156, 128);
        if (Mathf.Abs(center.b - edge.b) > 0.025f || center.b < 0.3f || center.b > 0.8f || center.r > 0.03f)
            throw new Exception("Ghost opacity is not flat: " + center + " vs " + edge);
        if (main.r < 0.95f || main.b > 0.03f) throw new Exception("Ghost drawn over main avatar: " + main);
        Debug.Log("NXCLONE_RENDER_SMOKE_OK: flat alpha and primary silhouette priority, " + SystemInfo.graphicsDeviceType);
    }
    static void Sphere(Transform parent, string name, Vector3 position, float scale, Material material)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        if (parent) sphere.transform.SetParent(parent, false);
        sphere.transform.localPosition = position;
        sphere.transform.localScale = Vector3.one * scale;
        sphere.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
    }
}
