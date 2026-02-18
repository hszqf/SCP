using System.Collections.Generic;

public sealed class DayPipelineResult
{
    public readonly List<string> Logs = new();
    public void Log(string msg) => Logs.Add(msg);
}
