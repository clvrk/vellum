using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;

namespace Vellum.Automation
{

    public class BackupManager : Manager
    {
        private ProcessManager _bds;
        public RunConfiguration RunConfig;
        ///<summary>Time in milliseconds to wait until sending next <code>save query</code> command to <code>ProcessManager</code>'s process</summary>
        public int QueryTimeout { get; set; } = 500;

        public BackupManager(ProcessManager p, RunConfiguration runConfig)
        {
            _bds = p;
            RunConfig = runConfig;
        }

        ///<summary>
        ///Creates a copy of a world and attempts to archive it as a compressed .zip-archive in the <code>archivePath</code> directory.
        ///</summary>
        ///<param name="worldPath">Path to the world to copy.</param>
        ///<param name="destinationPath">Path to copy the world to.</param>
        ///<param name="fullCopy">Whether to copy the whole world directory instead of just the updated files. The server must not be running for a full copy.</param>
        ///<param name="archive">Whether to archive the backup as a compressed .zip-file.</param>
        public void CreateWorldBackup(string worldPath, string destinationPath, bool fullCopy, bool archive)
        {
            Processing = true;

            #region PRE EXEC
            if (!string.IsNullOrWhiteSpace(RunConfig.Backups.PreExec))
            {
                Log(String.Format("{0}Executing pre-command...", _tag));
                ProcessManager.RunCustomCommand(RunConfig.Backups.PreExec);
            }
            #endregion

            Log(String.Format("{0}Creating backup...", _tag));
            // Send tellraw message 1/2
            _bds.SendTellraw("Creating backup...");

            // Shutdown server and take full backup
            if (RunConfig.Backups.StopBeforeBackup && _bds.IsRunning)
            {
                _bds.Stop();
            }

            if (fullCopy || RunConfig.Backups.StopBeforeBackup)
            {
                if (Directory.Exists(destinationPath))
                {
                    Log(String.Format("{0}{1}Clearing local world backup directory...\t", _tag, _indent));

                    Directory.Delete(destinationPath, true);
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    Directory.CreateDirectory(destinationPath);
                }


                if (Directory.Exists(worldPath))
                {
                    Log(String.Format("{0}{1}Creating full world backup...\t", _tag, _indent));

                    CopyDirectory(worldPath, destinationPath);
                }
                else
                {
                    Log(String.Format("{0}{1}Invalid world directory. Could not create full world backup!", _tag, _indent));
                }
            }
            else
            {
                Log(String.Format("{0}{1}Holding world saving...", _tag, _indent));

                _bds.SendInput("save hold");
                _bds.SetMatchPattern("^(" + Path.GetFileName(worldPath) + @"[\/]{1})");

                while (!_bds.HasMatched)
                {
                    _bds.SendInput("save query");
                    Thread.Sleep(QueryTimeout);
                }

                Regex fileListRegex = new Regex("(" + Path.GetFileName(worldPath) + @"[\/]{1}.+?)\:{1}(\d+)");
                MatchCollection matches = fileListRegex.Matches(_bds.GetMatchedText());

                string[,] sourceFiles = new string[matches.Count, 2];

                for (int i = 0; i < matches.Count; i++)
                {
                    sourceFiles[i, 0] = matches[i].Groups[1].Value.Replace(Path.GetFileName(worldPath), "");
                    sourceFiles[i, 1] = matches[i].Groups[2].Value;
                    //Console.WriteLine(sourceFiles[i,0]);
                    //Console.WriteLine(sourceFiles[i,1]);
                }

                Log(String.Format("{0}{1}Copying {2} files... ", _tag, _indent, sourceFiles.GetLength(0)));
                // ACTUAL COPYING BEGINS HERE
                for (uint i = 0; i < sourceFiles.GetLength(0); i++)
                {
                    // As of Bedrock Server 1.14, the queried files list doesn't include the "/db/" path, but to take precaution for future versions check if the "/db/" part is present 
                    // The last 3 files always seem to be the world metadata which need to be copied into the worlds root directory instead of the "db"-subdirectory (this only matters if the "/db/" part isn't available in the queried files list)
                    string subDir = (Regex.Match(sourceFiles[i, 0], @"(\/db\/)").Captures.Count < 1) && (i < sourceFiles.GetLength(0) - 3) ? "/db" : "";
                    string filePath = Path.Join(worldPath, subDir, sourceFiles[i, 0]);
                    string targetPath = Path.Join(destinationPath, subDir, sourceFiles[i, 0]);

                    /*
                    System.Console.WriteLine($"Old:\t{sourceFiles[i, 0]}\nNew:\t{filePath}");
                    System.Console.WriteLine(Regex.Match(sourceFiles[i, 0], @"(\/db\/)").Captures.Count);
                    System.Console.WriteLine("\"{0}\" -> \"{1}\"", filePath, targetPath);
                    */

                    using (FileStream sourceStream = File.Open(filePath, FileMode.Open, FileAccess.Read))
                    using (FileStream targetStream = File.Open(targetPath, FileMode.Create, FileAccess.Write))
                    {
                        // Console.WriteLine("Copying: {0}", filePath);

                        // Read bytes until truncate indicator
                        for (int j = 0; j < Convert.ToInt32(sourceFiles[i, 1]); j++)
                        {
                            targetStream.WriteByte((byte)sourceStream.ReadByte());
                        }

                        targetStream.Flush();
                    }
                }

                #region FILE INTEGRITY CHECK

                Log(String.Format("{0}{1}Veryfing file-integrity... ", _tag, _indent));

                string[] sourceDbFiles = Directory.GetFiles(worldPath + "/db/");
                string[] targetDbFiles = Directory.GetFiles(destinationPath + "/db/");

                foreach (string tFile in targetDbFiles)
                {
                    bool found = false;
                    foreach (string sFile in sourceDbFiles)
                    {
                        if (Path.GetFileName(tFile) == Path.GetFileName(sFile))
                        {
                            found = true;
                            break;
                        }
                    }

                    // File isn't in the source world directory anymore, delete!
                    if (!found)
                    {
                        // System.Console.Write("\nDeleting file \"{0}\"...", tFile);
                        File.Delete(tFile);
                    }
                }

                #endregion

                Log(String.Format("{0}{1}Resuming world saving...", _tag, _indent));

                _bds.SendInput("save resume");
                _bds.WaitForMatch("^(Changes to the level are resumed.)");
            }

            string tellrawMsg = "Finished creating backup!";

            // Archive
            if (archive)
            {
                Log(String.Format("{0}{1}Archiving world backup...", _tag, _indent));
                if (Archive(destinationPath, RunConfig.Backups.ArchivePath, RunConfig.Backups.BackupsToKeep))
                {
                    Log(String.Format("{0}{1}Archiving done!", _tag, _indent));
                }
                else
                {
                    Log(String.Format("{0}{1}Archiving failed!", _tag, _indent));
                    tellrawMsg = "Could not archive backup!";
                }
            }

            // Send tellraw message 2/2
            _bds.SendTellraw(tellrawMsg);

            Log(String.Format("{0}Backup done!", _tag));


            if (RunConfig.Backups.StopBeforeBackup && !_bds.IsRunning)
            {
                _bds.Start();
                _bds.WaitForMatch(@"^.+ (Server started\.)");
            }
            
            #region POST EXEC
            if (!string.IsNullOrWhiteSpace(RunConfig.Backups.PostExec))
            {
                Log(String.Format("{0}Executing post-command...", _tag));
                ProcessManager.RunCustomCommand(RunConfig.Backups.PostExec);
            }
            #endregion

            Processing = false;
        }

        ///<summary>Compresses a world as a .zip archive to the <code>destinationPath</code> directory and optionally deletes old backups.</summary>
        ///<param name="sourcePath">World to archive</param>
        ///<param name="destinationPath">Directory to save archive in (archives will be named like this: <code>yyyy-MM-dd_HH-mm_WORLDNAME.zip</code>)</param>
        ///<param name="archivesToKeep">Threshold for archives to keep, archives that exceed this threshold will be deleted, <code>-1</code> to not remove any archives</param>
        public static bool Archive(string sourcePath, string destinationPath, int archivesToKeep)
        {
            bool result = false;

            if (!Directory.Exists(destinationPath))
                Directory.CreateDirectory(destinationPath);

            string archiveName = String.Format("{0}_{1}.{2}", DateTime.Now.ToString("yyyy-MM-dd_HH-mm"), Path.GetFileName(sourcePath), "zip");
            string archivePath = Path.Join(destinationPath, archiveName);

            if (!File.Exists(archivePath))
            {
                try
                {
                    ZipFile.CreateFromDirectory(sourcePath, archivePath, CompressionLevel.Optimal, false);
                    result = true;
                }
                catch
                {
                    Log(String.Format("Could not create archive \"{0}\"!", archiveName));
                    result = false;
                }
            }
            else
            {
                Log(String.Format("Could not create archive \"{0}\" because it already exists!", archiveName));
                result = false;
            }

            // Delete older backups if threshold of archives to keep has been exceeded
            if (archivesToKeep != -1)
            {
                string[] files = Directory.GetFiles(destinationPath);
                DateTime[] creationTimes = new DateTime[files.Length];

                for (int i = 0; i < files.Length; i++)
                {
                    creationTimes[i] = File.GetCreationTime(files[i]);
                }

                Array.Sort(files, creationTimes);

                if (files.Length > archivesToKeep)
                {
                    for (uint i = 0; i < Math.Abs(archivesToKeep - files.Length); i++)
                    {
                        // System.Console.WriteLine("Deleting: {0}", files[i]);
                        try
                        {
                            File.Delete(files[i]);
                        }
                        catch
                        {
                            Log(String.Format("Could not delete {0}", files[i]));
                        }
                    }
                }
            }

            return result;
        }

        ///<summary>Copies an existing directory.</summary>
        ///<param name="sourceDir">Directory to copy</param>
        ///<param name="targetDir">Directory to create and populate with files</param>
        public static void CopyDirectory(string sourceDir, string targetDir)
        {
            // Create root directory
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string[] sourceFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

            foreach (string sFile in sourceFiles)
            {
                string tFile = sFile.Replace(sourceDir, targetDir);

                // Create sub-directory if needed
                string subDir = Path.GetDirectoryName(tFile);
                if (!Directory.Exists(subDir))
                {
                    Directory.CreateDirectory(subDir);
                }

                File.Copy(sFile, tFile, true);
            }
        }
    }
}
