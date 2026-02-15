using System.Collections.Generic;

public sealed class DayEndResult
{
    public readonly List<string> Logs = new();
    public void Log(string msg) => Logs.Add(msg);
}
