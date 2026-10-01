using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

public static class NxCloneMenuFilterSmoke
{
    public static void Run()
    {
        var created = new List<VRCExpressionsMenu>();
        Texture2D icon = null;
        try
        {
            VRCExpressionsMenu Menu(string name)
            {
                var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                menu.name = name;
                created.Add(menu);
                return menu;
            }

            var root = Menu("Avatar");
            var gogo = Menu("GoGo Loco helpers");
            var shared = Menu("Clothes and effects");
            var iconControl = new VRCExpressionsMenu.Control.Parameter { name = "wear_hat" };
            var activation = new VRCExpressionsMenu.Control.Parameter { name = "settings_open" };
            var speed = new VRCExpressionsMenu.Control.Parameter { name = "gogo_speed" };
            var puppets = new[] {
                new VRCExpressionsMenu.Control.Parameter { name = "puppet_x" },
                new VRCExpressionsMenu.Control.Parameter { name = "puppet_y" },
                new VRCExpressionsMenu.Control.Parameter { name = "puppet_z" }
            };
            icon = new Texture2D(2, 2);
            root.controls.Add(new VRCExpressionsMenu.Control {
                name = "GoGo Loco", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "root_gogo" }, value = 1
            });
            root.controls.Add(new VRCExpressionsMenu.Control {
                name = "GoGoLoco", type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "gogo_activation" }, subMenu = gogo
            });
            root.controls.Add(new VRCExpressionsMenu.Control {
                name = "Go Loco", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "go_loco_toggle" }, value = 1
            });
            root.controls.Add(new VRCExpressionsMenu.Control {
                name = "<b>GoGo Loco", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "richtext_open_tag" }
            });
            root.controls.Add(new VRCExpressionsMenu.Control {
                name = "<color=#ff00ff>GoGo Loco</color> ✨", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "richtext_color_tag" }
            });
            root.controls.Add(new VRCExpressionsMenu.Control {
                name = "Locomotion", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = shared
            });
            root.controls.Add(new VRCExpressionsMenu.Control {
                name = "Clothes", type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                parameter = activation, subMenu = shared
            });
            shared.controls.Add(new VRCExpressionsMenu.Control {
                name = "Hat", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                icon = icon, parameter = iconControl, value = 1
            });
            shared.controls.Add(new VRCExpressionsMenu.Control {
                name = "Speed", type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                parameter = speed, subParameters = puppets
            });
            shared.controls.Add(new VRCExpressionsMenu.Control {
                name = "Back", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = root
            });
            gogo.controls.Add(new VRCExpressionsMenu.Control {
                name = "Walk", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "walk_toggle" }
            });
            gogo.controls.Add(new VRCExpressionsMenu.Control {
                name = "Speed", type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "speed_activation" },
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = "speed_x" } }
            });

            using (var result = nxclone.NxCloneMenuFilter.CreateFilteredCopy(root))
            {
                var filtered = result.Menu;
                Assert(filtered && filtered != root, "Filter did not return an independent root menu.");
                Assert(filtered.controls.Select(control => control.name).SequenceEqual(new[] { "Locomotion", "Clothes" }),
                    "GoGo Loco labels remained or unrelated locomotion control was removed.");
                Assert(filtered.controls[0].subMenu == filtered.controls[1].subMenu,
                    "Shared submenu reference was not preserved.");
                var copiedShared = filtered.controls[0].subMenu;
                Assert(copiedShared.controls[2].subMenu == filtered, "Menu cycle did not point at copied root.");
                Assert(filtered.controls[1].parameter != activation && filtered.controls[1].parameter.name == "settings_open",
                    "Submenu activation parameter was not independently copied.");
                Assert(copiedShared.controls[0].icon == icon && copiedShared.controls[0].value == 1 &&
                    copiedShared.controls[0].parameter.name == "wear_hat", "Toggle icon, value, or parameter was lost.");
                Assert(copiedShared.controls[0].parameter != iconControl, "Toggle parameter still aliases source data.");
                var copiedPuppet = copiedShared.controls[1];
                Assert(copiedPuppet.parameter.name == "gogo_speed" && copiedPuppet.subParameters.Length == 3 &&
                    copiedPuppet.subParameters.Select(parameter => parameter.name)
                        .SequenceEqual(new[] { "puppet_x", "puppet_y", "puppet_z" }),
                    "Puppet activation or subparameters were not preserved.");
                Assert(copiedPuppet.subParameters[1] != puppets[1], "Puppet subparameter still aliases source data.");
                Assert(new[] { "root_gogo", "gogo_activation", "go_loco_toggle", "richtext_open_tag", "richtext_color_tag",
                    "walk_toggle", "speed_activation", "speed_x" }
                    .All(result.ExcludedParameters.Contains), "Excluded branch parameters are incomplete.");
                Assert(!result.ExcludedParameters.Contains("settings_open") &&
                    !result.ExcludedParameters.Contains("gogo_speed") && !result.ExcludedParameters.Contains("puppet_x"),
                    "Parameters shared with the kept tree were incorrectly excluded.");

                copiedShared.controls[0].parameter.name = "changed_copy";
                copiedPuppet.subParameters[0].name = "changed_puppet";
                Assert(iconControl.name == "wear_hat" && puppets[0].name == "puppet_x",
                    "Editing copied controls mutated source parameters.");
            }

            Assert(root.controls.Count == 7 && root.controls[0].parameter.name == "root_gogo" &&
                shared.controls[0].parameter.name == "wear_hat" && shared.controls[1].subParameters.Length == 3,
                "Filtering or disposing mutated source menu controls.");

            var namedBranch = Menu("GoGoLoco");
            namedBranch.controls.Add(new VRCExpressionsMenu.Control {
                name = "Unlabeled", parameter = new VRCExpressionsMenu.Control.Parameter { name = "named_branch_param" }
            });
            root.controls.Add(new VRCExpressionsMenu.Control {
                name = "Movement settings", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = namedBranch
            });
            using (var result = nxclone.NxCloneMenuFilter.CreateFilteredCopy(root))
                Assert(result.Menu.controls.All(control => control.name != "Movement settings") &&
                    result.ExcludedParameters.Contains("named_branch_param"),
                    "A submenu named GoGoLoco was not recognized and removed.");
        }
        finally
        {
            foreach (var menu in created) if (menu) UnityEngine.Object.DestroyImmediate(menu);
            if (icon) UnityEngine.Object.DestroyImmediate(icon);
        }
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
