using System;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace nxclone
{
    // Preview meshes must not participate in other tools' armature/mesh optimization.
    public sealed class NxClonePreviewCleanup : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 100;
        public bool OnPreprocessAvatar(GameObject avatar)
        {
            if (avatar && avatar.GetComponent<NxCloneSetup>()) NxCloneWindow.RemovePreview(avatar.transform);
            return true;
        }
    }

    public sealed class NxClonePreprocessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 50;

        public bool OnPreprocessAvatar(GameObject avatar)
        {
            var setup = avatar ? avatar.GetComponent<NxCloneSetup>() : null;
            if (!setup) return true;

            try
            {
                NxCloneWindow.BuildForUpload(setup);
                UnityEngine.Object.DestroyImmediate(setup);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e, avatar);
                return false;
            }
        }
    }
}
