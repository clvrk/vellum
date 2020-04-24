using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using Newtonsoft.Json;
using papyrus.Automation;

namespace papyrus
{
    class Program
    {
        private const string _configFname = "configuration.json";
        private const string _tempPath = "temp/";
        public static RunConfiguration RunConfig;
        private static BackupManager _backupManager;
        private static RenderManager _renderManager;
        public delegate void InputStreamHandler(string text);
        static InputStreamHandler inStream;
        private static Thread _ioThread;
        private static bool _readInput = true;
        public bool IsReady { get; private set; } = false;

        static void Main(string[] args)
        {
            Console.WriteLine("papyrus automation tool v{0}.{1}.{2} build {3}\n\tby clarkx86 & DeepBlue\n", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Major, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Minor, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Revision, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Build);

            if (File.Exists(_configFname))
            {
                using (StreamReader reader = new StreamReader(Path.Join(Directory.GetCurrentDirectory(), _configFname)))
                {
                    RunConfig = JsonConvert.DeserializeObject<RunConfiguration>(reader.ReadToEnd());
                }

                // ONLY FOR 1.14, should be fixed in the next BDS build
                if (!RunConfig.StopBeforeBackup && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    Console.WriteLine("NOTICE: Hot-backups are currently not supported on Windows. Please enable \"StopBeforeBackup\" in the \"{0}\" instead.", _configFname);
                    System.Environment.Exit(0);
                }


                #region BDS process and input thread
                // BDS
                ProcessStartInfo serverStartInfo = new ProcessStartInfo()
                {
                    FileName = Path.Join(RunConfig.BdsPath, System.Environment.OSVersion.Platform == PlatformID.Unix ? "bedrock_server" : "bedrock_server.exe"),
                    WorkingDirectory = RunConfig.BdsPath
                };

                // Set environment variable for linux-based systems
                if (System.Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    serverStartInfo.EnvironmentVariables.Add("LD_LIBRARY_PATH", RunConfig.BdsPath);
                }

                ProcessManager bds = new ProcessManager(serverStartInfo, new string[] {
                    "^(" + RunConfig.WorldName.Trim() + @"\/\d+\.\w+\:\d+)",
                    "^(Saving...)",
                    "^(A previous save has not been completed.)",
                    "^(Data saved. Files are now ready to be copied.)",
                    "^(Changes to the level are resumed.)"
                });

                // Input thread
                _ioThread = new Thread(new ThreadStart(() =>
                {
                    while (_readInput)
                    {
                        inStream?.Invoke(Console.ReadLine());
                    }
                }));
                _ioThread.Start();
                #endregion

                string worldPath = Path.Join(RunConfig.BdsPath, "worlds", RunConfig.WorldName);
                string tempWorldPath = Path.Join(Directory.GetCurrentDirectory(), _tempPath, RunConfig.WorldName);

                _renderManager = new RenderManager(bds, RunConfig);
                _backupManager = new BackupManager(bds, RunConfig);

                if (RunConfig.BackupOnStartup)
                {
                    // Create initial world backup
                    Console.WriteLine("Creating initial world backup...");
                    _backupManager.CreateWorldBackup(worldPath, tempWorldPath, true, false); // If "StopBeforeBackup" is set to "true" this will also automatically start the server when it's done
                }

                // Start server in case the BackupManager hasn't started it yet
                if (!bds.IsRunning) { bds.Start(); }

                // Wait until BDS successfully started
                bds.WaitForMatch(new Regex(@"^.+ (Server started\.)"));

                // Backup interval
                if (RunConfig.EnableBackups)
                {
                    System.Timers.Timer backupIntervalTimer = new System.Timers.Timer(RunConfig.BackupInterval * 60000);
                    backupIntervalTimer.AutoReset = true;
                    backupIntervalTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                    {
                        InvokeBackup(worldPath, tempWorldPath);
                    };
                    backupIntervalTimer.Start();

                    if (RunConfig.StopBeforeBackup)
                    {
                        System.Timers.Timer backupNotificationTimer = new System.Timers.Timer((RunConfig.BackupInterval * 60000) - (RunConfig.TimeBeforeStop * 1000));
                        backupNotificationTimer.AutoReset = true;
                        backupNotificationTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                        {
                            bds.SendTellraw(String.Format("Shutting down server in {0} seconds to take a backup.", RunConfig.TimeBeforeStop));
                        };
                    }
                }

                // Render interval
                if (RunConfig.EnableRenders)
                {
                    System.Timers.Timer renderIntervalTimer = new System.Timers.Timer(RunConfig.RenderInterval * 60000);
                    renderIntervalTimer.AutoReset = true;
                    renderIntervalTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                    {
                        InvokeRender(worldPath, tempWorldPath);
                    };
                    renderIntervalTimer.Start();
                }

                // Input thread
                inStream = (string text) =>
                {
                    if (RunConfig.BusyCommands || (!_backupManager.Processing && !_renderManager.Processing))
                    {
                        #region CUSTOM COMMANDS
                        MatchCollection cmd = Regex.Matches(text.ToLower().Trim(), @"(\S+)");

                        if (cmd.Count > 0)
                        {
                            bool result = false;
                            switch (cmd[0].Groups[1].Value)
                            {
                                case "force":
                                    if (cmd.Count >= 3)
                                    {
                                        switch (cmd[1].Groups[1].Value)
                                        {
                                            case "start":
                                                switch (cmd[2].Groups[1].Value)
                                                {
                                                    case "backup":
                                                        InvokeBackup(worldPath, tempWorldPath);
                                                        result = true;
                                                        break;

                                                    case "render":
                                                        InvokeRender(worldPath, tempWorldPath);
                                                        result = true;
                                                        break;
                                                }
                                                break;

                                                /*
                                                case "stop":
                                                    switch (cmd[2].Groups[1].Value)
                                                    {
                                                        case "render":
                                                            Console.WriteLine((_renderManager.Abort() ? "Render process stopped by user." : "Could not stop render process."));
                                                            result = true;
                                                        break;
                                                    }
                                                break;
                                                */
                                        }
                                    }
                                    break;

                                case "stop":
                                    result = true;
                                    // _renderManager.Abort();
                                    bds.SendInput("stop");
                                    bds.Process.WaitForExit();
                                    _readInput = false;
                                    Console.WriteLine("papyrus quit correctly.");
                                    break;

                                default:
                                    result = true;
                                    bds.SendInput(text);
                                    break;
                            }

                            if (!result) { Console.WriteLine("Could not execute papyrus command \"{0}\".", text); }
                        }
                        #endregion
                    }
                    else
                    {
                        Console.WriteLine("Could not execute papyrus command \"{0}\". Please wait until all tasks have finished or enable \"BusyCommands\" in your \"{1}\".", text, _configFname);
                    }
                };
            }
            else
            {
                Console.WriteLine("No previous configuration file found. Creating one...");

                using (StreamWriter writer = new StreamWriter(_configFname))
                {
                    writer.Write(JsonConvert.SerializeObject(new RunConfiguration()
                    {
                        BdsPath = "",
                        WorldName = "Bedrock level",
                        PapyrusBinPath = "",
                        PapyrusGlobalArgs = "-w ${WORLD_PATH} -o ${OUTPUT_PATH} --htmlfile index.html -f webp -q -1 --deleteexistingupdatefolder",
                        PapyrusTasks = new string[] {
                             "--dim 0",
                             "--dim 1",
                             "--dim 2"
                        },
                        PapyrusOutputPath = "",
                        ArchivePath = "./backups/",
                        BackupsToKeep = 10,
                        BackupOnStartup = true,
                        EnableRenders = true,
                        EnableBackups = true,
                        RenderInterval = 180,
                        BackupInterval = 60,
                        PreExec = "",
                        PostExec = "",
                        QuietMode = false,
                        HideStdout = true,
                        BusyCommands = true,
                        StopBeforeBackup = (System.Environment.OSVersion.Platform != PlatformID.Win32NT ? false : true), // Should be reverted to "false" by default when 1.16 releases
                        TimeBeforeStop = 60
                    }, Formatting.Indented));
                }

                Console.WriteLine(String.Format("Done! Please edit the \"{0}\" file and restart this application.", _configFname));
            }
        }

        private static void InvokeBackup(string worldPath, string tempWorldPath)
        {
            if (!_backupManager.Processing)
            {
                _backupManager.CreateWorldBackup(worldPath, tempWorldPath, false, true);
            }
            else
            {
                if (!Program.RunConfig.QuietMode) { Console.WriteLine("A backup task is still running."); }
            }
        }

        private static void InvokeRender(string worldPath, string tempWorldPath)
        {
            if (!_backupManager.Processing && !_renderManager.Processing)
            {
                _backupManager.CreateWorldBackup(worldPath, tempWorldPath, false, false);
                _renderManager.Start(tempWorldPath);
            }
            else
            {
                if (!Program.RunConfig.QuietMode) { Console.WriteLine("A render task is still running."); }
            }
        }
    }
}
