using System;

namespace Vellum.Automation
{
    public abstract class Manager
    {
        protected static string _tag = "[        PAPYRUS         ] ";
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