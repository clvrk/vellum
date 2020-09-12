using System;
using System.Collections.Generic;
using System.Reflection;

namespace Vellum.Extension
{
    public abstract class InternalPlugin : IPlugin
    {
        #region PLUGIN
        public IHost Host;
        private Dictionary<byte, IPlugin.HookHandler> _hookCallbacks = new Dictionary<byte, IPlugin.HookHandler>();
        public PluginType PluginType { get { return PluginType.INTERNAL; } }

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
            Type hookType = Type.GetType($"{this.GetType().FullName}+Hook");

            // foreach (byte hookId in Enum.GetValues(hookType))
            //     hooks.Add(hookId, Enum.GetName(hookType, hookId));

            string[] hookNames = Enum.GetNames(hookType);
            for (int i = 0; i < hookNames.Length; i++)
                hooks.Add((byte)i, hookNames[i]);

            return hooks;
        }

        public void RegisterHook(byte id, IPlugin.HookHandler callback)
        {
            if (!_hookCallbacks.ContainsKey(id))
                _hookCallbacks.Add(id, callback);
            else
                _hookCallbacks[id] += callback;
        }

        internal void CallHook(byte hookId, EventArgs e = null)
        {
            if (_hookCallbacks.ContainsKey(hookId))
                _hookCallbacks[hookId]?.Invoke(this, e == null ? EventArgs.Empty : e);
        }
        #endregion
    }
}