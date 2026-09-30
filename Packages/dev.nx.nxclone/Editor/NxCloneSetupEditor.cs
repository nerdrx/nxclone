using UnityEditor;
using UnityEngine;

namespace nxclone
{
    [CustomEditor(typeof(NxCloneSetup))]
    public sealed class NxCloneSetupEditor : Editor
    {
        bool preview;
        int selected;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var setup = (NxCloneSetup)target;
            preview = EditorGUILayout.Toggle("Show clone preview", preview);
            if (setup.slots.Count > 0)
                selected = EditorGUILayout.IntSlider("Placement handle", selected + 1, 1, setup.slots.Count) - 1;
            var frame = setup.transform.Find("nxclone/world");
            if (frame)
                for (int i = 0; i < setup.slots.Count; i++)
                {
                    var visual = frame.Find($"placement-{i + 1}/clone-{i + 1}");
                    if (visual) visual.gameObject.SetActive(preview);
                }
            EditorGUILayout.HelpBox("Scene handles edit the selected clone's anchor offset and rotation. Preview visibility is editor only; uploaded clones start hidden.", MessageType.Info);
        }

        void OnSceneGUI()
        {
            var setup = (NxCloneSetup)target;
            if (selected < 0 || selected >= setup.slots.Count) return;
            var marker = setup.GetComponentsInChildren<Transform>(true);
            Transform anchor = null;
            foreach (var child in marker)
                if (child.name == $"nxclone anchor placement-{selected + 1}") { anchor = child; break; }
            if (!anchor || !anchor.parent) return;
            var slot = setup.slots[selected];
            anchor.localPosition = slot.offset;
            anchor.localRotation = Quaternion.Euler(slot.rotation);
            EditorGUI.BeginChangeCheck();
            var position = Handles.PositionHandle(anchor.position, anchor.rotation);
            var rotation = Handles.RotationHandle(anchor.rotation, position);
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(setup, "Move nxclone placement");
            slot.offset = anchor.parent.InverseTransformPoint(position);
            slot.rotation = (Quaternion.Inverse(anchor.parent.rotation) * rotation).eulerAngles;
            anchor.SetPositionAndRotation(position, rotation);
            EditorUtility.SetDirty(setup);
        }
    }
}
