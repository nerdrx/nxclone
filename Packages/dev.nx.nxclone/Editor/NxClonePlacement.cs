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

        // A live world-space root proxy becomes relative motion inside the captured origin.
        // Copying its local pose into a rotated frame maps movement into the clone's facing axes.
        public static Transform Dance(Transform frame, Transform avatarRoot, string name, Vector3 offset, Vector3 rotation)
        {
            var reference = new GameObject("__nxclone dance origin " + name).transform;
            reference.SetParent(frame, false);
            reference.SetPositionAndRotation(avatarRoot.position, avatarRoot.rotation);
            var capture = reference.gameObject.AddComponent<VRCParentConstraint>();
            capture.Sources.Add(new VRCConstraintSource(avatarRoot, 1f));
            capture.ActivateConstraint();
            capture.ApplyConfigurationChanges();

            var live = new GameObject("__nxclone dance live").transform;
            live.SetParent(reference, false);
            var track = live.gameObject.AddComponent<VRCParentConstraint>();
            track.Sources.Add(new VRCConstraintSource(avatarRoot, 1f));
            track.ActivateConstraint();
            track.ApplyConfigurationChanges();

            var anchor = new GameObject("nxclone anchor " + name).transform;
            anchor.SetParent(reference, false);
            anchor.localPosition = offset;
            anchor.localRotation = Quaternion.Euler(rotation);
            var motion = new GameObject("__nxclone dance motion").transform;
            motion.SetParent(anchor, false);
            var relative = motion.gameObject.AddComponent<VRCParentConstraint>();
            relative.Sources.Add(new VRCConstraintSource(live, 1f));
            relative.SolveInLocalSpace = true;
            relative.ActivateConstraint();
            relative.ApplyConfigurationChanges();

            var driver = new GameObject(name).transform;
            driver.SetParent(frame, false);
            driver.SetPositionAndRotation(motion.position, motion.rotation);
            var pose = driver.gameObject.AddComponent<VRCParentConstraint>();
            pose.Sources.Add(new VRCConstraintSource(motion, 1f));
            pose.ActivateConstraint();
            pose.ApplyConfigurationChanges();
            var scale = driver.gameObject.AddComponent<VRCScaleConstraint>();
            scale.Sources.Add(new VRCConstraintSource(avatarRoot, 1f));
            scale.ActivateConstraint();
            scale.ApplyConfigurationChanges();
            return driver;
        }

        static Transform DanceMotion(Transform driver)
        {
            var pose = driver.GetComponent<VRCParentConstraint>();
            if (!pose || pose.Sources.Count == 0) return null;
            var source = pose.Sources[0].SourceTransform;
            while (source && source.name.StartsWith("__nxclone rotation ", System.StringComparison.Ordinal))
                source = source.parent;
            return source && source.name == "__nxclone dance motion" ? source : null;
        }

        public static Transform ControlAnchor(Transform driver)
        {
            var motion = DanceMotion(driver);
            if (!motion) return driver.GetComponent<VRCParentConstraint>().Sources[0].SourceTransform;
            var anchor = motion.parent;
            while (anchor && anchor.name.StartsWith("__nxclone rotation ", System.StringComparison.Ordinal))
                anchor = anchor.parent;
            return anchor;
        }

        public static Transform DanceReference(Transform driver)
        {
            var motion = DanceMotion(driver);
            return motion ? ControlAnchor(driver).parent : null;
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
