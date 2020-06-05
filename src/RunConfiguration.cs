using System;

namespace Vellum
{
    public struct RunConfiguration
    {
        public string BdsPath;
        public string WorldName;
        public string PapyrusBinPath;
        public string PapyrusGlobalArgs;
        public string[] PapyrusTasks;
        public string PapyrusOutputPath;
        public string ArchivePath;
        public int BackupsToKeep;
        public bool BackupOnStartup;
        public bool EnableRenders;
        public bool EnableBackups;
        public double RenderInterval;
        public double BackupInterval;
        public string PreExec;
        public string PostExec;
        public bool QuietMode;
        public bool HideStdout;
        public bool BusyCommands;
        public bool StopBeforeBackup;
        public uint NotifyBeforeStop;
        public bool CheckForUpdates;
    }
}
