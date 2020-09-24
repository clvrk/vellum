using System;
using System.Collections.Generic;
using Vellum.Extension;

namespace Vellum.Automation
{
    public class Watchdog : InternalPlugin
    {
        public uint RetryLimit = 3;
        private uint _failRetryCount = 0;

        #region PLUGIN
        public Version Version { get; }
        public enum Hook
        {
            CRASH,
            RETRY,
            LIMIT_REACHED,
            STABLE,
        }
        #endregion

        public Watchdog(ProcessManager processManager)
        {
            processManager.Process.EnableRaisingEvents = true;
            processManager.Process.Exited += (object sender, EventArgs e) =>
            {
                if (processManager.Process.ExitCode != 0)
                {
                    CallHook((byte)Hook.CRASH, new HookEventArgs() { Attachment = processManager.Process.ExitCode });

                    processManager.Close();

                    Console.WriteLine("BDS process unexpectedly exited");

                    if(++_failRetryCount <= RetryLimit)
                    {
                        Console.WriteLine($"Retry #{_failRetryCount} to start BDS process");
                        processManager.Start();
                        CallHook((byte)Hook.RETRY, new HookEventArgs() { Attachment = _failRetryCount });
                    }
                    else
                    {
                        Console.WriteLine("Maximum retry limit reached!");
                        CallHook((byte)Hook.LIMIT_REACHED, new HookEventArgs() { Attachment = _failRetryCount });
                    }
                }
            };

            processManager.RegisterMatchHandler(CommonRegex.ServerStarted, (object sender, MatchedEventArgs e) =>
            {
                CallHook((byte)Hook.STABLE, new HookEventArgs() { Attachment = _failRetryCount });
                _failRetryCount = 0;
            });
        }
    }
}