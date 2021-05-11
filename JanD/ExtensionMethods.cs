using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace JanD
{
    public static class ExtensionMethods
    {
        public static void WriteProcessInfo(this Utf8JsonWriter j, JanDProcess proc)
        {
            j.WriteString("Name", proc.Name);
            try
            {
                j.WriteNumber("ProcessId", proc.Process?.Id ?? -1);
            }
            catch
            {
                j.WriteNumber("ProcessId", -1);
            }
            j.WriteBoolean("Stopped", proc.Stopped);
            j.WriteNumber("ExitCode", proc.ExitCode);
            j.WriteNumber("RestartCount", proc.RestartCount);
        }

        /// <summary>
        /// Writes <paramref name="str"/> to the stream encoded in Utf8
        /// </summary>
        /// <param name="pipeServer"></param>
        /// <param name="str"></param>
        public static void Write(this NamedPipeServerStream pipeServer, string str)
            => pipeServer.Write(Encoding.UTF8.GetBytes(str));
    }
}