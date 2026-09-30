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
        var primaryBody = Sphere(primary.transform, "body", new Vector3(0.35f, 0, 0.8f), 0.65f, red);
        NxCloneAfterimages.CreateMainSilhouetteMask(primary.transform, primary.transform, new Material(maskShader));
        var ghost = Sphere(null, "ghost", new Vector3(-0.3f, 0, 0), 1.5f, blue);
        var overlappingGhost = Sphere(null, "overlapping ghost", new Vector3(-0.3f, 0, -0.1f), 1.4f, blue);
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
        ghost.SetActive(false);
        overlappingGhost.SetActive(false);
        var cameraGhost = Sphere(null, "camera-enclosing ghost", camera.transform.position, 0.2f, blue);
        camera.Render();
        RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
        image.Apply();
        var output = "/home/nerdrx/Documents/ChatGPT/FUN/nxclone/artifacts/validation/afterimage-camera.png";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllBytes(output, image.EncodeToPNG());
        center = image.GetPixel(95, 128);
        edge = image.GetPixel(70, 128);
        main = image.GetPixel(156, 128);
        if (center.b > 0.03f || edge.b > 0.03f) throw new Exception("Camera-enclosing ghost did not clear view: " + center + " / " + edge);
        if (main.r < 0.95f || main.b > 0.03f) throw new Exception("Camera fade hid primary avatar: " + main);
        cameraGhost.SetActive(false);
        primary.SetActive(false);
        var blueGhost = new Material(shader) { color = new Color(0, 0, 1, 0.5f) };
        blueGhost.SetFloat("_StencilBit", 2);
        blueGhost.renderQueue = 3050;
        var redGhost = new Material(shader) { color = new Color(1, 0, 0, 0.75f) };
        redGhost.SetFloat("_StencilBit", 4);
        redGhost.renderQueue = 3051;
        var blueShape = Sphere(null, "blue ghost", new Vector3(-0.5f, 0, -0.25f), 1.5f, blueGhost);
        var redShape = Sphere(null, "red ghost", new Vector3(0.5f, 0, 0.25f), 1.5f, redGhost);
        camera.Render();
        RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
        image.Apply();
        File.WriteAllBytes("/home/nerdrx/Documents/ChatGPT/FUN/nxclone/artifacts/validation/afterimage-colors.png", image.EncodeToPNG());
        var blueOnly = image.GetPixel(50, 128);
        var overlap = image.GetPixel(128, 128);
        var redOnly = image.GetPixel(206, 128);
        if (blueOnly.b < 0.45f || blueOnly.r > 0.03f || redOnly.r < 0.45f || redOnly.b > 0.03f)
            throw new Exception("Distinct ghost colors missing: " + blueOnly + " / " + redOnly);
        if (overlap.r < 0.2f || overlap.b < 0.1f || overlap.r <= overlap.b)
            throw new Exception("Overlapping ghosts did not both render: " + overlap);
        blueShape.SetActive(false);
        redShape.SetActive(false);
        var mainTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        mainTexture.SetPixel(0, 0, new Color(1, 0, 0, 0.5f));
        mainTexture.Apply();
        var lateMain = new Material(Shader.Find("Unlit/Transparent")) { mainTexture = mainTexture, renderQueue = 3500 };
        primaryBody.GetComponent<Renderer>().sharedMaterial = lateMain;
        primary.SetActive(true);
        var frontGhost = new Material(shader) { color = new Color(0, 0, 1, 0.5f) };
        frontGhost.SetFloat("_StencilBit", 2);
        frontGhost.renderQueue = 3050;
        Sphere(null, "front ghost", new Vector3(0.35f, 0, 0), 0.9f, frontGhost);
        camera.Render();
        RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
        image.Apply();
        main = image.GetPixel(156, 128);
        if (main.r < 0.65f || main.b > 0.03f)
            throw new Exception("Front ghost covered late transparent main avatar: " + main);
        Debug.Log("NXCLONE_RENDER_SMOKE_OK: flat alpha, primary priority, camera fade, distinct ordered colors, late main priority, " + SystemInfo.graphicsDeviceType);
    }
    static GameObject Sphere(Transform parent, string name, Vector3 position, float scale, Material material)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        if (parent) sphere.transform.SetParent(parent, false);
        sphere.transform.localPosition = position;
        sphere.transform.localScale = Vector3.one * scale;
        sphere.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
        return sphere;
    }
}
