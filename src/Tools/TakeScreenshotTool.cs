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
                "Captures a screenshot of the current game view and returns it as a base64 encoded PNG. Note: Only PNG is supported because native JPEG encoding is buggy and crashes the game.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        scale = new
                        {
                            type = "number",
                            description = "Scale factor for the screenshot resolution, from 0.1 to 1.0 (default: 0.5 to reduce payload size)"
                        },
                        save_to_file = new
                        {
                            type = "boolean",
                            description = "If true, the screenshot is saved as a unique PNG file in a shared temp directory (C:\\Users\\Public\\UnityRuntimeMCP_Temp\\screenshot_<guid>.png) and the file path is returned. The caller is responsible for deleting the file after consumption."
                        }
                    }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            float scale = 0.5f;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("scale", out var scaleProp))
            {
                if (scaleProp.ValueKind == JsonValueKind.Number)
                {
                    scale = (float)scaleProp.GetDouble();
                }
            }

            bool saveToFile = false;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("save_to_file", out var saveToFileProp))
            {
                if (saveToFileProp.ValueKind == JsonValueKind.True || saveToFileProp.ValueKind == JsonValueKind.False)
                {
                    saveToFile = saveToFileProp.GetBoolean();
                }
            }

            // Clamp scale between 0.1f and 1.0f
            scale = Math.Max(0.1f, Math.Min(1.0f, scale));

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                try
                {
                    var screenWidth = Screen.width;
                    var screenHeight = Screen.height;
                    
                    int targetWidth = (int)(screenWidth * scale);
                    int targetHeight = (int)(screenHeight * scale);
                    
                    Debug.Log($"[UnityRuntimeMCP] Capturing screenshot: {screenWidth}x{screenHeight} scaled to {targetWidth}x{targetHeight} (scale: {scale})");

                    // 1. Capture screen pixels at native resolution
                    var fullTex = new Texture2D(screenWidth, screenHeight, TextureFormat.RGB24, false);
                    fullTex.ReadPixels(new Rect(0, 0, screenWidth, screenHeight), 0, 0);
                    fullTex.Apply();

                    byte[] bytes;

                    if (scale < 0.99f)
                    {
                        // 2. Downscale using a temporary RenderTexture and Blit
                        var rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.Default);
                        var activeRT = RenderTexture.active;
                        
                        Graphics.Blit(fullTex, rt);
                        
                        RenderTexture.active = rt;
                        var scaledTex = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                        scaledTex.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                        scaledTex.Apply();
                        
                        // Restore active render texture and release temp resource
                        RenderTexture.active = activeRT;
                        RenderTexture.ReleaseTemporary(rt);
                        
                        // Encode the scaled texture
                        bytes = ImageConversion.EncodeToPNG(scaledTex);
                        
                        // Clean up textures
                        UnityEngine.Object.Destroy(scaledTex);
                    }
                    else
                    {
                        // Encode full texture directly
                        bytes = ImageConversion.EncodeToPNG(fullTex);
                    }

                    UnityEngine.Object.Destroy(fullTex);
                    Debug.Log($"[UnityRuntimeMCP] Screenshot capture completed: {bytes.Length} bytes");

                    if (saveToFile)
                    {
                        string tempDir;
                        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                        {
                            tempDir = @"C:\Users\Public\UnityRuntimeMCP_Temp";
                        }
                        else
                        {
                            tempDir = "/tmp/UnityRuntimeMCP_Temp";
                        }

                        if (!System.IO.Directory.Exists(tempDir))
                        {
                            System.IO.Directory.CreateDirectory(tempDir);
                        }

                        string targetPath = System.IO.Path.Combine(tempDir, $"screenshot_{Guid.NewGuid()}.png");
                        System.IO.File.WriteAllBytes(targetPath, bytes);

                        Debug.Log($"[UnityRuntimeMCP] Screenshot saved to shared directory: {targetPath}");

                        return (object)new
                        {
                            file_path = targetPath
                        };
                    }

                    return (object)new
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
