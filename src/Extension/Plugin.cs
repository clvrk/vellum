using System;
using System.Collections.Generic;
using Vellum.Extension;

namespace Vellum.Extension
{
    public abstract class Plugin : IPlugin
    {
        #region PLUGIN
        public IHost Host;
        private Dictionary<byte, IPlugin.HookHandler> _hookCallbacks = new Dictionary<byte, IPlugin.HookHandler>();
        public PluginType PluginType { get { return PluginType.INTERNAL; } }
        public enum Hook {};

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

        internal void CallHook(Hook hook, EventArgs e = null)
        {
            if (_hookCallbacks.ContainsKey((byte)hook))
                _hookCallbacks[(byte)hook]?.Invoke(this, e == null ? EventArgs.Empty : e);
        }
        #endregion
    }
}