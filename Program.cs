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

                #region BDS process and input thread
                // BDS
                ProcessStartInfo serverStartInfo = new ProcessStartInfo() {
                    FileName = Path.Join(RunConfig.BdsPath, System.Environment.OSVersion.Platform == PlatformID.Unix ? "bedrock_server" : "bedrock_server.exe"),
                    WorkingDirectory = RunConfig.BdsPath
                };

                // Set environment variable for linux-based systems
                if (System.Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    serverStartInfo.EnvironmentVariables.Add("LD_LIBRARY_PATH", RunConfig.BdsPath);
                }

                ProcessManager bds = new ProcessManager(serverStartInfo);

                // Input thread
                inStream = (string text) =>
                {
                    if (!_backupManager.Processing && !_renderManager.Processing)
                    {
                        if (text.ToLower() == "quit")
                        {
                            bds.SendInput("stop");
                            bds.Process.WaitForExit();
                            // _ioThread.Abort(); // Not supported on linux?
                        }
                        else
                        {
                            bds.SendInput(text);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Could not execute \"{0}\". Please wait until all tasks have finished.", text);
                    }
                };

                // Input thread
                _ioThread = new Thread(new ThreadStart(() =>
                {
                    while (true)
                    {
                        inStream?.Invoke(Console.ReadLine());
                    }
                }));
                _ioThread.Start();
                #endregion

                string worldPath = Path.Join(RunConfig.BdsPath, "worlds", RunConfig.WorldName);
                string tempWorldPath = Path.Join(Directory.GetCurrentDirectory(), _tempPath, RunConfig.WorldName);

                _backupManager = new BackupManager(bds, RunConfig);

                if (RunConfig.BackupOnStartup)
                {
                    // Create initial world backup
                    Console.WriteLine("Creating initial world backup...");
                    _backupManager.CreateWorldBackup(worldPath, tempWorldPath, true, false);
                }

                // // Run server
                // Console.WriteLine("Attempting to start server process...");
                // if (bds.Start())
                // {
                //     bds.BeginOutputReadLine();
                    
                //     Console.WriteLine("Process started!\n");
                    
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
                            if (!_backupManager.Processing)
                            {
                                _backupManager.CreateWorldBackup(worldPath, tempWorldPath, false, true);
                            }
                            else
                            {
                                if (!Program.RunConfig.QuietMode) { Console.WriteLine("A backup task is still running."); }
                            }

                        };
                        backupIntervalTimer.Start();

                        if (RunConfig.StopBeforeBackup)
                        {
                            System.Timers.Timer backupNotificationTimer = new System.Timers.Timer((RunConfig.BackupInterval * 60000) - (RunConfig.TimeBeforeStop * 1000));
                            backupNotificationTimer.AutoReset = true;
                            backupNotificationTimer.Elapsed += (object sender, ElapsedEventArgs e) => {
                                bds.SendTellraw(String.Format("Shutting down server in {0} seconds to take a backup.", RunConfig.TimeBeforeStop));
                            };
                        }
                    }

                    // Render interval
                    if (RunConfig.EnableRenders)
                    {
                        _renderManager = new RenderManager(bds, RunConfig);

                        System.Timers.Timer renderIntervalTimer = new System.Timers.Timer(RunConfig.RenderInterval * 60000);
                        renderIntervalTimer.AutoReset = true;
                        renderIntervalTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                        {
                            if (!_backupManager.Processing && !_renderManager.Processing)
                            {
                                _backupManager.CreateWorldBackup(worldPath, tempWorldPath, false, false);
                                _renderManager.StartRender(tempWorldPath);
                            }
                            else
                            {
                                if (!Program.RunConfig.QuietMode) { Console.WriteLine("A render task is still running."); }
                            }
                        };
                        renderIntervalTimer.Start();
                    }
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
                        StopBeforeBackup = false,
                        TimeBeforeStop = 60
                    }, Formatting.Indented));
                }

                Console.WriteLine(String.Format("Done! Please edit the \"{0}\" file and restart this application.", _configFname));
            }
        }
    }
}
