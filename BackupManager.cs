using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;

namespace papyrus_automation
{
    public class BackupManager : Manager
    {
        private ProcessManager _bds;

        public BackupManager(ProcessManager p)
        {
            _bds = p;
        }

        public void CreateWorldBackup(String worldPath, String tempPath, bool fullCopy, bool archive)
        {
            // if (!Processing)
            // {
                Processing = true;
                if (fullCopy)
                {
                    if (Directory.Exists(tempPath))
                    {
                        Console.Write("{0}Clearing local world backup directory...\t", _indent);

                        Directory.Delete(tempPath, true);
                        Directory.CreateDirectory(tempPath);

                        Console.WriteLine("Done!");
                    }
                    else
                    {
                        Directory.CreateDirectory(tempPath);
                    }

                    
                    if (Directory.Exists(worldPath))
                    {
                        Console.Write("{0}Attempting to create full world backup...\t", _indent);

                        CopyDirectory(worldPath, tempPath);

                        Console.WriteLine("Done!");
                    }
                    else
                    {
                        Console.WriteLine("{0}Invalid world directory. Could not create full world backup!", _indent);
                    }
                }
                else
                {
                    #region PRE EXEC
                    if (!String.IsNullOrWhiteSpace(Program.RunConfig.PreExec))
                    {
                        Console.WriteLine("{0}Executing pre-command...", _tag);
                        ProcessManager.RunCustomCommand(Program.RunConfig.PreExec);
                    }
                    #endregion

                    Console.WriteLine("{0}Creating backup...", _tag);
                    Console.WriteLine("{0}{1}Holding world saving...", _tag, _indent);

                    // Send tellraw message 1/2
                    _bds.SendTellraw("Creating backup...");
                    
                    _bds.EnableConsoleOutput = false;
                    _bds.SendInput("save hold\n");
                    _bds.SetMatchPattern(new Regex("^(" + Path.GetFileName(worldPath) + @"[\/]{1})"));

                    while (!_bds.HasMatched)
                    {
                        _bds.SendInput("save query\n");
                        Thread.Sleep(500);
                    }

                    _bds.EnableConsoleOutput = true;

                    Regex fileListRegex = new Regex("(" + Path.GetFileName(worldPath) + @"[\/]{1}.+?)\:{1}(\d+)");
                    MatchCollection matches = fileListRegex.Matches(_bds.GetMatchedText());

                    String[,] sourceFiles = new String[matches.Count, 2];

                    for (int i = 0; i < matches.Count; i++)
                    {
                        sourceFiles[i, 0] = matches[i].Groups[1].Value.Replace(Path.GetFileName(worldPath), "");
                        sourceFiles[i, 1] = matches[i].Groups[2].Value;
                        //Console.WriteLine(sourceFiles[i,0]);
                        //Console.WriteLine(sourceFiles[i,1]);
                    }

                    Console.WriteLine("{0}{1}Copying {2} files... ", _tag, _indent, sourceFiles.GetLength(0));
                    // ACTUAL COPYING BEGINS HERE
                    for (uint i = 0; i < sourceFiles.GetLength(0); i++)
                    {
                        // The last 3 files always seem to be the world metadata which need to be copied into the worlds root directory instead of the "db"-subdirectory
                        String subDir = (i < sourceFiles.GetLength(0) - 3) ? "/db" : "";
                        String filePath = Path.Join(worldPath + subDir, Path.GetFileName(sourceFiles[i, 0]));
                        String targetPath = tempPath + subDir + sourceFiles[i, 0];

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

                    Console.WriteLine("{0}{1}Veryfing file-integrity... ", _tag, _indent);

                    String[] sourceDbFiles = Directory.GetFiles(worldPath + "/db/");
                    String[] targetDbFiles = Directory.GetFiles(tempPath + "/db/");

                    foreach (String tFile in targetDbFiles)
                    {
                        bool found = false;
                        foreach (String sFile in sourceDbFiles)
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

                    Console.WriteLine("{0}{1}Resuming world saving...", _tag, _indent);

                    _bds.EnableConsoleOutput = false;
                    _bds.SendInput("save resume\n");
                    _bds.WaitForMatch(new Regex("^(Changes to the level are resumed.)"), 1);
                    _bds.EnableConsoleOutput = true;

                    String tellrawMsg = "Finished creating backup!";

                    // Archive
                    if (archive)
                    {
                        Console.WriteLine("{0}{1}Archiving world backup...", _tag, _indent);
                        if (ArchiveBackup(tempPath, Program.RunConfig.ArchivePath, Program.RunConfig.BackupsToKeep))
                        {
                            Console.WriteLine("{0}{1}Archiving done!", _tag, _indent);
                        } else {
                            Console.WriteLine("{0}{1}Archiving failed!", _tag, _indent);
                            tellrawMsg = "Could not archive backup!";
                        }
                    }
                    
                    // Send tellraw message 2/2
                    _bds.SendTellraw(tellrawMsg);

                    Console.WriteLine("{0}Backup done!", _tag);
                    
                    #region POST EXEC
                    if (!String.IsNullOrWhiteSpace(Program.RunConfig.PostExec))
                    {
                        Console.WriteLine("{0}Executing post-command...", _tag);
                        ProcessManager.RunCustomCommand(Program.RunConfig.PostExec);
                    }
                    #endregion
                }

                Processing = false;
            // }
        }

        public static bool ArchiveBackup(String sourcePath, String destinationPath, uint backupsToKeep)
        {
            bool result = false;

            if (!Directory.Exists(destinationPath)) { Directory.CreateDirectory(destinationPath); }

            String[] files = Directory.GetFiles(destinationPath);
            DateTime[] creationTimes = new DateTime[files.Length];

            for (int i = 0; i < files.Length; i++)
            {
                creationTimes[i] = File.GetCreationTime(files[i]);
            }

            Array.Sort(files, creationTimes);

            if (files.Length > Program.RunConfig.BackupsToKeep) 
            {
                for (uint i = 0; i < Math.Abs(Program.RunConfig.BackupsToKeep - files.Length); i++)
                {
                    // System.Console.WriteLine("Deleting: {0}", files[i]);
                    try
                    {
                        File.Delete(files[i]);
                    } catch {
                        System.Console.WriteLine("Could not delete {0}", files[i]);
                    }
                }
            }

            String archiveName = String.Format("{0}_{1}.{2}", DateTime.Now.ToString("yyyy-MM-dd_HH-mm"), Path.GetFileName(sourcePath), "zip");
            String archivePath = Path.Join(destinationPath, archiveName);

            if (!File.Exists(archivePath))
            {
                try {
                    ZipFile.CreateFromDirectory(sourcePath, archivePath, CompressionLevel.Optimal, false);
                    result = true;
                } catch {
                    Console.WriteLine("Could not create archive \"{0}\"!", archiveName);
                    result = false;
                }
            } else {
                Console.WriteLine("Could not create archive \"{0}\" because it already exists!", archiveName);
                result = false;
            }

            return result;
        }

        public static void CopyDirectory(String sourceDir, String targetDir)
        {
            // Create root directory
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            String[] sourceFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

            foreach (String sFile in sourceFiles)
            {
                String tFile = sFile.Replace(sourceDir, targetDir);

                // Create sub-directory if needed
                String subDir = Path.GetDirectoryName(tFile);
                if (!Directory.Exists(subDir))
                {
                    Directory.CreateDirectory(subDir);
                }

                File.Copy(sFile, tFile, true);
            }
        }
    }
}
