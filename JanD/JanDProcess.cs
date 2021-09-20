using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace JanD
{
    public class JanDProcess
    {
        public string Name { get; set; }

        /// <summary>
        /// DEPRECATED. Use Filename and Arguments instead.
        /// </summary>
        [Obsolete]
        public string Command { get; set; }

        public string Filename { get; set; }
        public string[] Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        [JsonIgnore] public Process Process { get; set; }
        public bool AutoRestart { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public bool Watch { get; set; }
        [JsonIgnore]
        public int SafeIndex { get; set; }

        [JsonIgnore]
        public bool ShouldRestart =>
            !Stopped && AutoRestart && Enabled && CurrentUnstableRestarts < Daemon.Config.MaxRestarts;

        [JsonIgnore] public bool Stopped { get; set; }
        [JsonIgnore] public int ExitCode { get; set; }
        [JsonIgnore] public int CurrentUnstableRestarts { get; set; }
        [JsonIgnore] public int RestartCount { get; set; }
        [JsonIgnore] public StreamWriter OutWriter { get; set; }
        [JsonIgnore] public StreamWriter ErrWriter { get; set; }
        [JsonIgnore] public FileSystemWatcher FileSystemWatcher { get; set; }

        public void Start()
        {
            if (Process != null)
                return;
            Console.WriteLine(
                Ansi.ForegroundColor($"Starting: Name: {Name}; Filename: {Filename}", 0, 247, 247));
            OutWriter ??= new StreamWriter(Path.Combine("./logs/") + Name + "-out.log", true)
            {
                AutoFlush = true
            };
            ErrWriter ??= new StreamWriter(Path.Combine("./logs/") + Name + "-err.log", true)
            {
                AutoFlush = true
            };
            if (Watch && FileSystemWatcher == null)
            {
                FileSystemWatcher = new FileSystemWatcher(WorkingDirectory)
                {
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.CreationTime
                                   | NotifyFilters.LastWrite
                };
                FileSystemWatcher.Changed += (_, args) =>
                {
                    Daemon.DaemonLog($"File changed for {Name}: {args.Name}, restarting in 500ms");
                    // prevent race condition/multiple files changed at once causing multiple restarts
                    bool wasHandled = false;
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        if (wasHandled)
                            return;
                        wasHandled = true;
                        Stop();
                        Start();
                    });
                };
            }

            var process = new Process();
            var startInfo = new ProcessStartInfo
            {
                FileName = Filename,
                Arguments = string.Join(' ', Arguments),
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
                var str = Ansi.ForegroundColor($"{Name} {whichStd}| ", (byte)(whichStd == "err" ? 255 : 0),
                              (byte)(whichStd == "out" ? 255 : 0), 0) + eventArgs.Data +
                          '\n';
                if (whichStd == "out")
                    OutWriter.Write(str);
                if (whichStd == "err")
                    ErrWriter.Write(str);
                if (Daemon.Config.LogProcessOutput)
                    Console.Write(str);
                try
                {
                    foreach (var con in Daemon.Connections.Where(c =>
                        c.Events.HasFlag((whichStd == "out" ? DaemonEvents.OutLog : DaemonEvents.ErrLog))))
                    {
                        if ((whichStd == "out" ? con.OutLogSubs.Contains(Name) : con.ErrLogSubs.Contains(Name)))
                        {
                            var json = new Utf8JsonWriter(con.Stream);
                            json.WriteStartObject();
                            json.WriteString("Event", whichStd + "log");
                            json.WriteString("Process", Name);
                            json.WriteString("Value", str);
                            json.WriteEndObject();
                            json.Flush();
                            json.Dispose();
                            con.Stream.WriteByte((byte)'\n');
                            con.Stream.Flush();
                        }
                    }
                }
                catch
                {
                    // will be hit if connection interrupted, rest of the code should take care of disposing...
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