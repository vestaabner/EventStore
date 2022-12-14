using System;

namespace EventStore.Core.Exceptions
{
    public class CorruptDatabaseException : Exception
    {
        public CorruptDatabaseException(Exception inner) : base("Corrupt database detected.", inner) { }
    }
}
