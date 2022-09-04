using System.Text.Json.Serialization;

namespace JanD
{
    public class GroupFile
    {
        public GroupFileProcess[] Processes { get; set; }
    }

    public class GroupFileProcess : JanDNewProcess
    {
        public bool Watch { get; set; }
        public bool AutoRestart { get; set; } = true;
        public bool Enabled { get; set; } = true;

        public GroupFileProcess(string name, string filename, string[] arguments, string workingDirectory) : base(name,
            filename, arguments,
            workingDirectory)
        {
        }
    }
    
    [JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GroupFile))]
    public partial class GroupFileJsonContext : JsonSerializerContext
    {
    }
}