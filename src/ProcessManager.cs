using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
namespace Vellum.Automation
{

    public class ProcessManager
    {

        public bool playerleft = true;
        public bool nextbackup = false;
        public Process Process { get; private set; }
        private ProcessStartInfo _startInfo;
        private string[] _ignorePatterns = new string[0];
        public Stack<string> _lastMessage = new Stack<string>();
        //private string _pattern;
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
                }
                catch
                {
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
        public ProcessManager(ProcessStartInfo startInfo, string[] ignorePatterns)
            : this(startInfo)
        {
            _ignorePatterns = ignorePatterns;
        }

        ///<summary>Starts the underlying process and begins reading it's output.</summary>
        public bool Start()
        {
            if (this.Process.Start())
            {
                this.Process.BeginOutputReadLine();
                return true;
            }
            else
            {
                return false;
            }
        }

        ///<summary>Frees the underlying process.</summary>
        public void Close()
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
        public bool WaitForMatch(string pattern)
        {
            bool ready = false;
            int count = -1;
            string current = "";
            int timeout = 0;
            while (!ready)
            {
                if (_lastMessage.Count > 0)
                {
                    current = _lastMessage.Pop();
                    count = Regex.Matches(current, pattern).Count;
                    ready = count >= 1 ? true : false;
                }
                else{
                   //Begin the timeout if the stack is empty
                   if(timeout > 2000){
                       Console.WriteLine("[DEBUG] Stack empty and timeout.");
                       _matchedText = current;
                       return true;
                   }
                }
                timeout++;
                System.Threading.Thread.Sleep(10);
            }

            _matchedText = current;
            return false;
        }
        public string GetMatchedText()
        {
            return _matchedText;
        }
        /*
 public void SetMatchPattern(string pattern)
        {
            HasMatched = false;
            _pattern = pattern;
        }
        */
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
                SendInput("tellraw @a {\"rawtext\":[{\"text\":\"§l[VELLUM]\"},{\"text\":\"§r " + message + "\"}]}");
#endif
            }
        }

        private void OutputTextReceived(object sender, DataReceivedEventArgs e)
        {
          
            _lastMessage.Push(e.Data);
            if(IsRunning){
            if (playerleft)
            { //do not run this while a backup is taken place since it would disrupt stdin. Since Processing is protected
                if (e.Data.Length >= 1)
                {
                    string player_left = Regex.Match(e.Data, @"(Player disconnected)").Value;
                    if (player_left.Length >= 1)
                    {
                        playerleft = false;
                        nextbackup = true;
                        //TODO: create a buffer for the Invoke command
                        Program.backupIntervalTimer.Interval = 30000; //~30 second delay at best incase of too many players leave which sets the Processing flag to true
                        Program.backupIntervalTimer.Start();

                    }
                
                }
            }
            }
            if (EnableConsoleOutput)
            {
                bool showMsg = true;

                if (_ignorePatterns.Length > 0)
                {
                    foreach (string pattern in _ignorePatterns)
                    {
                        if (!String.IsNullOrWhiteSpace(e.Data) && Regex.Matches(e.Data, pattern).Count > 0)
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
