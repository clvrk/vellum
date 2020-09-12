using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using Mono.Options;
using Newtonsoft.Json;
using Vellum.Automation;
using Vellum.Networking;
using Vellum.Extension;

namespace Vellum
{
    public class Program
    {
        private const string _serverPropertiesFileName = "server.properties";
        private const string _tempPath = "temp/";
        public static RunConfiguration RunConfig;
        public delegate void InputStreamHandler(string text);
        static InputStreamHandler inStream;
        public bool IsReady { get; private set; } = false;
        private static BackupManager _backupManager;
        private static RenderManager _renderManager;
        private static Watchdog _bdsWatchdog;

        private static UpdateChecker _updateChecker = new UpdateChecker(ReleaseProvider.GITHUB_RELEASES, @"https://api.github.com/repos/clarkx86/vellum/releases/latest", @"^v?(\d+)\.(\d+)\.(\d+)");
        private static uint playerCount;

        static void Main(string[] args)
        {
            string configPath = "configuration.json";
            string pluginDirectory = "plugins/";

            Version _localVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Version _bdsVersion = new Version();

            Thread _ioThread;
            bool _readInput = true;

            string debugTag = "";

            #if DEBUG
            debugTag = " DEBUG";
            #endif

            Console.WriteLine("vellum v{0} build {1}\n{2}by clarkx86, DeepBlue & contributors\n", UpdateChecker.ParseVersion(_localVersion, VersionFormatting.MAJOR_MINOR_REVISION) + debugTag, _localVersion.Build, new string(' ', 7));

            bool printHelp = false;
            bool noStart = false;
            string restorePath = null;
            bool backupOnStartup = true;

            OptionSet options = new OptionSet() {
                { "h|help", "Displays a help screen.", v => { printHelp = v != null; } },
                { "c=|configuration=", "The configuration file to load settings from.", v => { if (!String.IsNullOrWhiteSpace(v)) configPath = v.Trim(); } },
                { "p=|plugin-directory=", "The directory to scan for plugins.", v => { if (!String.IsNullOrWhiteSpace(v)) pluginDirectory = v; } },
                { "r=|restore=", "Path to an archive to restore a backup from.", v => { if (!String.IsNullOrWhiteSpace(v)) restorePath = v; } },
                { "no-start", "In conjunction with the --restore flag, this tells the application to not start the server after successfully restoring a backup.", v => { noStart = v != null; } },
                { "no-backup-on-startup", "Disables the initial temporary backup on startup.", v => { backupOnStartup = v == null; } }
            };
            System.Collections.Generic.List<string> extraOptions = options.Parse(args);

            if (printHelp)
            {
                System.Console.WriteLine("Overview of available parameters:");
                options.WriteOptionDescriptions(Console.Out);
                System.Environment.Exit(0);
            }
            
            if (File.Exists(configPath))
            {
                // Load configuration
                RunConfig = LoadConfiguration(configPath);
              
                string bdsDirPath = Path.GetDirectoryName(RunConfig.BdsBinPath);
                string worldName = "Bedrock level";

                Console.Write($"Reading \"{_serverPropertiesFileName}\"... ");

                using (StreamReader reader = new StreamReader(File.OpenRead(Path.Join(bdsDirPath, _serverPropertiesFileName))))
                    worldName = Regex.Match(reader.ReadToEnd(), @"^level\-name\=(.+)", RegexOptions.Multiline).Groups[1].Value;

                Console.WriteLine("Done!");

                if (!String.IsNullOrWhiteSpace(restorePath))
                {
                    BackupManager.Restore(restorePath, "");

                    if (noStart)
                    {
                        Console.WriteLine("\"--no-start\" flag provided. Exiting...");
                        System.Environment.Exit(0);
                    }
                }


                #region CONDITIONAL UPDATE CHECK
                if (RunConfig.CheckForUpdates)
                {
                    Console.WriteLine("Checking for updates...");

                    if (_updateChecker.GetLatestVersion())
                    {
                        if (_updateChecker.RemoteVersion > _localVersion)
                        {
                            Console.WriteLine("\nA new update is available!\nLocal version:\t{0}\nRemote version:\t{1}\nVisit {2} to update.\n", UpdateChecker.ParseVersion(_localVersion, VersionFormatting.MAJOR_MINOR_REVISION), UpdateChecker.ParseVersion(_updateChecker.RemoteVersion, VersionFormatting.MAJOR_MINOR_REVISION), @"https://git.io/vellum-latest");
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


                #region BDS process and input thread
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
                    "^(" + worldName.Trim() + @"(?>\/db)?\/)",
                    "^(Saving...)",
                    "^(A previous save has not been completed.)",
                    "^(Data saved. Files are now ready to be copied.)",
                    "^(Changes to the level are resumed.)",
                    "Running AutoCompaction..."
                });

                if (RunConfig.BdsWatchdog)
                {
                    _bdsWatchdog = new Watchdog(bds);

                    _bdsWatchdog.RegisterHook((byte)Watchdog.Hook.LIMIT_REACHED, (object sender, EventArgs e) => 
                    {
                        SaveConfiguration(RunConfig, configPath);
                        System.Environment.Exit(1);
                    });
                }

                // Stop BDS gracefully on unhandled exceptions
                if (RunConfig.StopBdsOnException)
                {
                    System.AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
                    {
                        Console.WriteLine($"Stopping BDS due to an unhandled exception from vellum...\n{e.ExceptionObject.ToString()}");

                        if (bds.IsRunning)
                        {
                            bds.SendInput("stop");
                            bds.Process.WaitForExit();
                            bds.Close();
                        }
                    };
                }

                // Input thread
                _ioThread = new Thread(() =>
                {
                    while (_readInput)
                        inStream?.Invoke(Console.ReadLine());
                });
                _ioThread.Start();

                // Store current BDS version
                bds.RegisterMatchHandler(CommonRegex.Version, (object sender, MatchedEventArgs e) =>
                {
                    _bdsVersion = UpdateChecker.ParseVersion(e.Matches[0].Groups[1].Value, VersionFormatting.MAJOR_MINOR_REVISION_BUILD);
                });
                
                playerCount = 0;

                bool nextBackup = true;
                if (RunConfig.Backups.OnActivityOnly)
                {
                    nextBackup = false;

                    // Player connect/ disconnect messages
                    bds.RegisterMatchHandler(CommonRegex.PlayerConnected, (object sender, MatchedEventArgs e) =>
                    {
                        playerCount++;
                        nextBackup = true;
                    });

                    bds.RegisterMatchHandler(CommonRegex.PlayerDisconnected, (object sender, MatchedEventArgs e) =>
                    {
                        playerCount--;
                    });
                }
                #endregion

                string worldPath = Path.Join(bdsDirPath, "worlds", worldName);
                string tempWorldPath = Path.Join(Directory.GetCurrentDirectory(), _tempPath, worldName);

                _renderManager = new RenderManager(bds, RunConfig);
                _backupManager = new BackupManager(bds, RunConfig);

                #region PLUGIN LOADING
                if (Directory.Exists(pluginDirectory))
                {
                    Host host = new Host(ref RunConfig);

                    #region INTERNAL PLUGINS
                    host.AddPlugin(_backupManager);
                    host.AddPlugin(_renderManager);

                    if (RunConfig.BdsWatchdog)
                        host.AddPlugin(_bdsWatchdog);
                    #endregion
                    
                    if (host.LoadPlugins(pluginDirectory) > 0)
                    {
                        foreach (IPlugin plugin in host.GetPlugins())
                        {
                            if (plugin.PluginType == PluginType.EXTERNAL)
                                Console.WriteLine($"Loaded plugin \"{plugin.GetType().Name} v{UpdateChecker.ParseVersion(System.Reflection.Assembly.GetAssembly(plugin.GetType()).GetName().Version, VersionFormatting.MAJOR_MINOR_REVISION)}\"");
                        }
                    }
                }
                #endregion

                // Backup interval
                if (RunConfig.Backups.EnableBackups)
                {
                    System.Timers.Timer backupIntervalTimer = new System.Timers.Timer(RunConfig.Backups.BackupInterval * 60000);
                    backupIntervalTimer.AutoReset = true;
                    backupIntervalTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                    {
                        if (nextBackup)
                        {
                            InvokeBackup(worldPath, tempWorldPath);

                            if (RunConfig.Backups.OnActivityOnly && playerCount == 0)
                                nextBackup = false;
                        } else
                            Console.WriteLine("Skipping this backup because no players were online since the last one was taken...");
                    };
                    backupIntervalTimer.Start();

                    bds.RegisterMatchHandler("Starting Server", (object sender, MatchedEventArgs e) => {
                    // bds.RegisterMatchHandler(CommonRegex.ServerStarted, (object sender, MatchedEventArgs e) => {
                        if (RunConfig.Backups.StopBeforeBackup)
                        {
                            System.Timers.Timer backupNotificationTimer = new System.Timers.Timer((RunConfig.Backups.BackupInterval * 60000) - Math.Clamp(RunConfig.Backups.NotifyBeforeStop * 1000, 0, RunConfig.Backups.BackupInterval * 60000));
                            backupNotificationTimer.AutoReset = false;
                            backupNotificationTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                            {
                                bds.SendTellraw(String.Format("Shutting down server in {0} seconds to take a backup.", RunConfig.Backups.NotifyBeforeStop));
                            };
                            backupNotificationTimer.Start();
                        }
                    });
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

                if (backupOnStartup)
                {
                    // Create initial world backup
                    Console.WriteLine("Creating initial temporary copy of world directory...");
                    _backupManager.CreateWorldBackup(worldPath, tempWorldPath, true, false); // If "StopBeforeBackup" is set to "true" this will also automatically start the server when it's done
                }

                // Start server in case the BackupManager hasn't started it yet
                if (!bds.IsRunning)
                {
                    bds.Start();
                    bds.WaitForMatch(CommonRegex.ServerStarted); // Wait until BDS successfully started
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
                                        shutdownTimer.Close();

                                        SaveConfiguration(RunConfig, configPath);

                                        Console.WriteLine("vellum quit correctly");
                                        System.Environment.Exit(0);
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
                                        RunConfig = LoadConfiguration(configPath);
                                    } else
                                    {
                                        bds.SendInput(text);
                                    }
                                    result = true;
                                    break;

                                case "updatecheck":
                                    Console.WriteLine("Checking for updates...");

                                    // BDS
                                    UpdateChecker bdsUpdateChecker = new UpdateChecker(ReleaseProvider.HTML, "https://minecraft.net/en-us/download/server/bedrock/", @"https:\/\/minecraft\.azureedge\.net\/bin-" + (System.Environment.OSVersion.Platform == PlatformID.Win32NT ? "win" : "linux") + @"\/bedrock-server-(\d+\.\d+\.\d+(?>\.\d+)?)\.zip");
                                    if (bdsUpdateChecker.GetLatestVersion())
                                            Console.WriteLine(String.Format("Bedrock Server:\t{0} -> {1}\t({2})", UpdateChecker.ParseVersion(_bdsVersion, VersionFormatting.MAJOR_MINOR_REVISION_BUILD), UpdateChecker.ParseVersion(bdsUpdateChecker.RemoteVersion, VersionFormatting.MAJOR_MINOR_REVISION_BUILD), bdsUpdateChecker.RemoteVersion > _bdsVersion ? "outdated" : "up to date"));
                                    else
                                            Console.WriteLine("Could not check for Bedrock server updates...");

                                    // vellum
                                    if (_updateChecker.GetLatestVersion())
                                            Console.WriteLine(String.Format("vellum:\t\t{0} -> {1}\t({2})", UpdateChecker.ParseVersion(_localVersion, VersionFormatting.MAJOR_MINOR_REVISION), UpdateChecker.ParseVersion(_updateChecker.RemoteVersion, VersionFormatting.MAJOR_MINOR_REVISION), _updateChecker.RemoteVersion > _localVersion ? "outdated" : "up to date"));
                                    else
                                            Console.WriteLine("Could not check for vellum updates...");
                                    
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
                        Console.WriteLine("Could not execute vellum command \"{0}\". Please wait until all tasks have finished or enable \"BusyCommands\" in your \"{1}\".", text, configPath);
                    }
                };
            }
            else
            {
                Console.WriteLine("No previous configuration file found. Creating one...");

                using (StreamWriter writer = new StreamWriter(configPath))
                {
                    writer.Write(JsonConvert.SerializeObject(new RunConfiguration()
                    {
                        BdsBinPath = System.Environment.OSVersion.Platform != PlatformID.Win32NT ? "bedrock_server" : "bedrock_server.exe",
                        Backups = new BackupConfig()
                        {
                            EnableBackups = true,
                            StopBeforeBackup = false,
                            NotifyBeforeStop = 60,
                            ArchivePath = "./backups/",
                            BackupsToKeep = 10,
                            OnActivityOnly = false,
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
                            PapyrusOutputPath = "",
                            LowPriority = false
                        },
                        QuietMode = false,
                        HideStdout = true,
                        BusyCommands = true,
                        CheckForUpdates = true,
                        StopBdsOnException = true,
                        BdsWatchdog = true,
                        Plugins = new Dictionary<string, PluginConfig>() },
                        Formatting.Indented));
                }

                Console.WriteLine(String.Format("Done! Please edit the \"{0}\" file and restart this application.", configPath));
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
            Console.Write($"Loading configuration \"{configPath}\"... ");

            RunConfiguration runConfig;
            using (StreamReader reader = new StreamReader(Path.Join(Directory.GetCurrentDirectory(), configPath)))
            {
                runConfig = JsonConvert.DeserializeObject<RunConfiguration>(reader.ReadToEnd());
            }

            Console.WriteLine("Done!");

            return runConfig;
        }

        private static void SaveConfiguration(RunConfiguration runConfig, string configPath)
        {
            // In case of new plugins added by the user, plugin-specific run configuration entries
            // may have been added to its respective sub-section in the "Plugins"-key.
            // This means the current RunConfig has to be serialized once again.

            string runConfigJson = JsonConvert.SerializeObject(runConfig, Formatting.Indented);

            using (MD5 md5 = MD5.Create())
            {
                byte[] sourceHash = md5.ComputeHash(File.ReadAllBytes(Path.Join(Directory.GetCurrentDirectory(), configPath)));
                byte[] targetHash = md5.ComputeHash(Encoding.ASCII.GetBytes(runConfigJson));

                if (Convert.ToBase64String(sourceHash) != Convert.ToBase64String(targetHash))
                {
                    Console.Write("Internal run-configuration has been modified, saving...");

                    using (StreamWriter writer = new StreamWriter(File.Open(Path.Join(Directory.GetCurrentDirectory(), configPath), FileMode.Truncate)))
                        writer.Write(runConfigJson);

                    Console.WriteLine(" Done!");
                }
            }
        }
    }

    public static class CommonRegex
    {
        public const string Version = @"^.+ Version (\d+\.\d+\.\d+(?>\.\d+)?)";
        public const string ServerStarted = @"^.+ (Server started\.)";
        public const string PlayerConnected = @".+Player connected:\s(.+),";
        public const string PlayerDisconnected = @".+Player disconnected:\s(.+),";
    }
}
