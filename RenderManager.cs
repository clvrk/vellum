using System;
using System.IO;
using System.Diagnostics;

namespace papyrus.Automation
{
    public class RenderManager : Manager
    {
        private ProcessManager _bds;
        public RunConfiguration RunConfig;

        public RenderManager(ProcessManager p, RunConfiguration runConfig)
        {
            _bds = p;
            RunConfig = runConfig;
        }

        public void StartRender(string worldPath)
        {
            // if (!Processing)
            // {
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
                    Process renderer = new Process();
                    string args = RunConfig.PapyrusGlobalArgs.Replace("${WORLD_PATH}", String.Format("\"{0}\"", tempPathCopy)).Replace("${OUTPUT_PATH}", String.Format("\"{0}\"", RunConfig.PapyrusOutputPath)) + " " + RunConfig.PapyrusTasks[i];
                    renderer.StartInfo.FileName = RunConfig.PapyrusBinPath;
                    renderer.StartInfo.Arguments = args;
                    renderer.StartInfo.RedirectStandardOutput = RunConfig.HideStdout;

                    Log(String.Format("{0}{1}Rendering map {2}/{3}...", _tag, _indent, i+1, RunConfig.PapyrusTasks.Length));

                    renderer.Start();
                    renderer.WaitForExit();
                }

                Log(String.Format("{0}{1}Cleaning up...", _tag, _indent));

                Directory.Delete(tempPathCopy, true);

                Log(String.Format("{0}Rendering done!", _tag, _indent));

                // Send tellraw message 2/2
                _bds.SendTellraw("Done rendering!");

                Processing = false;
            // }
        }
    }
}