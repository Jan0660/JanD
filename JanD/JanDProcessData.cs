using System;

namespace JanD;

public class JanDProcessData
{
    public string Name { get; set; }

    /// <summary>
    /// DEPRECATED. Use Filename and Arguments instead.
    /// </summary>
    [Obsolete]
    public string Command { get; set; }

    public string Filename { get; set; }
    public string[] Arguments { get; set; }
    public string WorkingDirectory { get; set; }
    public bool AutoRestart { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public bool Watch { get; set; }
}