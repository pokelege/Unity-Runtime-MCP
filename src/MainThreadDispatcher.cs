// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using BepInEx.Logging;

namespace PokeLege.UnityRuntimeMCP
{
    public class McpMainThreadDispatcher : MonoBehaviour
    {
        public McpMainThreadDispatcher(IntPtr ptr) : base(ptr) { }

        private static readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();
        private static ManualLogSource _logger;
        private static bool _initialized;
        private static McpMainThreadDispatcher _instance;
        private static bool _firstUpdateLogged = false;
        private static SynchronizationContext _unityContext;

        public static void Initialize(ManualLogSource logger = null)
        {
            _logger = logger;
            if (_initialized) return;
            
            if (_logger != null) _logger.LogInfo("McpMainThreadDispatcher static initialization called.");
            _initialized = true;
        }

        public void Awake()
        {
            _instance = this;
            _unityContext = SynchronizationContext.Current;
            if (_logger != null) 
            {
                _logger.LogInfo("McpMainThreadDispatcher Awake called.");
                _logger.LogInfo($"McpMainThreadDispatcher SyncContext captured in Awake: {_unityContext?.GetType().Name ?? "null"}");
            }
        }

        public void OnEnable()
        {
            if (_logger != null) _logger.LogInfo("McpMainThreadDispatcher OnEnable called.");
        }

        public void OnDisable()
        {
            if (_logger != null) 
            {
                _logger.LogWarning($"McpMainThreadDispatcher OnDisable called. Scene: {gameObject.scene.name}, Time: {Time.time}");
                _logger.LogDebug(Environment.StackTrace);
            }
        }

        public void OnDestroy()
        {
            if (_logger != null) 
            {
                _logger.LogWarning($"McpMainThreadDispatcher OnDestroy called! Scene: {gameObject.scene.name}, Time: {Time.time}");
                _logger.LogDebug(Environment.StackTrace);
            }
            if (_instance == this) _instance = null;
        }

        public void Start()
        {
            if (_logger != null) _logger.LogInfo("McpMainThreadDispatcher Start called.");
        }

        public void Update()
        {
            if (!_firstUpdateLogged)
            {
                if (_logger != null) _logger.LogInfo("McpMainThreadDispatcher first Update loop hit.");
                _firstUpdateLogged = true;
            }
            ProcessQueue();
        }

        public void FixedUpdate()
        {
            ProcessQueue();
        }

        public void LateUpdate()
        {
            ProcessQueue();
        }

        public static Task<T> EnqueueAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();
            
            Action wrappedAction = () =>
            {
                try
                {
                    if (_logger != null) _logger.LogDebug("Dispatcher: Executing enqueued function on main thread");
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.LogError($"Dispatcher: Error in main thread function: {ex}");
                    tcs.SetException(ex);
                }
            };

            if (_unityContext != null && Thread.CurrentThread.ManagedThreadId != 1) // Simple check for main thread
            {
                _unityContext.Post(_ => wrappedAction(), null);
            }
            else
            {
                _executionQueue.Enqueue(wrappedAction);
            }
            
            return tcs.Task;
        }

        public static Task EnqueueAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            Action wrappedAction = () =>
            {
                try
                {
                    if (_logger != null) _logger.LogDebug("Dispatcher: Executing enqueued action on main thread");
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.LogError($"Dispatcher: Error in main thread action: {ex}");
                    tcs.SetException(ex);
                }
            };

            if (_unityContext != null && Thread.CurrentThread.ManagedThreadId != 1)
            {
                _unityContext.Post(_ => wrappedAction(), null);
            }
            else
            {
                _executionQueue.Enqueue(wrappedAction);
            }

            return tcs.Task;
        }

        public static void ProcessQueue()
        {
            if (!_executionQueue.IsEmpty)
            {
                int count = _executionQueue.Count;
                while (count > 0 && _executionQueue.TryDequeue(out var action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        if (_logger != null) _logger.LogError($"Error executing main thread action: {ex}");
                    }
                    count--;
                }
            }
        }
    }
}
