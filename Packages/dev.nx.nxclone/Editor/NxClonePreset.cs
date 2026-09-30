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
        public Vector3 offset = new Vector3(0.8f, 0f, 0f);
        public Vector3 scale = Vector3.one;
        public bool mirror;
        public NxAttachPoint attachTo;
    }

    [CreateAssetMenu(menuName = "nxclone/Preset", fileName = "NxClonePreset")]
    public sealed class NxClonePreset : ScriptableObject
    {
        public List<NxCloneSlot> slots = new List<NxCloneSlot> { new NxCloneSlot() };
        public bool worldDrop;
        public bool poseFreeze;
        public bool copyVisemes = true;
        public bool copyFxAnimations = true;
        public bool runtimeScale;
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
