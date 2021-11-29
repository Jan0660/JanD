namespace JanD.Lib.Objects;

public class JanDRuntimeProcess
{
    public string Name { get; set; }
    public string Filename { get; set; }
    public string[] Arguments { get; set; }
    public string WorkingDirectory { get; set; }
    public int ProcessId { get; set; }
    public bool Stopped { get; set; }
    public int ExitCode { get; set; }
    public int RestartCount { get; set; }
    public bool Enabled { get; set; }
    public bool AutoRestart { get; set; }
    public bool Running { get; set; }
    public bool Watch { get; set; }
    public int SafeIndex { get; set; }
}