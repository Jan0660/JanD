using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
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
                DaemonEvents.ProcessDeleted => "procdel",
                DaemonEvents.ProcessRenamed => "procren",
                _ => "invalid"
            };

        public static bool TryRemove<T>(this List<T> list, T item)
        {
            try
            {
                list.Remove(item);
                return true;
            }
            catch
            {
                return false;
            }
        }


        public static PropertyInfo GetPropertyCaseInsensitive(this Type type, string str)
            => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(p => p.Name.ToLower() == str);

        public static void SetValueString(this PropertyInfo property, object obj, string value)
        {
            if (property!.PropertyType == typeof(bool))
                property.SetValue(obj, bool.Parse(value));
            else if (property.PropertyType == typeof(int))
                property.SetValue(obj, int.Parse(value));
        }
    }
}