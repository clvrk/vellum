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
    public class VellumHost : Host
    {
        private const string _serverPropertiesPath = "server.properties";
        public const string TempPath = "temp/";
        private static string _pluginDirectory = "plugins/";
        private string _configPath = "configuration.json";
        public delegate void InputStreamHandler(string text);
        private static InputStreamHandler inStream;
        private static BackupManager _backupManager;
        private static RenderManager _renderManager;
        private static Watchdog _bdsWatchdog;

        private static UpdateChecker _updateChecker = new UpdateChecker(ReleaseProvider.GITHUB_RELEASES, @"https://api.github.com/repos/clarkx86/vellum/releases/latest", @"^v?(\d+)\.(\d+)\.(\d+)");
        private static uint playerCount;
        private static Version _localVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        public enum Hook
        {
            RELOAD_CONFIG,
            BACKUP_SCHEDULED,
            FORCE_BACKUP,
            FORCE_RENDER,
            EXIT_SCHEDULED,     // Gets invoked as soon as the user schedules a server shutdown
            EXIT_USER           // Gets invoked before exiting, given that the user manually requested a server shutdown using the "stop" command
        }

        static void Main(string[] args)
        {
            string suffix = "";

#if DEBUG
            suffix = " DEBUG";
#elif ALPHA
            suffix = " alpha";
#elif BETA
            suffix = " beta";
#endif

            Console.WriteLine("vellum v{0} build {1}\n{2}by clarkx86, DeepBlue & contributors\n", UpdateChecker.ParseVersion(_localVersion, VersionFormatting.MAJOR_MINOR_REVISION) + suffix, _localVersion.Build, new string(' ', 7));

            new VellumHost(args);
        }

        public VellumHost(string[] args)
        {
            Thread _ioThread;
            bool _readInput = true;

            bool printHelp = false;
            bool noStart = false;
            string restorePath = null;
            bool backupOnStartup = true;

            OptionSet options = new OptionSet() {
                { "h|help", "Displays a help screen.", v => { printHelp = v != null; } },
                { "c=|configuration=", "The configuration file to load settings from.", v => { if (!String.IsNullOrWhiteSpace(v)) _configPath = v.Trim(); } },
                { "p=|plugin-directory=", "The directory to scan for plugins.", v => { if (!String.IsNullOrWhiteSpace(v)) _pluginDirectory = v; } },
                { "r=|restore=", "Path to an archive to restore a backup from.", v => { if (!String.IsNullOrWhiteSpace(v)) restorePath = Path.GetFullPath(v); } },
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

            Version bdsVersion = new Version();

            if (File.Exists(_configPath))
            {
                // Load configuration
                RunConfig = LoadConfiguration(_configPath);

                string bdsDirPath = Path.GetDirectoryName(RunConfig.BdsBinPath);
                string worldName = "Bedrock level";

                Console.Write($"Reading \"{_serverPropertiesPath}\"... ");

                using (StreamReader reader = new StreamReader(File.OpenRead(Path.Join(bdsDirPath, _serverPropertiesPath))))
                    worldName = Regex.Match(reader.ReadToEnd(), @"^level\-name\=(.+)", RegexOptions.Multiline).Groups[1].Value.Trim();

                string worldPath = Path.Join(bdsDirPath, "worlds", worldName);
                string tempWorldPath = Path.Join(Directory.GetCurrentDirectory(), TempPath, worldName);

                Console.WriteLine("Done!");

                if (!String.IsNullOrWhiteSpace(restorePath))
                {
                    Console.WriteLine("\n\"--restore\" flag provided, attempting to restore backup from specified archive...");
                    BackupManager.Restore(restorePath, worldPath);
                    Console.WriteLine();

                    if (noStart)
                    {
                        Console.WriteLine("\"--no-start\" flag provided, exiting...");
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
                    }
                    else
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
                        SaveConfiguration(RunConfig, _configPath);
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
                    bdsVersion = UpdateChecker.ParseVersion(e.Matches[0].Groups[1].Value, VersionFormatting.MAJOR_MINOR_REVISION_BUILD);
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

                _renderManager = new RenderManager(bds, RunConfig);
                _backupManager = new BackupManager(bds, RunConfig);

                #region PLUGIN LOADING
                if (Directory.Exists(_pluginDirectory))
                {
                    SetPluginDirectory(_pluginDirectory);

                    #region INTERNAL PLUGINS
                    AddPlugin(bds);
                    AddPlugin(_backupManager);
                    AddPlugin(_renderManager);

                    if (RunConfig.BdsWatchdog)
                        AddPlugin(_bdsWatchdog);
                    #endregion

                    if (LoadPlugins() > 0)
                    {
                        foreach (IPlugin plugin in GetPlugins())
                        {
                            if (plugin.PluginType == PluginType.EXTERNAL)
                                Console.WriteLine($"Loaded plugin: {plugin.GetType().Name} v{UpdateChecker.ParseVersion(System.Reflection.Assembly.GetAssembly(plugin.GetType()).GetName().Version, VersionFormatting.MAJOR_MINOR_REVISION)}");
                        }

                        Console.WriteLine();
                    }
                }
                else
                {
                    Directory.CreateDirectory(_pluginDirectory);
                }
                #endregion

                // Scheduled/ interval backups
                if (RunConfig.Backups.EnableBackups)
                {
                    double interval = RunConfig.Backups.BackupInterval * 60000;

                    if (RunConfig.Backups.EnableSchedule)
                    {
                        if (RunConfig.Backups.Schedule.Length == 0)
                            Console.WriteLine("Scheduled backups are enabled but there are no entries in the schedule, switching to interval backups!");
                        else
                            interval = 1;
                    }

                    System.Timers.Timer backupIntervalTimer = new System.Timers.Timer(interval);
                    backupIntervalTimer.AutoReset = true;
                    backupIntervalTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                    {
                        if (backupIntervalTimer.Interval != 1) // This prevents performing a backup if scheduled backups are enabled and the next real time-span hasn't been determined yet
                        {
                            if (nextBackup)
                            {
                                if (RunConfig.Backups.OnActivityOnly && playerCount == 0)
                                    nextBackup = false;

                                InvokeBackup(worldPath, tempWorldPath);
                            }
                            else
                                Console.WriteLine("Skipping this backup because no players were online since the last one was taken...");
                        }

                        if (RunConfig.Backups.EnableSchedule && RunConfig.Backups.Schedule.Length > 0)
                        {                            
                            // Check which entry is up next
                            TimeSpan nextSpan = TimeSpan.MaxValue;
                            DateTime backupTime = DateTime.Now;

                            foreach (string clockTime in RunConfig.Backups.Schedule)
                            {
                                try
                                {
                                    backupTime = DateTime.Parse(clockTime);
                                }
                                catch (FormatException)
                                {
                                    Console.WriteLine($"Invalid schedule entry \"{clockTime}\" in \"{_configPath}\"!");
                                    continue;
                                }
                                // 2 second buffer to fix this timer sometimes jumping the gun by a few hundred milliseconds -TH
                                if (backupTime < DateTime.Now.AddSeconds(2)) backupTime = backupTime.AddDays(1);
                                TimeSpan span = backupTime.Subtract(DateTime.Now);
                                if (span.TotalSeconds > 2 && span < nextSpan) nextSpan = span;                                   
                            }

                            // Set the new interval
                            Console.WriteLine($"Next scheduled backup is at {(DateTime.Now + nextSpan).ToShortTimeString()} (in {nextSpan.Days} days, {nextSpan.Hours} hours, {nextSpan.Minutes} minutes and {nextSpan.Seconds} seconds)");
                            backupIntervalTimer.Interval = nextSpan.TotalMilliseconds;
                            CallHook((byte)Hook.BACKUP_SCHEDULED, new HookEventArgs() { Attachment = nextSpan });
                        }
                    };

                    backupIntervalTimer.Start();

                    bds.RegisterMatchHandler("Starting Server", (object sender, MatchedEventArgs e) =>
                    {
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
                                                        CallHook((byte)Hook.FORCE_BACKUP);
                                                        break;

                                                    case "render":
                                                        InvokeRender(worldPath, tempWorldPath);
                                                        result = true;
                                                        CallHook((byte)Hook.FORCE_RENDER);
                                                        break;
                                                }
                                                break;
                                        }
                                    }
                                    break;

                                case "stop":
                                    System.Timers.Timer shutdownTimer = new System.Timers.Timer();
                                    shutdownTimer.AutoReset = false;
                                    shutdownTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                                    {
                                        // _renderManager.Abort();
                                        _bdsWatchdog.Disable();
                                        bds.SendInput("stop");
                                        bds.Process.WaitForExit();
                                        bds.Close();
                                        _readInput = false;
                                        shutdownTimer.Close();

                                        SaveConfiguration(RunConfig, _configPath);

                                        CallHook((byte)Hook.EXIT_USER);

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
                                            CallHook((byte)Hook.EXIT_SCHEDULED, new HookEventArgs() { Attachment = interval });
                                        }
                                        catch
                                        {
                                            Console.WriteLine("Could not schedule shutdown because \"{0}\" is not a valid number.", cmd[1].Captures[0].Value);
                                            result = false;
                                        }
                                    }
                                    else
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
                                    bool isVellumConfig = false;

                                    if (cmd.Count == 2 && cmd[1].Captures[0].Value == "vellum")
                                    {
                                        RunConfig = LoadConfiguration(_configPath);
                                        isVellumConfig = true;
                                    }
                                    else
                                    {
                                        bds.SendInput(text);
                                    }
                                    result = true;
                                    CallHook((byte)Hook.RELOAD_CONFIG, new HookEventArgs() { Attachment = isVellumConfig });
                                    break;

                                case "updatecheck":
                                    Console.WriteLine("Checking for updates...");

                                    // BDS
                                    UpdateChecker bdsUpdateChecker = new UpdateChecker(ReleaseProvider.HTML, "https://minecraft.net/en-us/download/server/bedrock/", @"https:\/\/minecraft\.azureedge\.net\/bin-" + (System.Environment.OSVersion.Platform == PlatformID.Win32NT ? "win" : "linux") + @"\/bedrock-server-(\d+\.\d+\.\d+(?>\.\d+)?)\.zip");
                                    if (bdsUpdateChecker.GetLatestVersion())
                                        Console.WriteLine(String.Format("Bedrock Server:\t{0} -> {1}\t({2})", UpdateChecker.ParseVersion(bdsVersion, VersionFormatting.MAJOR_MINOR_REVISION_BUILD), UpdateChecker.ParseVersion(bdsUpdateChecker.RemoteVersion, VersionFormatting.MAJOR_MINOR_REVISION_BUILD), UpdateChecker.CompareVersions(bdsVersion, bdsUpdateChecker.RemoteVersion, VersionFormatting.MAJOR_MINOR_REVISION_BUILD) < 0 ? "outdated" : "up to date"));
                                    else
                                        Console.WriteLine("Could not check for Bedrock server updates...");

                                    // vellum
                                    if (_updateChecker.GetLatestVersion())
                                        Console.WriteLine(String.Format("vellum:\t\t{0} -> {1}\t({2})", UpdateChecker.ParseVersion(_localVersion, VersionFormatting.MAJOR_MINOR_REVISION), UpdateChecker.ParseVersion(_updateChecker.RemoteVersion, VersionFormatting.MAJOR_MINOR_REVISION), UpdateChecker.CompareVersions(_localVersion, _updateChecker.RemoteVersion, VersionFormatting.MAJOR_MINOR_REVISION) < 0 ? "outdated" : "up to date"));
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
                        Backups = new BackupConfig()
                        {
                            EnableBackups = true,
                            EnableSchedule = true,
                            Schedule = new string[] {
                                "00:00",
                                "06:00",
                                "12:00",
                                "18:00"
                            },
                            BackupInterval = 60,
                            ArchivePath = "./backups/",
                            StopBeforeBackup = false,
                            NotifyBeforeStop = 60,
                            BackupsToKeep = 10,
                            OnActivityOnly = false,
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
                        Plugins = new Dictionary<string, PluginConfig>()
                    },
                        Formatting.Indented));
                }

                Console.WriteLine(String.Format("Done! Please edit the \"{0}\" file and restart this application.", _configPath));
            }
        }

        public void InvokeBackup(string worldPath, string tempWorldPath)
        {
            if (!_backupManager.Processing)
            {
                _backupManager.CreateWorldBackup(worldPath, tempWorldPath, false, true);
            }
            else
            {
                if (!RunConfig.QuietMode) { Console.WriteLine("A backup task is still running."); }
            }
        }

        public void InvokeRender(string worldPath, string tempWorldPath)
        {
            if (!_backupManager.Processing && !_renderManager.Processing)
            {
                _backupManager.CreateWorldBackup(worldPath, tempWorldPath, false, false);
                _renderManager.Start(tempWorldPath);
            }
            else
            {
                if (!RunConfig.QuietMode) { Console.WriteLine("A render task is still running."); }
            }
        }

        private static RunConfiguration LoadConfiguration(string _configPath)
        {
            Console.Write($"Loading configuration \"{_configPath}\"... ");

            RunConfiguration runConfig;
            using (StreamReader reader = new StreamReader(Path.Join(Directory.GetCurrentDirectory(), _configPath)))
            {
                runConfig = JsonConvert.DeserializeObject<RunConfiguration>(reader.ReadToEnd());
            }

            Console.WriteLine("Done!");

            return runConfig;
        }

        private static void SaveConfiguration(RunConfiguration runConfig, string _configPath)
        {
            // In case of new plugins added by the user, plugin-specific run configuration entries
            // may have been added to its respective sub-section in the "Plugins"-key.
            // This means the current RunConfig has to be serialized once again.

            string runConfigJson = JsonConvert.SerializeObject(runConfig, Formatting.Indented);

            using (MD5 md5 = MD5.Create())
            {
                byte[] sourceHash = md5.ComputeHash(File.ReadAllBytes(Path.Join(Directory.GetCurrentDirectory(), _configPath)));
                byte[] targetHash = md5.ComputeHash(Encoding.UTF8.GetBytes(runConfigJson));

                // System.Console.WriteLine(Convert.ToBase64String(sourceHash));
                // System.Console.WriteLine(Convert.ToBase64String(targetHash));

                if (Convert.ToBase64String(sourceHash) != Convert.ToBase64String(targetHash))
                {
                    Console.Write("Internal run-configuration has been modified, saving...");

                    using (StreamWriter writer = new StreamWriter(File.Open(Path.Join(Directory.GetCurrentDirectory(), _configPath), FileMode.Truncate)))
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
