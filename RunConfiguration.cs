using System;

namespace papyrus_automation
{
    public struct RunConfiguration
    {
        public String BdsPath;
        public String WorldName;
        public String PapyrusBinPath;
        public String PapyrusGlobalArgs;
        public String[] PapyrusTasks;
        public String PapyrusOutputPath;
        public String ArchivePath;
        public uint BackupsToKeep;
        public bool BackupOnStartup;
        public bool EnableRenders;
        public bool EnableBackups;
        public double RenderInterval;
        public double BackupInterval;
        public String PreExec;
        public String PostExec;
        public bool QuietMode;
        public bool HideStdout;
    }
}
