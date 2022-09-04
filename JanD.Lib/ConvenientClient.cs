using System.Text.Json;
using JanD.Lib.Objects;

namespace JanD.Lib;

/// <summary>
/// Request extension methods for <see cref="JanD.Lib.IpcClient"/>
/// </summary>
public static class ConvenientClient
{
    /// <exception cref="JanDClientException">An error has been received from the daemon.</exception>
    public static string Request(this IpcClient client, string type, string? data = null)
    {
        var str = client.RequestString(type, data);
        if (str.StartsWith("ERR:"))
            throw new JanDClientException(str);
        return str;
    }

    public static DaemonStatus GetStatus(this IpcClient client)
        => client.RequestJson<DaemonStatus>("status");
    public static JanDRuntimeProcess[] GetProcesses(this IpcClient client)
        => client.RequestJson<JanDRuntimeProcess[]>("get-processes");
    public static JanDRuntimeProcess GetProcessInfo(this IpcClient client, string processName)
        => client.RequestJson<JanDRuntimeProcess>("get-process-info", processName);
    public static string FlushAllLogs(this IpcClient client)
        => client.Request("flush-all-logs");
    public static string SaveConfig(this IpcClient client)
        => client.Request("save-config");
    public static string SetConfig(this IpcClient client, string key, string value)
        => client.Request("set-config", $"{key}:{value}");

    #region Process

    public static string NewProcess(this IpcClient client, JanDNewProcess process)
        => client.Request("new-process", JsonSerializer.Serialize(process, typeof(JanDNewProcess), MyJsonContext.Default));

    public static string StartProcess(this IpcClient client, string name)
        => client.Request("start-process", name);

    public static string RenameProcess(this IpcClient client, string oldName, string newName)
        => client.Request("rename-process", $"{oldName}:{newName}");

    public static string SetProcessProperty(this IpcClient client, SetPropertyIpcPacket packet)
        => client.Request("set-process-property", JsonSerializer.Serialize(packet, typeof(SetPropertyIpcPacket), MyJsonContext.Default));

    #endregion

    #region Events

    public static string SubscribeEvents(this IpcClient client, string events)
        => client.Request("subscribe-events", events);

    public static string SubscribeLogEvent(this IpcClient client, string processName)
        => client.Request("subscribe-log-event", processName);

    [Obsolete("Use SubscribeLogEvent instead")]
    public static string SubscribeOutLogEvent(this IpcClient client, string processName)
        => client.Request("subscribe-outlog-event", processName);

    [Obsolete("Use SubscribeLogEvent instead")]
    public static string SubscribeErrLogEvent(this IpcClient client, string processName)
        => client.Request("subscribe-errlog-event", processName);

    #endregion
}