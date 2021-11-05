using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JanD
{
    public class IpcClient
    {
        public NamedPipeClientStream Stream;
        public const int BufferSize = 200_000;

        public IpcClient(string pipeName = null)
        {
            pipeName ??= Program.PipeName;
            Stream = new(".", pipeName, PipeDirection.InOut);
            var timeout = int.Parse(Environment.GetEnvironmentVariable("JAND_TIMEOUT") ?? "3000");
            try
            {
                Stream.Connect(timeout);
            }
            catch (TimeoutException)
            {
                Console.WriteLine($"Could not connect to pipe {pipeName} within {timeout} millisecond timeout.");
                Environment.Exit(1);
            }
        }

        public void SendString(string type, string data)
        {
            Stream.Write(JsonSerializer.SerializeToUtf8Bytes(new IpcPacket
            {
                Type = type,
                Data = data
            }));
        }

        public string RequestString(string type, string data)
        {
            SendString(type, data);
            return ReadString();
        }

        public T RequestJson<T>(string type, string data)
        {
            SendString(type, data);
            Span<byte> bytes = stackalloc byte[BufferSize];
            var count = Stream.Read(bytes);
            try
            {
                return JsonSerializer.Deserialize<T>(bytes[..count]);
            }
            catch
            {
                Console.WriteLine(Encoding.UTF8.GetString(bytes[..count]));
                return default;
            }
        }

        public DaemonStatus GetStatus()
        {
            SendString("status", "");
            Span<byte> bytes = stackalloc byte[BufferSize];
            var count = Stream.Read(bytes);
            return JsonSerializer.Deserialize<DaemonStatus>(bytes[..count]);
        }

        public string ReadString()
        {
            Span<byte> bytes = stackalloc byte[BufferSize];
            var count = Stream.Read(bytes);
            return Encoding.UTF8.GetString(bytes[..count]);
        }

        public void Write(string str)
            => SendString("write", str);

        public void DoRequests(Span<string> processes, string type)
        {
            foreach (var proc in processes)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(proc);
                Console.ResetColor();
                Console.Write(": ");
                Console.WriteLine(RequestString(type, proc));
            }
        }

        public string GetProcessName(string arg)
            => GetProcessNames(new[] { arg }).FirstOrDefault();

        public string[] GetProcessNames(ReadOnlySpan<string> args)
        {
            Program.JanDRuntimeProcess[] processes = null;

            Program.JanDRuntimeProcess[] ProcessList()
            {
                if (processes != null)
                    return processes;
                processes = RequestJson<Program.JanDRuntimeProcess[]>("get-processes", "");
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

                var ev = JsonSerializer.Deserialize<DaemonClientEvent>(bytes[..bytesCount]);
                action(ev);
            }
        }

        public class DaemonClientEvent
        {
            public string Event { get; set; }
            public string Process { get; set; }
            public string Value { get; set; }
        }
    }
}