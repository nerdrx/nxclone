using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace nxclone
{
    // Drivers remain active when their visual children are hidden.
    public static class NxClonePlacement
    {
        public static void FreezeFrame(Transform frame)
        {
            var inherited = frame.lossyScale;
            frame.localScale = new Vector3(1f / inherited.x, 1f / inherited.y, 1f / inherited.z);
            var pose = frame.gameObject.AddComponent<VRCParentConstraint>();
            pose.ActivateConstraint();
            pose.FreezeToWorld = true;
            pose.ApplyConfigurationChanges();
            var scale = frame.gameObject.AddComponent<VRCScaleConstraint>();
            scale.ActivateConstraint();
            scale.FreezeToWorld = true;
            scale.ApplyConfigurationChanges();
        }

        public static Transform Follow(Transform frame, Transform anchor, string name, Vector3 offset, Vector3 rotation)
        {
            var target = new GameObject("nxclone anchor " + name).transform;
            target.SetParent(anchor, false);
            target.localPosition = offset;
            target.localRotation = Quaternion.Euler(rotation);
            var driver = new GameObject(name).transform;
            driver.SetParent(frame, false);
            driver.SetPositionAndRotation(target.position, target.rotation);
            driver.localScale = target.lossyScale;
            var pose = driver.gameObject.AddComponent<VRCParentConstraint>();
            pose.Sources.Add(new VRCConstraintSource(target, 1f));
            pose.ActivateConstraint();
            pose.ApplyConfigurationChanges();
            var scale = driver.gameObject.AddComponent<VRCScaleConstraint>();
            scale.Sources.Add(new VRCConstraintSource(target, 1f));
            scale.ActivateConstraint();
            scale.ApplyConfigurationChanges();
            return driver;
        }

        public static Transform Delay(Transform frame, Transform source, string name, float weight)
        {
            var driver = new GameObject(name).transform;
            driver.SetParent(frame, false);
            driver.SetPositionAndRotation(source.position, source.rotation);
            driver.localScale = source.lossyScale;
            var pose = driver.gameObject.AddComponent<VRCParentConstraint>();
            pose.Sources.Add(new VRCConstraintSource(driver, 1f));
            pose.Sources.Add(new VRCConstraintSource(source, weight));
            pose.ActivateConstraint();
            pose.ApplyConfigurationChanges();
            var scale = driver.gameObject.AddComponent<VRCScaleConstraint>();
            scale.Sources.Add(new VRCConstraintSource(source, 1f));
            scale.ActivateConstraint();
            scale.ApplyConfigurationChanges();
            return driver;
        }
    }
}
