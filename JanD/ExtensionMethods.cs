﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace JanD
{
    public static class ExtensionMethods
    {
        public static void WriteProcessInfo(this Utf8JsonWriter j, JanDProcess proc)
        {
            j.WriteString("Name", proc.Data.Name);
            j.WriteString("Filename", proc.Data.Filename);
            j.WritePropertyName("Arguments");
            j.WriteStartArray();
            foreach (var argument in proc.Data.Arguments)
            {
                j.WriteStringValue(argument.AsSpan());
            }
            j.WriteEndArray();
            j.WriteString("WorkingDirectory", proc.Data.WorkingDirectory);
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
            j.WriteBoolean("Enabled", proc.Data.Enabled);
            j.WriteBoolean("AutoRestart", proc.Data.AutoRestart);
            j.WriteBoolean("Running", proc.Process != null);
            j.WriteBoolean("Watch", proc.Data.Watch);
            j.WriteNumber("SafeIndex", proc.SafeIndex);
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
                DaemonEvents.ProcessPropertyUpdated => "procprop",
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
                .FirstOrDefault(p => p.Name.ToLower() == str.ToLower());

        public static void SetValueString(this PropertyInfo property, object obj, string value)
        {
            if (property!.PropertyType == typeof(bool))
                property.SetValue(obj, bool.Parse(value));
            else if (property.PropertyType == typeof(int))
                property.SetValue(obj, int.Parse(value));
            else if (property.PropertyType == typeof(string))
                property.SetValue(obj, value);
        }

        public static string ToFullPath(this string path) => path.StartsWith('.') ? Path.GetFullPath(path) : path;

        public static string[] FixArguments(this IEnumerable<string> argumentsEnumerable)
        {
            var arguments = argumentsEnumerable.ToArray();
            for (var i = 0; i < arguments.Length; i++)
            {
                var arg = arguments[i];
                if (arg.StartsWith("\\"))
                    arguments[i] = arg[1..];
            }

            return arguments;
        }
    }
}