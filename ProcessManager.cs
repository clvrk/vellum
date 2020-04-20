using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace papyrus.Automation
{
    public class ProcessManager
    {
        private Process _process;
        private string _lastMessage = "";
        private Regex _pattern = null;
        public bool HasMatched { get; private set; } = false;
        private string _matchedText;
        public bool EnableConsoleOutput { get; set; } = true;

        public ProcessManager(Process p)
        {
            _process = p;
            p.OutputDataReceived += OutputTextReceived;
        }

        public void SendInput(string text)
        {
            _process.StandardInput.Write(text + "\n");
        }

        private void OutputTextReceived(object sender, DataReceivedEventArgs e)
        {
            _lastMessage = e.Data;

            if (!HasMatched && _pattern != null)
            {
                if (_pattern.Matches(e.Data).Count >= 1)
                {
                    HasMatched = true;
                    _pattern = null;
                    _matchedText = e.Data;
                }
            }

            if (EnableConsoleOutput)
            {
                Console.WriteLine(e.Data);
            }
        }

        public int WaitForMatch(Regex regex, uint minMatches)
        {
            bool ready = false;
            int count = -1;

            while (!ready)
            {
                count = regex.Matches(_lastMessage).Count;
                ready = count >= minMatches ? true : false;
            }

            return count;
        }

        public void SetMatchPattern(Regex regex)
        {
            HasMatched = false;
            _pattern = regex;
        }

        public string GetMatchedText()
        {
            return _matchedText;
        }

        public static void RunCustomCommand(string cmd)
        {
            string shell = "";
            string args = "";
            switch ((int)System.Environment.OSVersion.Platform)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                    shell = @"C:\Windows\System32\cmd.exe";
                    args = "/k \"" + cmd + "\"";
                    break;
                case 4:
                    shell = "/bin/bash";
                    args = "-c \"" + cmd + "\"";
                    break;
            }

            Process p = new Process();
            p.StartInfo.FileName = shell;
            p.StartInfo.Arguments = args;

            p.Start();
            p.WaitForExit();
        }

        public void SendTellraw(string message)
        {
            if (!Program.RunConfig.QuietMode)
            {
                #if !IS_LIB
                SendInput("tellraw @a {\"rawtext\":[{\"text\":\"§l[PAPYRUS]\"},{\"text\":\"§r " + message + "\"}]}\n");
                #endif
            }                
        }
    }
}
