using System;
using System.Reflection;
using nxclone;
using UnityEditor;
using UnityEngine;

// Visual fixture: capture the actual X11 utility window under hidden Gamescope.
public static class NxCloneUiSmoke
{
    static NxCloneWindow window;
    static int frames;
    public static void Run()
    {
        try { NxCloneOptionsSmoke.Run(); RunOnly(); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    public static void RunOnly()
    {
        window = ScriptableObject.CreateInstance<NxCloneWindow>();
        window.titleContent = new GUIContent("nxclone narrow UI");
        window.ShowUtility();
        window.position = new Rect(60f, 60f, 300f, 850f);
        Set("runtimePosition", true);
        Set("afterimages", true);
        Set("afterimageCount", 4);
        frames = 0;
        EditorApplication.update += Tick;
    }
    static void Set(string name, object value) => typeof(NxCloneWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
    static void Tick()
    {
        window.Repaint();
        if (++frames < 60) return;
        EditorApplication.update -= Tick;
        Debug.Log("NXCLONE_UI_CAPTURE_READY");
    }
}
