using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using Mono.Options;
using Newtonsoft.Json;
using Vellum.Automation;
using Vellum.Networking;

namespace Vellum
{
    class Program
    {
        private static string _configPath = "configuration.json";
        private const string _tempPath = "temp/";
        public static RunConfiguration RunConfig;
        private static BackupManager _backupManager;
        private static RenderManager _renderManager;
        public delegate void InputStreamHandler(string text);
        static InputStreamHandler inStream;
        private static Thread _ioThread;
        private static bool _readInput = true;
        public bool IsReady { get; private set; } = false;
        private static Version _localVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        static void Main(string[] args)
        {
            Console.WriteLine("vellum v{0} build {1}\n\tby clarkx86 & DeepBlue\n", UpdateChecker.ParseVersion(_localVersion, VersionFormatting.MAJOR_MINOR_REVISION), _localVersion.Build);

            bool printHelp = false;

            OptionSet options = new OptionSet() {
                { "h|help", "Displays a help screen.", (string h) => { printHelp = h != null; } },
                { "c=|configuration=", "The configuration file to load settings from.", (string c) => { if (!String.IsNullOrWhiteSpace(c)) { _configPath = c.Trim(); } } }
            };
            System.Collections.Generic.List<string> extraOptions = options.Parse(args);

            if (printHelp)
            {
                System.Console.WriteLine("Overview of available parameters:");
                options.WriteOptionDescriptions(Console.Out);
                System.Environment.Exit(0);
            }
            
            if (File.Exists(_configPath))
            {
                RunConfig = LoadConfiguration(_configPath);

                // ONLY FOR 1.14, should be fixed in the next BDS build
                if (!RunConfig.Backups.StopBeforeBackup && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    Console.WriteLine("NOTICE: Hot-backups are currently not supported on Windows. Please enable \"StopBeforeBackup\" in the \"{0}\" instead.", _configPath);
                    System.Environment.Exit(0);
                }

                #region CONDITIONAL UPDATE CHECK
                if (RunConfig.CheckForUpdates)
                {
                    Console.WriteLine("Checking for updates... ");
                    UpdateChecker updateChecker = new UpdateChecker(ReleaseProvider.GITHUB_RELEASES, @"https://api.github.com/repos/clarkx86/vellum/releases/latest", @"^v?(\d+)\.(\d+)\.(\d+)");

                    Version localVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (updateChecker.GetLatestVersion())
                    {
                        if (updateChecker.RemoteVersion > localVersion)
                        {
                            Console.WriteLine("\nA new update is available!\nLocal version:\t{0}\nRemote version:\t{1}\nVisit {2} to update.\n", UpdateChecker.ParseVersion(localVersion, VersionFormatting.MAJOR_MINOR_REVISION), UpdateChecker.ParseVersion(updateChecker.RemoteVersion, VersionFormatting.MAJOR_MINOR_REVISION), @"https://git.io/vellum-latest");
                        }
                    } else
                    {
                        System.Console.WriteLine("Could not check for updates.");
                    }
                }
                #endregion

                if (RunConfig.Renders.EnableRenders && String.IsNullOrWhiteSpace(RunConfig.Renders.PapyrusBinPath))
                {
                    Console.WriteLine("Disabling renders because no valid path to a Papyrus executable has been specified");
                    RunConfig.Renders.EnableRenders = false;
                }

                string bdsDirPath = Path.GetDirectoryName(RunConfig.BdsBinPath);

                #region BDS process and input thread
                // BDS
                ProcessStartInfo serverStartInfo = new ProcessStartInfo()
                {
                    FileName = RunConfig.BdsBinPath,
                    WorkingDirectory = bdsDirPath
                };

                // Set environment variable for linux-based systems
                if (System.Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    serverStartInfo.EnvironmentVariables.Add("LD_LIBRARY_PATH", bdsDirPath);
                }

                ProcessManager bds = new ProcessManager(serverStartInfo, new string[] {
                    "^(" + RunConfig.WorldName.Trim() + @"\/\d+\.\w+\:\d+)",
                    "^(Saving...)",
                    "^(A previous save has not been completed.)",
                    "^(Data saved. Files are now ready to be copied.)",
                    "^(Changes to the level are resumed.)",
                    "Running AutoCompaction..."
                });

                // Input thread
                _ioThread = new Thread(() =>
                {
                    while (_readInput)
                    {
                        inStream?.Invoke(Console.ReadLine());
                    }
                });
                _ioThread.Start();
                #endregion

                string worldPath = Path.Join(bdsDirPath, "worlds", RunConfig.WorldName);
                string tempWorldPath = Path.Join(Directory.GetCurrentDirectory(), _tempPath, RunConfig.WorldName);

                _renderManager = new RenderManager(bds, RunConfig);
                _backupManager = new BackupManager(bds, RunConfig);

                if (RunConfig.Backups.BackupOnStartup)
                {
                    // Create initial world backup
                    Console.WriteLine("Creating initial world backup...");
                    _backupManager.CreateWorldBackup(worldPath, tempWorldPath, true, false); // If "StopBeforeBackup" is set to "true" this will also automatically start the server when it's done
                }

                // Start server in case the BackupManager hasn't started it yet
                if (!bds.IsRunning) { bds.Start(); }

                // Wait until BDS successfully started
                bds.WaitForMatch(@"^.+ (Server started\.)");

                // Backup interval
                if (RunConfig.Backups.EnableBackups)
                {
                    System.Timers.Timer backupIntervalTimer = new System.Timers.Timer(RunConfig.Backups.BackupInterval * 60000);
                    backupIntervalTimer.AutoReset = true;
                    backupIntervalTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                    {
                        InvokeBackup(worldPath, tempWorldPath);
                    };
                    backupIntervalTimer.Start();

                    if (RunConfig.Backups.StopBeforeBackup)
                    {
                        System.Timers.Timer backupNotificationTimer = new System.Timers.Timer((RunConfig.Backups.BackupInterval * 60000) - Math.Clamp(RunConfig.Backups.NotifyBeforeStop * 1000, 0, RunConfig.Backups.BackupInterval * 60000));
                        backupNotificationTimer.AutoReset = false;
                        backupNotificationTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                        {
                            bds.SendTellraw(String.Format("Shutting down server in {0} seconds to take a backup.", RunConfig.Backups.NotifyBeforeStop));
                        };
                        backupIntervalTimer.Start();
                    }
                }

                // Render interval
                if (RunConfig.Renders.EnableRenders)
                {
                    System.Timers.Timer renderIntervalTimer = new System.Timers.Timer(RunConfig.Renders.RenderInterval * 60000);
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
                            switch (cmd[0].Captures[0].Value)
                            {
                                case "force":
                                    if (cmd.Count >= 3)
                                    {
                                        switch (cmd[1].Captures[0].Value)
                                        {
                                            case "start":
                                                switch (cmd[2].Captures[0].Value)
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
                                        }
                                    }
                                    break;

                                case "stop":
                                    System.Timers.Timer shutdownTimer = new System.Timers.Timer();
                                    shutdownTimer.AutoReset = false;
                                    shutdownTimer.Elapsed += (object sender, ElapsedEventArgs e) => {
                                        // _renderManager.Abort();
                                        bds.SendInput("stop");
                                        bds.Process.WaitForExit();
                                        bds.Close();
                                        _readInput = false;
                                        Console.WriteLine("vellum quit correctly");
                                        shutdownTimer.Close();
                                    };

                                    if (cmd.Count == 2 && !String.IsNullOrWhiteSpace(cmd[1].Captures[0].Value))
                                    {
                                        try
                                        {
                                            double interval = Convert.ToDouble(cmd[1].Captures[0].Value);
                                            shutdownTimer.Interval = (interval > 0 ? interval * 1000 : 1);
                                            bds.SendTellraw(String.Format("Scheduled shutdown in {0} seconds...", interval));
                                            result = true;
                                        } catch
                                        {
                                            Console.WriteLine("Could not schedule shutdown because \"{0}\" is not a valid number.", cmd[1].Captures[0].Value);
                                            result = false;
                                        }
                                    } else
                                    {
                                        shutdownTimer.Interval = 1;
                                        result = true;
                                    }

                                    if (result)
                                    {
                                        shutdownTimer.Start();
                                    }
                                    break;

                                case "reload":
                                    if (cmd.Count == 2 && cmd[1].Captures[0].Value == "vellum")
                                    {
                                        RunConfig = LoadConfiguration(_configPath);
                                    } else
                                    {
                                        bds.SendInput(text);
                                    }
                                    result = true;
                                    break;

                                default:
                                    result = true;
                                    bds.SendInput(text);
                                    break;
                            }

                            if (!result) { Console.WriteLine("Could not execute vellum command \"{0}\".", text); }
                        }
                        #endregion
                    }
                    else
                    {
                        Console.WriteLine("Could not execute vellum command \"{0}\". Please wait until all tasks have finished or enable \"BusyCommands\" in your \"{1}\".", text, _configPath);
                    }
                };
            }
            else
            {
                Console.WriteLine("No previous configuration file found. Creating one...");

                using (StreamWriter writer = new StreamWriter(_configPath))
                {
                    writer.Write(JsonConvert.SerializeObject(new RunConfiguration()
                    {
                        BdsBinPath = System.Environment.OSVersion.Platform != PlatformID.Win32NT ? "bedrock_server" : "bedrock_server.exe",
                        WorldName = "Bedrock level",
                        Backups = new BackupConfig()
                        {
                            EnableBackups = true,
                            StopBeforeBackup = (System.Environment.OSVersion.Platform != PlatformID.Win32NT ? false : true), // Should be reverted to "false" by default when 1.16 releases
                            NotifyBeforeStop = 60,
                            ArchivePath = "./backups/",
                            BackupsToKeep = 10,
                            BackupOnStartup = true,
                            BackupInterval = 60,
                            PreExec = "",
                            PostExec = "",
                        },
                        Renders = new RenderConfig()
                        {
                            EnableRenders = true,
                            RenderInterval = 180,
                            PapyrusBinPath = "",
                            PapyrusGlobalArgs = "-w $WORLD_PATH -o $OUTPUT_PATH --htmlfile index.html -f png -q 100 --deleteexistingupdatefolder",
                            PapyrusTasks = new string[] {
                                "--dim 0",
                                "--dim 1",
                                "--dim 2"
                            },
                            PapyrusOutputPath = ""
                        },
                        QuietMode = false,
                        HideStdout = true,
                        BusyCommands = true,
                        CheckForUpdates = true,
                    }, Formatting.Indented));
                }

                Console.WriteLine(String.Format("Done! Please edit the \"{0}\" file and restart this application.", _configPath));
            }
        }

        public static void InvokeBackup(string worldPath, string tempWorldPath)
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

        public static void InvokeRender(string worldPath, string tempWorldPath)
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

        private static RunConfiguration LoadConfiguration(string configPath)
        {
            Console.Write("Loading configuration \"{0}\"... ", configPath);

            RunConfiguration runConfig;
            using (StreamReader reader = new StreamReader(Path.Join(Directory.GetCurrentDirectory(), _configPath)))
            {
                runConfig = JsonConvert.DeserializeObject<RunConfiguration>(reader.ReadToEnd());
            }

            Console.WriteLine("Done!");

            return runConfig;
        }
    }
}
