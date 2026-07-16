// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
using Il2CppInterop.Runtime;

namespace PokeLege.UnityRuntimeMCP.Tools
{
    public static class FindObjectsTool
    {
        public static void Register()
        {
            McpServer.RegisterTool(
                "find_objects",
                Handle,
                "Finds GameObjects or assets of a specific type. Returns a list with instance_id, name, and type.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        class_name = new
                        {
                            type = "string",
                            description = "The full name of the class/type to search for (e.g., 'UnityEngine.GameObject', 'TMPro.TextMeshProUGUI')."
                        },
                        include_assets = new
                        {
                            type = "boolean",
                            description = "Optional: If true, searches all loaded assets, ScriptableObjects, and inactive objects in memory using Resources.FindObjectsOfTypeAll. Defaults to false."
                        }
                    },
                    required = new[] { "class_name" }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("class_name", out var classNameProp))
                throw new Exception("Missing parameter: class_name");

            string className = classNameProp.GetString();

            bool includeAssets = false;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("include_assets", out var includeAssetsProp))
            {
                if (includeAssetsProp.ValueKind == JsonValueKind.True || includeAssetsProp.ValueKind == JsonValueKind.False)
                {
                    includeAssets = includeAssetsProp.GetBoolean();
                }
            }

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                var type = UnityObjectExtensions.ResolveType(className);
                if (type == null) return new System.Collections.Generic.List<object>();

                var il2cppType = Il2CppType.From(type);
                UnityEngine.Object[] objects;
                
                if (includeAssets)
                {
                    objects = Resources.FindObjectsOfTypeAll(il2cppType);
                }
                else
                {
                    objects = GameObject.FindObjectsOfType(il2cppType);
                }

                return objects.Select(obj => obj.ToMcpValue()).ToList();
            });
        }
    }
}
