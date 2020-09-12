using System;
using System.Collections.Generic;
using Vellum.Extension;

namespace Vellum.Automation
{
    public class Watchdog : IPlugin
    {
        public uint RetryLimit = 3;
        private uint _failRetryCount = 0;

        #region PLUGIN
        public IHost Host;
        public Version Version { get; }
        public PluginType PluginType { get { return PluginType.INTERNAL; } }
        private Dictionary<byte, IPlugin.HookHandler> _hookCallbacks = new Dictionary<byte, IPlugin.HookHandler>();
        public enum Hook
        {
            CRASH,
            RETRY,
            LIMIT_REACHED,
            STABLE,
        }

        public void Initialize(IHost host)
        {
            Host = host;
        }

        public void Unload()
        {
        }

        public Dictionary<byte, string> GetHooks()
        {
            Dictionary<byte, string> hooks = new Dictionary<byte, string>();

            foreach (byte hookId in Enum.GetValues(typeof(Hook)))
                hooks.Add(hookId, Enum.GetName(typeof(Hook), hookId));

            return hooks;
        }

        public void RegisterHook(byte id, IPlugin.HookHandler callback)
        {
            if (!_hookCallbacks.ContainsKey(id))
                _hookCallbacks.Add(id, callback);
            else
                _hookCallbacks[id] += callback;
        }

        private void CallHook(Hook hook, EventArgs e = null)
        {
            if (_hookCallbacks.ContainsKey((byte)hook))
                _hookCallbacks[(byte)hook]?.Invoke(this, e == null ? EventArgs.Empty : e);
        }
        #endregion

        public Watchdog(ProcessManager processManager)
        {
            processManager.Process.EnableRaisingEvents = true;
            processManager.Process.Exited += (object sender, EventArgs e) =>
            {
                if (processManager.Process.ExitCode != 0)
                {
                    CallHook(Hook.CRASH);

                    processManager.Close();

                    Console.WriteLine("BDS process unexpectedly exited");

                    if(++_failRetryCount <= RetryLimit)
                    {
                        Console.WriteLine($"Retry #{_failRetryCount} to start BDS process");
                        processManager.Start();
                        CallHook(Hook.RETRY);
                    }
                    else
                    {
                        Console.WriteLine("Maximum retry limit reached!");
                        CallHook(Hook.LIMIT_REACHED);
                    }
                }
            };

            processManager.RegisterMatchHandler(CommonRegex.ServerStarted, (object sender, MatchedEventArgs e) =>
            {
                _failRetryCount = 0;
                CallHook(Hook.STABLE);
            });
        }
    }
}