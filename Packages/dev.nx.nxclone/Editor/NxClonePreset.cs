using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace nxclone
{
    public enum NxAttachPoint { Root, Head, Chest, Hips, LeftHand, RightHand, LeftFoot, RightFoot }

    [Serializable]
    public sealed class NxCloneSlot
    {
        [NonSerialized] public VRCAvatarDescriptor source;
        [NonSerialized] public Transform anchor;
        public Vector3 offset = Vector3.zero;
        public Vector3 rotation = Vector3.zero;
        public Vector3 scale = Vector3.one;
        public bool mirror;
        public NxAttachPoint attachTo;
        public bool contactAnchor;
        public string contactTag = "HandL";
        public bool contactAllowSelf;
        public bool contactAllowOthers = true;
    }

    [CreateAssetMenu(menuName = "nxclone/Preset", fileName = "NxClonePreset")]
    public sealed class NxClonePreset : ScriptableObject
    {
        public List<NxCloneSlot> slots = new List<NxCloneSlot> { new NxCloneSlot() };
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
        [Range(0.05f, 0.8f)] public float afterimageFollowStrength = 0.25f;
        public Color afterimageColor = new Color(0.25f, 0.8f, 1f, 0.3f);

        void OnValidate()
        {
            if (slots == null) slots = new List<NxCloneSlot>();
            if (slots.Count > 4) slots.RemoveRange(4, slots.Count - 4);
            afterimageCount = Mathf.Clamp(afterimageCount, 1, 4);
            afterimageFollowStrength = Mathf.Clamp(afterimageFollowStrength, 0.05f, 0.8f);
        }
    }
}
