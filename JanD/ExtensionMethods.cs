﻿using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DaemonEvents = JanD.Daemon.DaemonEvents;

namespace JanD
{
    public static class ExtensionMethods
    {
        public static void WriteProcessInfo(this Utf8JsonWriter j, JanDProcess proc)
        {
            j.WriteString("Name", proc.Name);
            j.WriteString("Command", proc.Command);
            j.WriteString("WorkingDirectory", proc.WorkingDirectory);
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
            j.WriteBoolean("Enabled", proc.Enabled);
            j.WriteBoolean("AutoRestart", proc.AutoRestart);
            j.WriteBoolean("Running", proc.Process != null);
        }

        /// <summary>
        /// Writes <paramref name="str"/> to the stream encoded in Utf8
        /// </summary>
        /// <param name="pipeServer"></param>
        /// <param name="str"></param>
        public static void Write(this NamedPipeServerStream pipeServer, string str)
            => pipeServer.Write(Encoding.UTF8.GetBytes(str));

        public static string ToIpcString(this DaemonEvents daemonEvent)
            => daemonEvent switch
            {
                DaemonEvents.OutLog => "outlog",
                DaemonEvents.ErrLog => "errlog",
                DaemonEvents.ProcessStopped => "procstop",
                DaemonEvents.ProcessStarted => "procstart",
                DaemonEvents.ProcessAdded => "procadd",
                DaemonEvents.ProcessDeleted => "procdel"
            };
    }
}