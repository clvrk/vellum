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
        public void CreateWorldBackup(string worldPath, string destinationPath, bool fullCopy, bool archive, bool force)
        {

            Processing = true;
            _bds.playerleft = false; //In case.
            #region PRE EXEC
            if (!string.IsNullOrWhiteSpace(RunConfig.PreExec))
            {
                Log(String.Format("{0}Executing pre-command...", _tag));
                ProcessManager.RunCustomCommand(RunConfig.PreExec);
            }
            #endregion


            //Check for players
            if (_bds.IsRunning)
            {

                _bds.SendInput("list");
                _bds.WaitForMatch(@"^(There are [0-9]{1,5}\/[0-9]{1,5} players online\:)");
                string players_str = Regex.Match(_bds.GetMatchedText(), @"([0-9]{1,5}\/)").Value; //should return the There are... string 
                players_str = players_str.Remove(players_str.Length - 1); //since the value returned is 0/ so the '/' has to be removed.
                int players = Int32.Parse(players_str);
                Console.WriteLine("{0} Players Online: {1}", _tag, players);
                if (_bds.nextbackup == false && players == 0 && force == false)
                {
                    Console.WriteLine("{0}Backup not taken due to no players online.", _tag);
                    _bds.SendInput("save resume");
                    Processing = false;
                    _bds.playerleft = true;
                    return;
                }
                if (players > 0 || force)
                {
                    _bds.nextbackup = true;
                    Console.WriteLine("{0}Taking backup", _tag);
                    Console.WriteLine("\n");

                }
                else
                {
                    _bds.nextbackup = false;
                    Console.WriteLine("{0}No player is online,next backup will not be taken", _tag);
                    Console.WriteLine("\n");
                }

            }
            Log(String.Format("{0}Creating backup...", _tag));
            // Send tellraw message 1/2
            _bds.SendTellraw("Creating backup...");
            // Shutdown server and take full backup
            if (RunConfig.StopBeforeBackup && _bds.IsRunning)
            {
                _bds.SendInput("stop");
                // _bds.WaitForMatch(new Regex(@"^(Quit correctly)"));
                _bds.Process.WaitForExit();
                _bds.Close();
            }

            if (fullCopy || RunConfig.StopBeforeBackup)
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
                _bds.WaitForMatch(@"Saving...");
                _bds.SendInput("save query");
                _bds.WaitForMatch("^(" + Path.GetFileName(worldPath) + @"[\/]{1})");


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
                    // The last 3 files always seem to be the world metadata which need to be copied into the worlds root directory instead of the "db"-subdirectory
                    string subDir = (i < sourceFiles.GetLength(0) - 3) ? "/db" : "";
                    string filePath = Path.Join(worldPath + subDir, Path.GetFileName(sourceFiles[i, 0]));
                    string targetPath = destinationPath + subDir + sourceFiles[i, 0];

                    // System.Console.WriteLine("\"{0}\" -> \"{1}\"", filePath, targetPath);

                    using (FileStream sourceStream = File.Open(filePath, FileMode.Open, FileAccess.Read))
                    using (FileStream targetStream = File.Open(targetPath, FileMode.Create, FileAccess.Write))
                    {
                        // Console.WriteLine("Copying: {0}", filePath);

                        // Read bytes until truncate indicator
                        for (int j = 0; j < Convert.ToInt32(sourceFiles[i, 1]); j++)
                        {
                            targetStream.WriteByte((byte)sourceStream.ReadByte());
                        }
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
                if (Archive(destinationPath, RunConfig.ArchivePath, RunConfig.BackupsToKeep))
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


            if (RunConfig.StopBeforeBackup && !_bds.IsRunning)
            {
                _bds.Start();
                _bds.WaitForMatch(@"^.+ (Server started\.)");
            }

            #region POST EXEC
            if (!string.IsNullOrWhiteSpace(RunConfig.PostExec))
            {
                Log(String.Format("{0}Executing post-command...", _tag));
                ProcessManager.RunCustomCommand(RunConfig.PostExec);
            }
            #endregion

            Processing = false;
            _bds.playerleft = true; //listen for player 'events' 
        }

        ///<summary>Compresses a world as a .zip archive to the <code>destinationPath</code> directory and optionally deletes old backups.</summary>
        ///<param name="sourcePath">World to archive</param>
        ///<param name="destinationPath">Directory to save archive in (archives will be named like this: <code>yyyy-MM-dd_HH-mm_WORLDNAME.zip</code>)</param>
        ///<param name="archivesToKeep">Threshold for archives to keep, archives that exceed this threshold will be deleted, <code>-1</code> to not remove any archives</param>
        public static bool Archive(string sourcePath, string destinationPath, int archivesToKeep)
        {
            bool result = false;

            if (!Directory.Exists(destinationPath)) { Directory.CreateDirectory(destinationPath); }

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
