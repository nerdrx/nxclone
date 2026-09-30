using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Avatars.Components;

namespace nxclone
{
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
        public Vector3 offset = Vector3.zero;
        public Vector3 rotation = Vector3.zero;
        public Vector3 scale = Vector3.one;
        public bool mirror;
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
        public bool independentCloneFx;
        public bool deferParameterBudgetToVrcfury;
        public bool runtimeScale;
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
        public Color afterimageColor = new Color(0.25f, 0.8f, 1f, 0.3f);
        public string generatedFolder;
    }

}
