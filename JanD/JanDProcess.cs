using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JanD
{
    public class JanDProcess
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string WorkingDirectory { get; set; }
        [JsonIgnore] public Process Process { get; set; }
        public bool AutoRestart { get; set; }
        public bool Enabled { get; set; }

        [JsonIgnore]
        public bool ShouldRestart =>
            !Stopped && AutoRestart && Enabled && CurrentUnstableRestarts < Daemon.Config.MaxRestarts;

        [JsonIgnore] public bool Stopped { get; set; }
        [JsonIgnore] public int ExitCode { get; set; }
        [JsonIgnore] public int CurrentUnstableRestarts { get; set; }
        [JsonIgnore] public int RestartCount { get; set; }
        [JsonIgnore] public StreamWriter OutWriter { get; set; }
        [JsonIgnore] public StreamWriter ErrWriter { get; set; }

        public void Start()
        {
            if (Process != null)
                return;
            Console.WriteLine(
                Ansi.ForegroundColor($"Starting: Name: {Name}; Command: {Command}", 0, 247, 247));
            OutWriter ??= new StreamWriter(Path.Combine("./logs/") + Name + "-out.log", true);
            ErrWriter ??= new StreamWriter(Path.Combine("./logs/") + Name + "-err.log", true);
            var process = new Process();
            var index = Command.IndexOf(' ');
            var last = Command.Length;
            var startInfo = new ProcessStartInfo
            {
                FileName = Command[..(index == -1 ? last : index)],
                Arguments = Command[(index == -1 ? last : (Command.IndexOf(' ') + 1))..],
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = WorkingDirectory ?? Directory.GetCurrentDirectory()
            };
            process.StartInfo = startInfo;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                Console.WriteLine(Ansi.ForegroundColor(
                    $"Exited: {Name}; ExitCode: {process.ExitCode}; AutoRestart: {AutoRestart}; ShouldRestart: {ShouldRestart};",
                    0, 247,
                    247));
                if (process.ExitCode != 0 && !Stopped)
                    CurrentUnstableRestarts++;
                if (!Stopped)
                    WasStopped();
                if (ShouldRestart)
                {
                    try
                    {
                        Start();
                    }
                    catch
                    {
                        Console.WriteLine($"{Name} failed to restart.");
                    }
                }

#pragma warning disable 4014
                Daemon.ProcessEventAsync(DaemonEvents.ProcessStopped, Name);
#pragma warning restore 4014
            };

            void Log(string whichStd, DataReceivedEventArgs eventArgs)
            {
                if (eventArgs.Data == null)
                    return;
                var str = Ansi.ForegroundColor($"{Name} {whichStd}| ", (byte) (whichStd == "err" ? 255 : 0),
                              (byte) (whichStd == "out" ? 255 : 0), 0) + eventArgs.Data +
                          '\n';
                if (whichStd == "out")
                    OutWriter.Write(str);
                if (whichStd == "err")
                    ErrWriter.Write(str);
                if(Daemon.Config.LogProcessOutput)
                    Console.Write(str);
                foreach (var con in Daemon.Connections.Where(c =>
                    c.Events.HasFlag((whichStd == "out" ? DaemonEvents.OutLog : DaemonEvents.ErrLog))))
                {
                    if ((whichStd == "out" ? con.OutLogSubs.Contains(Name) : con.ErrLogSubs.Contains(Name)))
                    {
                        try
                        {
                            var json = new Utf8JsonWriter(con.Stream);
                            json.WriteStartObject();
                            json.WriteString("Event", whichStd + "log");
                            json.WriteString("Process", Name);
                            json.WriteString("Value", str);
                            json.WriteEndObject();
                            json.Flush();
                            json.Dispose();
                        }
                        catch
                        {
                            // will be hit if connection interrupted, rest of the code should take care of disposing...
                        }
                    }
                }
            }

            process.OutputDataReceived += (_, eventArgs) => Log("out", eventArgs);
            process.ErrorDataReceived += (_, eventArgs) => Log("err", eventArgs);
            process.Start();
            Process = process;
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

#pragma warning disable 4014
            Daemon.ProcessEventAsync(DaemonEvents.ProcessStarted, Name);
#pragma warning restore 4014
        }

        /// <summary>
        /// Performs cleanup operations for when the process has been stopped.
        /// </summary>
        public void WasStopped()
        {
            RestartCount++;
            ExitCode = Process?.ExitCode ?? -1;
            Process?.Dispose();
            Process = null;
        }

        public void Stop()
        {
            Stopped = true;
            Process?.Kill(true);
            try
            {
                Process?.WaitForExit();
            }
            finally
            {
                WasStopped();
            }
        }
    }
}