using System;

namespace papyrus_automation
{
    public abstract class Manager
    {
        protected static String _tag = "[        PAPYRUS         ] ";
        protected static String _indent = "\t-> ";
        public bool Processing { get; protected set; } = false;
    }
}