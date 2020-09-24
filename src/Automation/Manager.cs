using System;
using Vellum.Extension;

namespace Vellum.Automation
{
    public abstract class Manager : InternalPlugin
    {
        protected static string _tag = "[         VELLUM         ] ";
        protected static string _indent = "\t-> ";
        public bool Processing { get; protected set; } = false;

        protected static void Log(string text)
        {
            #if !IS_LIB
            Console.WriteLine(text);
            #endif
        }
    }
}