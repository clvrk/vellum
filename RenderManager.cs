using System;
using System.IO;
using System.Diagnostics;

namespace papyrus_automation
{
    public class RenderManager : Manager
    {
        private ProcessManager _bds;

        public RenderManager(ProcessManager p)
        {
            _bds = p;
        }

        public void StartRender(String tempPath)
        {
            // if (!Processing)
            // {
                Processing = true;

                // Send tellraw message 1/2
                _bds.SendTellraw("Rendering map...");

                Console.WriteLine("{0}Initializing map rendering...", _tag);

                // Create temporary copy of latest backup to initiate render on
                String prfx = "_";
                String tempPathCopy = tempPath.Replace(Path.GetFileName(tempPath), prfx + Path.GetFileName(tempPath));
                BackupManager.CopyDirectory(tempPath, tempPathCopy);

                // Prepare map render output directory
                if (!Directory.Exists(Program.RunConfig.PapyrusOutputPath))
                {
                    Directory.CreateDirectory(Program.RunConfig.PapyrusOutputPath);
                }

                for (int i = 0; i < Program.RunConfig.PapyrusTasks.Length; i++)
                {
                    Process renderer = new Process();
                    String args = Program.RunConfig.PapyrusGlobalArgs.Replace("${WORLD_PATH}", String.Format("\"{0}\"", tempPathCopy)).Replace("${OUTPUT_PATH}", String.Format("\"{0}\"", Program.RunConfig.PapyrusOutputPath)) + " " + Program.RunConfig.PapyrusTasks[i];
                    renderer.StartInfo.FileName = Program.RunConfig.PapyrusBinPath;
                    renderer.StartInfo.Arguments = args;
                    renderer.StartInfo.RedirectStandardOutput = Program.RunConfig.HideStdout;

                    Console.WriteLine("{0}{1}Rendering map {2}/{3}...", _tag, _indent, i+1, Program.RunConfig.PapyrusTasks.Length);

                    renderer.Start();
                    renderer.WaitForExit();
                }

                Console.WriteLine("{0}{1}Cleaning up...", _tag, _indent);

                Directory.Delete(tempPathCopy, true);

                Console.WriteLine("{0}Rendering done!", _tag, _indent);

                // Send tellraw message 2/2
                _bds.SendTellraw("Done rendering!");

                Processing = false;
            // }
        }
    }
}