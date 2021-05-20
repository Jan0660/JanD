﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable 4014

namespace JanD
{
    public class Config
    {
        public JanDProcess[] Processes { get; set; }
        public bool LogIpc { get; set; } = true;
        public bool FormatConfig { get; set; } = true;
    }

    public class JanDProcess
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string WorkingDirectory { get; set; }
        [JsonIgnore] public Process Process { get; set; }
        public bool AutoRestart { get; set; }
        public bool Enabled { get; set; }
        [JsonIgnore] public bool ShouldRestart => !Stopped && (AutoRestart | Enabled);
        [JsonIgnore] public bool Stopped { get; set; }
        [JsonIgnore] public int ExitCode { get; set; }
        [JsonIgnore] public int RestartCount { get; set; }
        [JsonIgnore] public StreamWriter OutWriter { get; set; }
        [JsonIgnore] public StreamWriter ErrWriter { get; set; }

        public void Start()
        {
            if (this.Process != null)
                return;
            Console.WriteLine(
                Ansi.ForegroundColor($"Starting: Name: {this.Name}; Command: {this.Command}", 0, 247, 247));
            OutWriter ??= new StreamWriter(Path.Combine("./logs/") + this.Name + "-out.log", true);
            ErrWriter ??= new StreamWriter(Path.Combine("./logs/") + this.Name + "-err.log", true);
            var process = new Process();
            var index = this.Command.IndexOf(' ');
            var last = this.Command.Length;
            var startInfo = new ProcessStartInfo
            {
                FileName = this.Command[..(index == -1 ? last : index)],
                Arguments = this.Command[(index == -1 ? last : (this.Command.IndexOf(' ') + 1))..],
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                UseShellExecute = false,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = this.WorkingDirectory ?? Directory.GetCurrentDirectory()
            };
            process.StartInfo = startInfo;
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) =>
            {
                Console.WriteLine(Ansi.ForegroundColor(
                    $"Exited: {this.Name}; ExitCode: {process.ExitCode}; AutoRestart: {this.AutoRestart}; ShouldRestart: {this.ShouldRestart};",
                    0, 247,
                    247));
                if (!Stopped)
                    WasStopped();
                if (this.ShouldRestart)
                    try
                    {
                        this.Start();
                    }
                    catch
                    {
                        Console.WriteLine($"{this.Name} failed to restart.");
                    }

                Daemon.ProcessEventAsync(Daemon.DaemonEvents.ProcessStopped, this.Name);
            };

            void Log(string whichStd, DataReceivedEventArgs eventArgs)
            {
                if (eventArgs.Data == null)
                    return;
                var str = Ansi.ForegroundColor($"{this.Name} {whichStd}| ", (byte) (whichStd == "err" ? 255 : 0),
                              (byte) (whichStd == "out" ? 255 : 0), 0) + eventArgs.Data +
                          '\n';
                if (whichStd == "out")
                    OutWriter.Write(str);
                if (whichStd == "err")
                    ErrWriter.Write(str);
                Console.Write(str);
                foreach (var con in Daemon.Connections.Where(c =>
                    c.Events.HasFlag((whichStd == "out" ? Daemon.DaemonEvents.OutLog : Daemon.DaemonEvents.ErrLog))))
                {
                    if ((whichStd == "out" ? con.OutLogSubs.Contains(this.Name) : con.ErrLogSubs.Contains(this.Name)))
                    {
                        var json = new Utf8JsonWriter(con.Stream);
                        json.WriteStartObject();
                        json.WriteString("Event", whichStd + "log");
                        json.WriteString("Process", this.Name);
                        json.WriteString("Value", str);
                        json.WriteEndObject();
                        json.Flush();
                    }
                }
            }

            process.OutputDataReceived += (_, eventArgs) => Log("out", eventArgs);
            process.ErrorDataReceived += (_, eventArgs) => Log("err", eventArgs);
            process.Start();
            this.Process = process;
            process.BeginOutputReadLine();
            process.StartInfo.RedirectStandardError = true;
            /*
             inefficient: probably not best approach, if this isn't here it sometimes throws:
             `Unhandled Exception: System.InvalidOperationException: StandardError has not been redirected.`
            */
            try
            {
                process.BeginErrorReadLine();
            }
            catch
            {
                try
                {
                    process.BeginErrorReadLine();
                }
                catch (InvalidOperationException exc)
                {
                    if (exc.Message != "An async read operation has already been started on the stream.")
                        throw;
                }
            }

            Daemon.ProcessEventAsync(Daemon.DaemonEvents.ProcessStarted, this.Name);
        }

        /// <summary>
        /// Performs cleanup operations for when the process has been stopped.
        /// </summary>
        public void WasStopped()
        {
            RestartCount++;
            ExitCode = Process.ExitCode;
            Process.Dispose();
            Process = null;
        }

        public void Stop()
        {
            Stopped = true;
            Process.Kill();
            try
            {
                Process.WaitForExit();
            }
            finally
            {
                WasStopped();
            }
        }
    }
}