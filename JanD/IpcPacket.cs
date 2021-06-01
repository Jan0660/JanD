namespace JanD
{
    public class IpcPacket
    {
        public string Type { get; set; }
        public string Data { get; set; }
    }

    public class SetPropertyIpcPacket
    {
        public string Process { get; set; }
        public string Property { get; set; }
        public string Data { get; set; }
    }
}