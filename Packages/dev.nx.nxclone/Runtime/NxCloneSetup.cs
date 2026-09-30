using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Avatars.Components;

namespace nxclone
{
    public enum NxCloneAttachPoint
    {
        Root, Head, Chest, Hips, LeftHand, RightHand, LeftFoot, RightFoot
    }

    [Serializable]
    public sealed class NxCloneSetupSlot
    {
        [Tooltip("Leave empty to use the avatar carrying this setup component.")]
        public VRCAvatarDescriptor source;
        public Vector3 offset = new Vector3(0.8f, 0f, 0f);
        public Vector3 scale = Vector3.one;
        public bool mirror;
        public NxCloneAttachPoint attachTo;
    }

    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class NxCloneSetup : MonoBehaviour, IEditorOnly
    {
        public List<NxCloneSetupSlot> slots = new List<NxCloneSetupSlot> { new NxCloneSetupSlot() };
        public bool worldDrop;
        public bool poseFreeze;
        public bool copyVisemes = true;
        public bool copyFxAnimations = true;
        public bool runtimeScale;
        public bool afterimages;
        [Range(1, 4)] public int afterimageCount = 1;
        [Range(0.05f, 0.8f)] public float damping = 0.25f;
        public Color afterimageColor = new Color(0.25f, 0.8f, 1f, 0.3f);
        public string generatedFolder;
    }
}
