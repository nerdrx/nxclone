using System;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

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
        // Build after armature tools, before late parameter compression.
        public int callbackOrder => int.MaxValue - 200;

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

    public sealed class NxCloneDeferredBudgetValidation : IVRCSDKPreprocessAvatarCallback
    {
        // VRCFury's Parameter Compressor is max-100; run after it and before SDK validation.
        public int callbackOrder => int.MaxValue - 10;

        public bool OnPreprocessAvatar(GameObject avatar)
        {
            var marker = avatar ? avatar.GetComponent<NxCloneDeferredParameterBudget>() : null;
            if (!marker) return true;

            int cost = 0;
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if (descriptor && descriptor.customExpressions && descriptor.expressionParameters)
                cost = descriptor.expressionParameters.CalcTotalCost();

            UnityEngine.Object.DestroyImmediate(marker);
            if (cost <= VRCExpressionParameters.MAX_PARAMETER_COST) return true;

            Debug.LogError($"nxclone: VRCFury compression left the avatar at {cost}/{VRCExpressionParameters.MAX_PARAMETER_COST} synced parameter bits. Reduce or remove synced parameters before upload.", avatar);
            return false;
        }
    }
}
