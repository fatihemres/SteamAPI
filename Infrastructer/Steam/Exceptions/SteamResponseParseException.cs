using System;

namespace Infrastructer.Steam.Exceptions
{
    public class SteamResponseParseException : Exception
    {
        public SteamResponseParseException(string message) : base(message)
        {
        }

        public SteamResponseParseException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
