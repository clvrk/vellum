namespace Vellum
{
    public class RunConfiguration
    {
        public string BdsBinPath;
        public BackupConfig Backups;
        public RenderConfig Renders;
        public bool QuietMode;
        public bool HideStdout;
        public bool BusyCommands;
        public bool CheckForUpdates;
        public bool StopBdsOnException;
        public bool BdsWatchdog;
        public System.Collections.Generic.Dictionary<string, PluginConfig> Plugins;
    }

    public class BackupConfig
    {
        public bool EnableBackups;
        public bool EnableSchedule;
        public string[] Schedule;
        public double BackupInterval;
        public string ArchivePath;
        public int BackupsToKeep;
        public bool OnActivityOnly;
        public bool StopBeforeBackup;
        public uint NotifyBeforeStop;
        public string PreExec;
        public string PostExec;
    }

    public class RenderConfig
    {
        public bool EnableRenders;
        public string PapyrusBinPath;
        public string PapyrusOutputPath;
        public double RenderInterval;
        public string PapyrusGlobalArgs;
        public string[] PapyrusTasks;
        public bool LowPriority;
    }

    public class PluginConfig
    {
        public bool Enable;
        public object Config;
    }
}
