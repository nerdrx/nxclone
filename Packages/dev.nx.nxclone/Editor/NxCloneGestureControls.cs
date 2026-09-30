using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace nxclone
{
    public static class NxCloneGestureControls
    {
        // SDK parameter drivers run in VRChat; a held gesture produces exactly one command.
        public static string Configure(AnimatorController fx, string target, NxCloneGesture gesture,
            NxCloneGestureHand hand, string key)
        {
            if (gesture == NxCloneGesture.Disabled) return null;
            if (!Enum.IsDefined(typeof(NxCloneGesture), gesture) || !Enum.IsDefined(typeof(NxCloneGestureHand), hand))
                throw new ArgumentException("Choose a valid hand and VRChat gesture.");
            Require(fx, target, AnimatorControllerParameterType.Bool, false);
            RequireBuiltin(fx, "GestureLeft", AnimatorControllerParameterType.Int);
            RequireBuiltin(fx, "GestureRight", AnimatorControllerParameterType.Int);
            RequireBuiltin(fx, "IsLocal", AnimatorControllerParameterType.Bool);
            string name = "nxclone gesture " + key;
            while (fx.layers.Any(layer => layer.name == name)) name += "_";
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, fx);
            var off = State(machine, "released off");
            var on = State(machine, "released on");
            var pressOn = State(machine, "pressed on");
            var pressOff = State(machine, "pressed off");
            machine.defaultState = off;
            Driver(pressOn, target, 1);
            Driver(pressOff, target, 0);
            Transition(off, on).AddCondition(AnimatorConditionMode.If, 0, target);
            Transition(on, off).AddCondition(AnimatorConditionMode.IfNot, 0, target);
            Press(fx, off, pressOn, target, false, gesture, hand);
            Press(fx, on, pressOff, target, true, gesture, hand);
            Release(fx, pressOn, on, gesture, hand);
            Release(fx, pressOff, off, gesture, hand);
            fx.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1, stateMachine = machine });
            EditorUtility.SetDirty(fx);
            return name;
        }

        static AnimatorState State(AnimatorStateMachine machine, string name)
        {
            var state = machine.AddState(name);
            state.writeDefaultValues = false;
            return state;
        }

        static void Driver(AnimatorState state, string target, float value)
        {
            var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            driver.localOnly = true;
            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter {
                name = target, type = VRC_AvatarParameterDriver.ChangeType.Set, value = value
            });
        }

        static AnimatorStateTransition Transition(AnimatorState source, AnimatorState destination)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = false;
            transition.duration = 0;
            return transition;
        }

        static void Press(AnimatorController fx, AnimatorState source, AnimatorState destination, string target, bool current,
            NxCloneGesture gesture, NxCloneGestureHand hand)
        {
            foreach (var inputs in Inputs(hand, true))
            {
                var transition = Transition(source, destination);
                var localType = fx.parameters.Single(p => p.name == "IsLocal").type;
                transition.AddCondition(localType == AnimatorControllerParameterType.Bool ? AnimatorConditionMode.If : AnimatorConditionMode.Greater,
                    localType == AnimatorControllerParameterType.Float ? 0.5f : 0, "IsLocal");
                transition.AddCondition(current ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, target);
                foreach (var input in inputs)
                {
                    if (fx.parameters.Single(p => p.name == input).type == AnimatorControllerParameterType.Float)
                    {
                        transition.AddCondition(AnimatorConditionMode.Greater, (int)gesture - 0.5f, input);
                        transition.AddCondition(AnimatorConditionMode.Less, (int)gesture + 0.5f, input);
                    }
                    else transition.AddCondition(AnimatorConditionMode.Equals, (int)gesture, input);
                }
            }
        }

        static void Release(AnimatorController fx, AnimatorState source, AnimatorState destination,
            NxCloneGesture gesture, NxCloneGestureHand hand)
        {
            foreach (var inputs in Inputs(hand, false))
            {
                var alternatives = new List<List<AnimatorCondition>> { new List<AnimatorCondition>() };
                foreach (var input in inputs)
                {
                    var mismatch = fx.parameters.Single(p => p.name == input).type == AnimatorControllerParameterType.Float
                        ? new[] {
                            new AnimatorCondition { mode = AnimatorConditionMode.Less, threshold = (int)gesture - 0.5f, parameter = input },
                            new AnimatorCondition { mode = AnimatorConditionMode.Greater, threshold = (int)gesture + 0.5f, parameter = input }
                        }
                        : new[] { new AnimatorCondition { mode = AnimatorConditionMode.NotEqual, threshold = (int)gesture, parameter = input } };
                    alternatives = alternatives.SelectMany(group => mismatch.Select(condition => group.Append(condition).ToList())).ToList();
                }
                foreach (var conditions in alternatives)
                {
                    var transition = Transition(source, destination);
                    foreach (var condition in conditions) transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
                }
            }
        }

        static void RequireBuiltin(AnimatorController fx, string name, AnimatorControllerParameterType fallback)
        {
            var parameter = fx.parameters.FirstOrDefault(p => p.name == name);
            if (parameter == null) { fx.AddParameter(name, fallback); return; }
            // VRCFury's optimized controllers can represent VRChat integer/bool inputs as floats.
            if (parameter.type == AnimatorControllerParameterType.Float || parameter.type == fallback ||
                name == "IsLocal" && parameter.type == AnimatorControllerParameterType.Int) return;
            throw new InvalidOperationException($"Gesture input '{name}' has unsupported type {parameter.type}.");
        }

        static string[][] Inputs(NxCloneGestureHand hand, bool press)
        {
            if (hand == NxCloneGestureHand.Left) return new[] { new[] { "GestureLeft" } };
            if (hand == NxCloneGestureHand.Right) return new[] { new[] { "GestureRight" } };
            bool separate = press ? hand == NxCloneGestureHand.Either : hand == NxCloneGestureHand.Both;
            return separate ? new[] { new[] { "GestureLeft" }, new[] { "GestureRight" } }
                : new[] { new[] { "GestureLeft", "GestureRight" } };
        }

        static void Require(AnimatorController fx, string name, AnimatorControllerParameterType type, bool create)
        {
            if (!fx) throw new ArgumentNullException(nameof(fx));
            var parameter = fx.parameters.FirstOrDefault(p => p.name == name);
            if (parameter == null && create) { fx.AddParameter(name, type); return; }
            if (parameter == null || parameter.type != type)
                throw new InvalidOperationException($"Gesture controls require '{name}' with type {type}. Fix its FX parameter definition.");
        }
    }
}
