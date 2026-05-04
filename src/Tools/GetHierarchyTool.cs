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
                "Returns the immediate parent and all children of a specific Unity object.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "The unique instance ID of the GameObject or Transform to inspect." }
                    },
                    required = new[] { "instance_id" }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("instance_id", out var idProp))
                throw new Exception("Missing parameter: instance_id");

            int instanceId = idProp.GetInt32();

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                var obj = UnityObjectExtensions.FindObjectById(instanceId);
                if (obj == null) throw new Exception($"Object with ID {instanceId} not found.");

                var typedObj = obj.CastToRuntimeType();
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
