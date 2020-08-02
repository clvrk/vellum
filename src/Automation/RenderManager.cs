using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using Vellum.Extension;

namespace Vellum.Automation
{
    public class RenderManager : Manager, IPlugin
    {
        private ProcessManager _bds;
        private Process _renderer;
        public RunConfiguration RunConfig;

#region PLUGIN
        public IHost Host;
        public Version Version { get; }
        public PluginType PluginType { get { return PluginType.INTERNAL; } }
        private Dictionary<byte, IPlugin.HookHandler> _hookCallbacks = new Dictionary<byte, IPlugin.HookHandler>();
        public enum Hook
        {
            BEGIN,
            END
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
            _hookCallbacks[id] += callback;
        }

        private void CallHook(Hook hook, EventArgs e = null)
        {
            _hookCallbacks[(byte)hook]?.Invoke(this, e);
        }
        #endregion

        public RenderManager(ProcessManager p, RunConfiguration runConfig)
        {
            _bds = p;
            RunConfig = runConfig;
        }

        public void Start(string worldPath)
        {
            Processing = true;

            // Send tellraw message 1/2
            _bds.SendTellraw("Rendering map...");

            Log(String.Format("{0}Initializing map rendering...", _tag));

            // Create temporary copy of latest backup to initiate render on
            string prfx = "_";
            string tempPathCopy = worldPath.Replace(Path.GetFileName(worldPath), prfx + Path.GetFileName(worldPath));
            BackupManager.CopyDirectory(worldPath, tempPathCopy);

            // Prepare map render output directory
            if (!Directory.Exists(RunConfig.Renders.PapyrusOutputPath))
            {
                Directory.CreateDirectory(RunConfig.Renders.PapyrusOutputPath);
            }

            for (int i = 0; i < RunConfig.Renders.PapyrusTasks.Length; i++)
            {
                Dictionary<string, string> placeholderReplacements = new Dictionary<string, string>()
                {
                    { "$WORLD_PATH", String.Format("\"{0}\"", tempPathCopy) },
                    { "$OUTPUT_PATH", String.Format("\"{0}\"", RunConfig.Renders.PapyrusOutputPath) },
                    { "${WORLD_PATH}", String.Format("\"{0}\"", tempPathCopy) },
                    { "${OUTPUT_PATH}", String.Format("\"{0}\"", RunConfig.Renders.PapyrusOutputPath) }
                };

                string args = RunConfig.Renders.PapyrusGlobalArgs;

                foreach (KeyValuePair<string, string> kv in placeholderReplacements)
                    args = args.Replace(kv.Key, kv.Value);

                _renderer = new Process();
                _renderer.StartInfo.FileName = RunConfig.Renders.PapyrusBinPath;
                _renderer.StartInfo.WorkingDirectory = Path.GetDirectoryName(RunConfig.Renders.PapyrusBinPath);
                _renderer.StartInfo.Arguments = $"{args} {RunConfig.Renders.PapyrusTasks[i]}";
                _renderer.StartInfo.RedirectStandardOutput = RunConfig.HideStdout;
                _renderer.StartInfo.RedirectStandardInput = true;

                Log(String.Format("{0}{1}Rendering map {2}/{3}...", _tag, _indent, i + 1, RunConfig.Renders.PapyrusTasks.Length));

                _renderer.Start();
                _renderer.WaitForExit();
            }

            Log(String.Format("{0}{1}Cleaning up...", _tag, _indent));

            Directory.Delete(tempPathCopy, true);

            Log(String.Format("{0}Rendering done!", _tag, _indent));

            // Send tellraw message 2/2
            _bds.SendTellraw("Done rendering!");

            Processing = false;
        }

        public bool Abort()
        {
            bool result = false;
            if (_renderer != null)
            {
                _renderer.Kill();
                result = true;
            } else {
                result = false;
            }

            return result;
        }
    }
}