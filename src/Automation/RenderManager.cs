using System;
using System.IO;
using System.Diagnostics;

namespace Vellum.Automation
{
    public class RenderManager : Manager
    {
        private ProcessManager _bds;
        private Process _renderer;
        public RunConfiguration RunConfig;

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
            if (!Directory.Exists(RunConfig.PapyrusOutputPath))
            {
                Directory.CreateDirectory(RunConfig.PapyrusOutputPath);
            }

            for (int i = 0; i < RunConfig.PapyrusTasks.Length; i++)
            {
                _renderer = new Process();
                string args = RunConfig.PapyrusGlobalArgs.Replace("${WORLD_PATH}", String.Format("\"{0}\"", tempPathCopy)).Replace("${OUTPUT_PATH}", String.Format("\"{0}\"", RunConfig.PapyrusOutputPath)) + " " + RunConfig.PapyrusTasks[i];
                _renderer.StartInfo.FileName = RunConfig.PapyrusBinPath;
                _renderer.StartInfo.Arguments = args;
                _renderer.StartInfo.RedirectStandardOutput = RunConfig.HideStdout;
                _renderer.StartInfo.RedirectStandardInput = true;

                Log(String.Format("{0}{1}Rendering map {2}/{3}...", _tag, _indent, i + 1, RunConfig.PapyrusTasks.Length));

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