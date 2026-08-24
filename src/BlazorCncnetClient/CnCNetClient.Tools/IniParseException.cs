using System;

namespace CnCNetClient.Tools;

public class IniParseException : Exception
{
    public IniParseException(string message) : base(message)
    {
    }
}
