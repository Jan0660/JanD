using System.Text.Json.Serialization;
using JanD.Lib.Objects;

namespace JanD.Lib;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JanDNewProcess))]
[JsonSerializable(typeof(JanDRuntimeProcess))]
[JsonSerializable(typeof(JanDRuntimeProcess[]))]
[JsonSerializable(typeof(SetPropertyIpcPacket))]
[JsonSerializable(typeof(IpcPacket))]
[JsonSerializable(typeof(DaemonStatus))]
[JsonSerializable(typeof(IpcClient.DaemonClientEvent))]
public partial class MyJsonContext : JsonSerializerContext
{
}