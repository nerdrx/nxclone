using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace nxclone
{
    /// <summary>Creates a temporary copy of an expression menu with GoGo Loco branches removed.</summary>
    public static class NxCloneMenuFilter
    {
        static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic);

        public sealed class Result : IDisposable
        {
            readonly List<VRCExpressionsMenu> copies;
            public VRCExpressionsMenu Menu { get; private set; }
            public IReadOnlyCollection<string> ExcludedParameters { get; }

            internal Result(VRCExpressionsMenu menu, List<VRCExpressionsMenu> copies, string[] excludedParameters)
            {
                Menu = menu;
                this.copies = copies;
                ExcludedParameters = excludedParameters;
            }

            public void Dispose()
            {
                if (copies == null) return;
                foreach (var copy in copies)
                    if (copy) UnityEngine.Object.DestroyImmediate(copy);
                copies.Clear();
                Menu = null;
            }
        }

        public static Result CreateFilteredCopy(VRCExpressionsMenu source)
        {
            var copies = new List<VRCExpressionsMenu>();
            var map = new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>();
            var removedParameters = new HashSet<string>(StringComparer.Ordinal);
            var keptParameters = new HashSet<string>(StringComparer.Ordinal);
            var menu = source ? CopyMenu(source, map, copies, removedParameters, keptParameters) : null;
            removedParameters.ExceptWith(keptParameters);
            return new Result(menu, copies, removedParameters.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }

        static VRCExpressionsMenu CopyMenu(VRCExpressionsMenu source,
            Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> copies,
            List<VRCExpressionsMenu> ownedCopies,
            HashSet<string> removedParameters,
            HashSet<string> keptParameters)
        {
            if (!source) return null;
            if (copies.TryGetValue(source, out var existing)) return existing;

            var copy = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            copy.name = source.name + " (nxclone filtered)";
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.controls = new List<VRCExpressionsMenu.Control>();
            copies.Add(source, copy);
            ownedCopies.Add(copy);

            bool removeWholeMenu = IsGoGoLocoName(source.name);
            foreach (var control in source.controls ?? new List<VRCExpressionsMenu.Control>())
            {
                bool remove = removeWholeMenu || IsGoGoLocoName(control.name) ||
                    control.subMenu && IsGoGoLocoName(control.subMenu.name);
                CollectControlParameters(control, remove ? removedParameters : keptParameters);
                if (remove)
                {
                    if (control.subMenu) CollectMenuParameters(control.subMenu, removedParameters, new HashSet<VRCExpressionsMenu>());
                    continue;
                }

                var copiedControl = CopyControl(control);
                copiedControl.subMenu = CopyMenu(control.subMenu, copies, ownedCopies, removedParameters, keptParameters);
                copy.controls.Add(copiedControl);
            }
            return copy;
        }

        static VRCExpressionsMenu.Control CopyControl(VRCExpressionsMenu.Control source)
        {
            var copy = (VRCExpressionsMenu.Control)CloneMethod.Invoke(source, null);
            copy.parameter = CopyParameter(source.parameter);
            copy.subParameters = source.subParameters == null
                ? null
                : source.subParameters.Select(CopyParameter).ToArray();

            // Preserve SDK-added serializable control data such as puppet labels while making its
            // arrays and entries independent from the source control.
            for (var type = typeof(VRCExpressionsMenu.Control); type != null && type != typeof(object); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.Name == "parameter" || field.Name == "subParameters" || field.Name == "subMenu") continue;
                    if (field.GetValue(source) is Array array)
                    {
                        var fieldCopy = (Array)array.Clone();
                        for (int i = 0; i < fieldCopy.Length; i++)
                            if (fieldCopy.GetValue(i) is object entry && !entry.GetType().IsValueType && entry is not string &&
                                !(entry is UnityEngine.Object))
                                fieldCopy.SetValue(CloneMethod.Invoke(entry, null), i);
                        field.SetValue(copy, fieldCopy);
                    }
                }
            return copy;
        }

        static VRCExpressionsMenu.Control.Parameter CopyParameter(VRCExpressionsMenu.Control.Parameter parameter)
        {
            if (parameter == null) return null;
            return (VRCExpressionsMenu.Control.Parameter)CloneMethod.Invoke(parameter, null);
        }

        static void CollectMenuParameters(VRCExpressionsMenu menu, HashSet<string> parameters,
            HashSet<VRCExpressionsMenu> visited)
        {
            if (!menu || !visited.Add(menu)) return;
            foreach (var control in menu.controls ?? new List<VRCExpressionsMenu.Control>())
            {
                CollectControlParameters(control, parameters);
                CollectMenuParameters(control.subMenu, parameters, visited);
            }
        }

        static void CollectControlParameters(VRCExpressionsMenu.Control control, HashSet<string> parameters)
        {
            AddParameter(control.parameter, parameters);
            if (control.subParameters != null)
                foreach (var parameter in control.subParameters) AddParameter(parameter, parameters);
        }

        static void AddParameter(VRCExpressionsMenu.Control.Parameter parameter, HashSet<string> parameters)
        {
            if (parameter != null && !string.IsNullOrEmpty(parameter.name)) parameters.Add(parameter.name);
        }

        public static bool IsGoGoLocoName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var plainName = new System.Text.StringBuilder(name.Length);
            bool inTag = false;
            foreach (char character in name)
            {
                if (character == '<') { inTag = true; continue; }
                if (inTag)
                {
                    if (character == '>') inTag = false;
                    continue;
                }
                plainName.Append(character);
            }
            var normalized = new string(plainName.ToString().Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            return normalized == "gogoloco" || normalized == "goloco" || normalized == "gogo";
        }
    }
}
