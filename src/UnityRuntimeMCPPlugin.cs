// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using PokeLege.UnityRuntimeMCP.Tools;

namespace PokeLege.UnityRuntimeMCP
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class UnityRuntimeMCPPlugin : BasePlugin
    {
        public override void Load()
        {
            // Initialize Config
            PluginConfig.Init(Config);

            Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            // Register types for IL2CPP
            ClassInjector.RegisterTypeInIl2Cpp<McpMainThreadDispatcher>();
            Log.LogInfo("McpMainThreadDispatcher registered in IL2CPP.");

            // Initialize Dispatcher
            McpMainThreadDispatcher.Initialize(Log);
            var dispatcherGo = new GameObject("UnityRuntimeMCP_McpDispatcher");
            dispatcherGo.hideFlags = HideFlags.HideAndDontSave; // Protect from scene cleanup and object explorers
            
            Log.LogInfo($"Created GameObject: {dispatcherGo.name} (InstanceID: {dispatcherGo.GetInstanceID()})");
            
            var component = dispatcherGo.AddComponent<McpMainThreadDispatcher>();
            if (component == null)
            {
                Log.LogError("FAILED to add McpMainThreadDispatcher component!");
            }
            else
            {
                Log.LogInfo($"Successfully added McpMainThreadDispatcher component (InstanceID: {component.GetInstanceID()})");
            }
            
            Object.DontDestroyOnLoad(dispatcherGo);
            Log.LogInfo($"MainThreadDispatcher persistent GameObject state: Active={dispatcherGo.activeInHierarchy}, Scene={dispatcherGo.scene.name}, HideFlags={dispatcherGo.hideFlags}");

            // Initialize McpServer (Phase 2)
            McpServer.Start(PluginConfig.Host.Value, PluginConfig.Port.Value, Log);

            // Register Tools (Phase 3)
            FindObjectsTool.Register();
            InspectObjectTool.Register();
            ReadFieldTool.Register();
            WriteFieldTool.Register();
            InvokeMethodTool.Register();
            TakeScreenshotTool.Register();

            Log.LogInfo($"UnityRuntimeMCP initialized on port {PluginConfig.Port.Value}");
        }

        public override bool Unload()
        {
            McpServer.Stop();
            return base.Unload();
        }

    }

    public static class MyPluginInfo
    {
        public const string PLUGIN_GUID = "me.pokelege.unityruntimemcp";
        public const string PLUGIN_NAME = "UnityRuntimeMCP";
        public const string PLUGIN_VERSION = "1.0.0";
    }
}
