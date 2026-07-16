// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
using PokeLege.UnityRuntimeMCP;
using Object = UnityEngine.Object;

namespace PokeLege.UnityRuntimeMCP.Tools
{
    public static class GetHierarchyTool
    {
        public static void Register()
        {
            McpServer.RegisterTool(
                "get_hierarchy",
                Handle,
                "Returns the immediate parent and all children of a specific Unity object. If instance_id is omitted or 0, returns the root GameObjects of all active scenes.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "Optional: The unique instance ID of the GameObject or Transform to inspect. If omitted or 0, returns root GameObjects." }
                    }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            int instanceId = 0;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("instance_id", out var idProp))
            {
                if (idProp.ValueKind == JsonValueKind.Number)
                {
                    instanceId = idProp.GetInt32();
                }
            }

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                if (instanceId == 0)
                {
                    var roots = new List<object>();
                    var sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
                    for (int i = 0; i < sceneCount; i++)
                    {
                        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                        if (scene.isLoaded)
                        {
                            try
                            {
                                var sceneRoots = scene.GetRootGameObjects();
                                foreach (var root in sceneRoots)
                                {
                                    if (root != null)
                                    {
                                        roots.Add(root.ToMcpValue());
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // Handle scenes that might fail to get root game objects
                            }
                        }
                    }
                    return new
                    {
                        instance_id = 0,
                        name = "Active Scenes Roots",
                        parent = (object)null,
                        children = roots
                    };
                }

                var obj = UnityObjectExtensions.FindObjectById(instanceId);
                if (obj == null) throw new Exception($"Object with ID {instanceId} not found.");

                if (!(obj is UnityEngine.Object unityObj))
                    throw new Exception($"Object with ID {instanceId} is not a Unity Object.");

                var typedObj = unityObj.CastToRuntimeType();
                Transform transform = null;

                if (typedObj is GameObject gameObject)
                {
                    transform = gameObject.transform;
                }
                else if (typedObj is Component component)
                {
                    transform = component.transform;
                }

                if (transform == null) throw new Exception($"Object with ID {instanceId} does not have a Transform.");

                var parent = transform.parent;
                var children = new List<object>();
                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    children.Add(child.gameObject.ToMcpValue());
                }

                return new
                {
                    instance_id = transform.gameObject.GetInstanceID(),
                    name = transform.gameObject.name,
                    parent = parent?.gameObject.ToMcpValue(),
                    children = children
                };
            });
        }
    }
}
