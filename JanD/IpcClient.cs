using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JanD
{
    public unsafe class IpcClient
    {
        public NamedPipeClientStream Stream;
        public const int BufferSize = 50_000;

        public IpcClient(string pipeName = null)
        {
            pipeName ??= Program.PipeName;
            Stream = new(".", pipeName, PipeDirection.InOut);
            Stream.Connect();
        }

        public void SendString(string type, string data)
        {
            Stream.Write(JsonSerializer.SerializeToUtf8Bytes(new IpcPacket()
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
            return JsonSerializer.Deserialize<T>(bytes[..Stream.Read(bytes)]);
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

        public void DoRequests(string[] processes, string type)
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

        public string[] GetProcessNames(string[] args)
        {
            string[] processNames = null;

            string[] GetProcesses()
            {
                if (processNames != null)
                    return processNames;
                var processes = RequestJson<JanDProcess[]>("get-process-list", "");
                var ls = new List<string>(processes.Length);
                foreach (var proc in processes)
                    ls.Add(proc.Name);
                processNames = ls.ToArray();
                return processNames;
            }

            var result = new List<string>();
            foreach (var arg in args)
            {
                if (arg.StartsWith('/') && arg.EndsWith('/'))
                {
                    var rgx = new Regex(arg[1..^1]);
                    foreach (var proc in GetProcesses())
                        if (rgx.IsMatch(proc))
                            result.Add(proc);
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
                var inDepth = 1;
                var firstRead = true;
                while (inDepth != 0)
                {
                    var byt = Stream.ReadByte();
                    if (byt == -1)
                    {
                        throw new Exception("Pipe closed.");
                    }

                    bytes[bytesCount] = (byte) byt;
                    bytesCount++;
                    var ch = (char) byt;
                    if (ch == '{' && !firstRead)
                        inDepth++;
                    if (ch == '}')
                    {
                        inDepth--;
                    }

                    firstRead = false;
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