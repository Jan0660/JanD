using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JanD.Lib.Objects;

namespace JanD.Lib
{
    public class IpcClient
    {
        public const string DefaultPipeName = "jand";
        public readonly NamedPipeClientStream Stream;
        public const int BufferSize = 200_000;

        
        /// <exception cref="TimeoutException"><paramref name="timeout"/> has been exceeded.</exception>
        public IpcClient(string pipeName = DefaultPipeName, int timeout = 3000)
        {
            Stream = new(".", pipeName, PipeDirection.InOut);
            Stream.Connect(timeout);
        }

        public void SendString(string type, string? data = null)
        {
            Stream.Write(JsonSerializer.SerializeToUtf8Bytes(new IpcPacket
            {
                Type = type,
                Data = data
            }, typeof(IpcPacket), MyJsonContext.Default));
        }

        public string RequestString(string type, string? data = null)
        {
            SendString(type, data);
            return ReadString();
        }

        /// <exception cref="JanDClientException">An exception has occured during deserialization, e.g. an error from the daemon.</exception>
        public T RequestJson<T>(string type, string? data = null)
        {
            SendString(type, data);
            Span<byte> bytes = stackalloc byte[BufferSize];
            var count = Stream.Read(bytes);
            try
            {
                return (T)JsonSerializer.Deserialize(bytes[..count], typeof(T), MyJsonContext.Default)!;
            }
            catch
            {
                throw new JanDClientException(Encoding.UTF8.GetString(bytes[..count]));
            }
        }

        public string ReadString()
        {
            Span<byte> bytes = stackalloc byte[BufferSize];
            var count = Stream.Read(bytes);
            return Encoding.UTF8.GetString(bytes[..count]);
        }

        public void WriteProcessName(string proc)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(proc);
            Console.ResetColor();
            Console.Write(": ");
        }

        public void DoRequests(Span<string> processes, string type)
        {
            foreach (var proc in processes)
            {
                WriteProcessName(proc);
                Console.WriteLine(RequestString(type, proc));
            }
        }

        // todo: null-safe usages, change return to string?
        public string GetProcessName(string arg)
            => GetProcessNames(new[] { arg }).FirstOrDefault();

        public string[] GetProcessNames(ReadOnlySpan<string> args)
        {
            JanDRuntimeProcess[] processes = null;

            JanDRuntimeProcess[] ProcessList()
            {
                if (processes != null)
                    return processes;
                processes = RequestJson<JanDRuntimeProcess[]>("get-processes", "");
                return processes;
            }

            var result = new List<string>();
            foreach (var arg in args)
            {
                if (Regex.IsMatch(arg, "^\\d"))
                {
                    var index = int.Parse(arg);
                    var proc = ProcessList().FirstOrDefault(p => p.SafeIndex == index);
                    result.Add(proc?.Name ?? arg);
                }
                else if (arg.StartsWith('/') && arg.EndsWith('/'))
                {
                    foreach (var proc in ProcessList())
                        if (Regex.IsMatch(proc.Name, arg[1..^1]))
                            result.Add(proc.Name);
                }
                else
                    result.Add(arg);
            }

            return result.ToArray();
        }

        [DoesNotReturn]
        public void ListenEvents(Action<DaemonClientEvent> action)
        {
            byte[] bytes = new byte[100_000];
            while (true)
            {
                var bytesCount = 0;
                var hitNewline = true;
                // Read bytes until we hit a real newline
                while (hitNewline)
                {
                    var byt = Stream.ReadByte();
                    if (byt == -1)
                        throw new Exception("Pipe closed.");

                    bytes[bytesCount] = (byte)byt;
                    if ((char)byt == '\n')
                        hitNewline = false;
                    bytesCount++;
                }

                var ev = (DaemonClientEvent)JsonSerializer.Deserialize(bytes[..bytesCount], typeof(DaemonClientEvent), MyJsonContext.Default);
                action(ev);
            }
        }

        public class DaemonClientEvent
        {
            public string Event { get; set; }
            public string Process { get; set; }
            public string Value { get; set; }
        }

        public string Vacuum(VacuumRequest request)
            => RequestString("vacuum", JsonSerializer.Serialize(request, typeof(VacuumRequest), MyJsonContext.Default));
    }
}