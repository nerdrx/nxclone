using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Avatars.Components;

namespace nxclone
{
    [Flags]
    public enum NxCloneAxes { None = 0, X = 1, Y = 2, Z = 4, All = X | Y | Z }

    public enum NxCloneMovement { Attached, DancePartner }

    public enum NxCloneWriteDefaults { Auto, Off, On }

    public enum NxCloneAttachPoint
    {
        Root, Head, Chest, Hips, LeftHand, RightHand, LeftFoot, RightFoot
    }

    [Serializable]
    public sealed class NxCloneSetupSlot
    {
        [Tooltip("Leave empty to use the avatar carrying this setup component.")]
        public VRCAvatarDescriptor source;
        public Transform anchor;
        public Vector3 offset = Vector3.forward;
        public Vector3 rotation = new Vector3(0f, 180f, 0f);
        public Vector3 scale = Vector3.one;
        public bool mirror;
        public NxCloneMovement movement = NxCloneMovement.DancePartner;
        public NxCloneAttachPoint attachTo;
        public bool contactAnchor;
        public string contactTag = "HandL";
        public bool contactAllowSelf;
        public bool contactAllowOthers = true;
    }

    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class NxCloneSetup : MonoBehaviour, IEditorOnly
    {
        public List<NxCloneSetupSlot> slots = new List<NxCloneSetupSlot> { new NxCloneSetupSlot() };
        public NxCloneWriteDefaults writeDefaults;
        public bool worldDrop;
        public bool poseFreeze;
        public bool copyVisemes = true;
        public bool copyFxAnimations = true;
        public bool independentCloneFx = true;
        public bool excludeGoGoLoco = true;
        public bool deferParameterBudgetToVrcfury;
        public bool runtimeScale;
        public bool runtimePosition;
        [Tooltip("X: left/right, Y: up/down, Z: forward/back distance.")]
        public NxCloneAxes positionAxes = NxCloneAxes.Z;
        public bool runtimeRotation;
        [Tooltip("X: pitch, Y: yaw, Z: roll.")]
        public NxCloneAxes rotationAxes = NxCloneAxes.Y;
        public bool posing;
        public bool limbIk;
        public bool limbContacts;
        public bool wear;
        public bool recording;
        [Range(2, 15)] public int recordingSamples = 8;
        [Range(1f, 30f)] public float recordingDuration = 5f;
        public NxCloneGesture cloneGesture = NxCloneGesture.Disabled;
        public NxCloneGestureHand cloneGestureHand;
        public NxCloneGesture afterimageGesture = NxCloneGesture.Disabled;
        public NxCloneGestureHand afterimageGestureHand = NxCloneGestureHand.Right;
        public bool afterimages;
        [Range(1, 4)] public int afterimageCount = 1;
        [Range(0.05f, 0.8f)] public float damping = 0.25f;
        public bool distinctAfterimageColors = true;
        public List<Color> afterimageColors = new List<Color>();
        public Color afterimageColor = new Color(0.25f, 0.8f, 1f, 0.3f);
        public string generatedFolder;
    }

}
