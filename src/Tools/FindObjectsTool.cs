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
                "Finds all active GameObjects of a specific type in the scene. Returns a list with instance_id and name.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        class_name = new
                        {
                            type = "string",
                            description = "The full name of the class/type to search for (e.g., 'UnityEngine.GameObject', 'TMPro.TextMeshProUGUI')."
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

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                var type = UnityObjectExtensions.ResolveType(className);
                if (type == null) throw new Exception($"Type not found: {className}");

                var il2cppType = Il2CppType.From(type);
                var objects = GameObject.FindObjectsOfType(il2cppType);

                return objects.Select(obj => obj.ToMcpValue()).ToList();
            });
        }
    }
}
