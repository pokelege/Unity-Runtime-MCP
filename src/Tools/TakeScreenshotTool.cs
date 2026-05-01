// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace PokeLege.UnityRuntimeMCP.Tools
{
    public static class TakeScreenshotTool
    {
        public static void Register()
        {
            McpServer.RegisterTool(
                "take_screenshot",
                Handle,
                "Captures a screenshot of the current game view and returns it as a base64 encoded PNG.",
                new
                {
                    type = "object",
                    properties = new { }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                try
                {
                    var width = Screen.width;
                    var height = Screen.height;
                    Debug.Log($"[UnityRuntimeMCP] Capturing screenshot: {width}x{height}");

                    var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    Debug.Log("[UnityRuntimeMCP] Created Texture2D");

                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    Debug.Log("[UnityRuntimeMCP] ReadPixels completed");

                    tex.Apply();
                    Debug.Log("[UnityRuntimeMCP] tex.Apply completed");

                    var bytes = ImageConversion.EncodeToPNG(tex);
                    Debug.Log($"[UnityRuntimeMCP] EncodeToPNG completed: {bytes.Length} bytes");

                    UnityEngine.Object.Destroy(tex);

                    return new
                    {
                        format = "png",
                        base64 = Convert.ToBase64String(bytes)
                    };
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UnityRuntimeMCP] Error in TakeScreenshotTool: {ex}");
                    throw;
                }
            });
        }
    }
}
