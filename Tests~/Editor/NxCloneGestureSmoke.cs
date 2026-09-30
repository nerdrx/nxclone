using System;
using System.Linq;
using nxclone;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

public static class NxCloneGestureSmoke
{
    public static void Run()
    {
        const string folder = "Assets/nxclone-gesture-smoke";
        AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder("Assets", "nxclone-gesture-smoke");
        var root = new GameObject("gesture test");
        try
        {
            foreach (bool numeric in new[] { false, true })
            foreach (NxCloneGestureHand hand in Enum.GetValues(typeof(NxCloneGestureHand)))
            {
                var fx = AnimatorController.CreateAnimatorControllerAtPath($"{folder}/{hand}-{numeric}.controller");
                fx.AddParameter("visible", AnimatorControllerParameterType.Bool);
                if (numeric)
                {
                    fx.AddParameter("GestureLeft", AnimatorControllerParameterType.Float);
                    fx.AddParameter("GestureRight", AnimatorControllerParameterType.Float);
                    fx.AddParameter("IsLocal", AnimatorControllerParameterType.Float);
                }
                Assert(NxCloneGestureControls.Configure(fx, "visible", NxCloneGesture.Disabled, hand, "off") == null,
                    "Disabled gesture added a layer.");
                var name = NxCloneGestureControls.Configure(fx, "visible", NxCloneGesture.Fist, hand, "visibility");
                int layer = Array.FindIndex(fx.layers, item => item.name == name);
                var animator = root.GetComponent<Animator>();
                if (!animator) animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = fx;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetLayerWeight(layer, 1);
                SetInput(animator, numeric, "IsLocal", 0);
                SetInput(animator, numeric, "GestureLeft", 1);
                SetInput(animator, numeric, "GestureRight", 1);
                Tick(animator);
                Assert(State(animator, layer, "released off"), "Remote gestures triggered commands.");
                SetInput(animator, numeric, "IsLocal", 1);
                Tick(animator);
                Assert(State(animator, layer, "pressed on"), "Local press did not toggle on.");
                ApplyDriver(fx, animator, layer, "pressed on", 1);
                Tick(animator);
                Assert(State(animator, layer, "pressed on"), "Held gesture repeated a command.");
                if (hand == NxCloneGestureHand.Either || hand == NxCloneGestureHand.Both)
                {
                    SetInput(animator, numeric, "GestureLeft", 0);
                    Tick(animator);
                    Assert(State(animator, layer, hand == NxCloneGestureHand.Either ? "pressed on" : "released on"),
                        "Either/Both release semantics differ from their press semantics.");
                }
                SetInput(animator, numeric, "GestureLeft", 0);
                SetInput(animator, numeric, "GestureRight", 0);
                Tick(animator);
                Assert(State(animator, layer, "released on"), "Release did not rearm gesture.");
                SetInput(animator, numeric, "GestureLeft", 1);
                SetInput(animator, numeric, "GestureRight", 1);
                Tick(animator);
                Assert(State(animator, layer, "pressed off"), "Second press did not toggle off.");
                ApplyDriver(fx, animator, layer, "pressed off", 0);
                SetInput(animator, numeric, "GestureLeft", 0);
                SetInput(animator, numeric, "GestureRight", 0);
                Tick(animator);
                animator.SetBool("visible", true);
                Tick(animator);
                Assert(State(animator, layer, "released on"), "Menu changes did not update the gesture toggle state.");
            }
            Debug.Log("NXCLONE_GESTURE_SMOKE_OK: native FSM; SDK parameter-driver schema and explicitly mirrored writes");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(folder);
        }
    }

    static void ApplyDriver(AnimatorController fx, Animator animator, int layer, string state, float expected)
    {
        // The Unity editor does not execute VRChat parameter drivers. Check their real
        // SDK schema, then mirror the write to exercise the native Animator handshake.
        var driver = fx.layers[layer].stateMachine.states.Single(s => s.state.name == state)
            .state.behaviours.OfType<VRCAvatarParameterDriver>().Single();
        var command = driver.parameters.Single();
        Assert(driver.localOnly && command.name == "visible" && command.value == expected &&
               command.type == VRC_AvatarParameterDriver.ChangeType.Set, "Wrong SDK gesture command.");
        animator.SetBool(command.name, command.value > 0);
    }

    static void SetInput(Animator animator, bool numeric, string name, int value)
    {
        if (numeric) animator.SetFloat(name, value);
        else if (name == "IsLocal") animator.SetBool(name, value != 0);
        else animator.SetInteger(name, value);
    }
    static bool State(Animator animator, int layer, string state) => animator.GetCurrentAnimatorStateInfo(layer).IsName(state);
    static void Tick(Animator animator) { for (int i = 0; i < 5; i++) animator.Update(0.05f); }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
