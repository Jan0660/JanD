namespace JanD.Lib.Objects;

public class JanDNewProcess
{
    public string Name { get; set; }
    public string Filename { get; set; }
    public string[] Arguments { get; set; }
    public string WorkingDirectory { get; set; }

    public JanDNewProcess(string name, string filename, string[] arguments, string workingDirectory) =>
        (Name, Filename, Arguments, WorkingDirectory) = (name, filename, arguments, workingDirectory);
}