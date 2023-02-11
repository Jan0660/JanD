namespace JanD.Lib.Objects;

public class VacuumRequest
{
    public int KeepLines { get; set; }
    public string Process { get; set; }
    public int WhichStd { get; set; }
}

public enum WhichStd
{
    StdOut = 1,
    StdErr = 2,
    Both = 3
}