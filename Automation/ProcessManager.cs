using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace papyrus.Automation
{
    public class ProcessManager
    {
        public Process Process { get; private set; }
        private ProcessStartInfo _startInfo;
        private string[] _ignoreMessages = new string[0];
        private string _lastMessage = "";
        private Regex _pattern = null;
        public bool HasMatched { get; private set; } = false;
        private string _matchedText;
        public bool EnableConsoleOutput { get; set; } = true;

        public bool IsRunning
        {
            get
            {
                bool result;
                try
                {
                    result = Process.HasExited ? false : true;
                } catch {
                    result = false;
                }
                return result;
            }
        }

        ///<param name="startInfo">Start configuration for the process.</param>
        public ProcessManager(ProcessStartInfo startInfo)
        {
            _startInfo = startInfo;
            this.Process = new Process();
            this.Process.StartInfo = startInfo;
            this.Process.StartInfo.RedirectStandardInput = true;
            this.Process.StartInfo.RedirectStandardOutput = true;
            this.Process.StartInfo.UseShellExecute = false;
            this.Process.OutputDataReceived += OutputTextReceived;
        }

        ///<summary>Starts the underlying process and begins reading it's output.</summary>
        ///<param name="startInfo">Start configuration for the process.</param>
        ///<param name="ignoreMessages">Array of messages that should not be redirected when written to the underlying processes stdout.</param>
        public ProcessManager(ProcessStartInfo startInfo, string[] ignoreMessages)
            : this(startInfo)
        {
            _ignoreMessages = ignoreMessages;
        }

        ///<summary>Starts the underlying process and begins reading it's output.</summary>
        public bool Start()
        {
            if (this.Process.Start())
            {
                this.Process.BeginOutputReadLine();
                return true;
            } else {
                return false;
            }
        }

        ///<summary>Stops the underlying process.</summary>
        public void Stop()
        {
            this.Process.CancelOutputRead();
            this.Process.Close();
        }

        ///<summary>Sends a command to the underlying processes stdin and executes it.</summary>
        ///<param name="cmd">Command to execute.</param>
        public void SendInput(string cmd)
        {
            this.Process.StandardInput.Write(cmd + "\n");
        }

        ///<summary>Halt program flow until the specified regex pattern has matched in the underlying processes stdout.</summary>
        public void WaitForMatch(Regex regex)
        {
            bool ready = false;
            int count = -1;

            while (!ready)
            {
                count = regex.Matches(_lastMessage).Count;
                ready = count >= 1 ? true : false;
            }
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

        ///<summary>Executes a custom command in the operating systems shell.</summary>
        ///<param name="cmd">Command to execute.</param>
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
            if (IsRunning && !Program.RunConfig.QuietMode)
            {
                #if !IS_LIB
                SendInput("tellraw @a {\"rawtext\":[{\"text\":\"§l[PAPYRUS]\"},{\"text\":\"§r " + message + "\"}]}");
                #endif
            }                
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
                bool showMsg = true;

                if (_ignoreMessages.Length > 0)
                {
                    foreach (string msg in _ignoreMessages)
                    {
                        if (msg == e.Data)
                        {
                            showMsg = false;
                            break;
                        }
                    }
                }

                if (showMsg) { Console.WriteLine(e.Data); }
            }
        }
    }
}
