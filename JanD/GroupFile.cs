namespace JanD
{
    public class GroupFile
    {
        public GroupFileProcess[] Processes { get; set; }
    }

    public class GroupFileProcess : Daemon.JanDNewProcess
    {
        public bool Watch { get; set; }
        public bool AutoRestart { get; set; }
        public bool Enabled { get; set; }
        public GroupFileProcess(string name, string filename, string[] arguments, string workingDirectory) : base(name, filename, arguments,
            workingDirectory)
        {
        }
    }
}