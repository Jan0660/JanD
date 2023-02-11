using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace JanD
{
    public class JanDProcess
    {
        public JanDProcessData Data { get; set; }
        [JsonIgnore] public Process Process { get; set; }
        [JsonIgnore] public int SafeIndex { get; set; }

        [JsonIgnore]
        public bool ShouldRestart =>
            !Stopped && Data.AutoRestart && Data.Enabled && CurrentUnstableRestarts < Daemon.Config.MaxRestarts;

        [JsonIgnore] public bool Stopped { get; set; }
        [JsonIgnore] public int ExitCode { get; set; }
        [JsonIgnore] public int CurrentUnstableRestarts { get; set; }
        [JsonIgnore] public int RestartCount { get; set; }
        [JsonIgnore] public StreamWriter OutWriter { get; set; }
        [JsonIgnore] public StreamWriter ErrWriter { get; set; }
        [JsonIgnore] public SemaphoreSlim LogsLock { get; set; } = new(1, 1);
        [JsonIgnore] public FileSystemWatcher FileSystemWatcher { get; set; }

        public static string GetLogFileName(string processName, string whichStd)
            => Path.Combine("./logs/", processName + "-" + whichStd + ".log");
        public static StreamWriter GetStreamWriter(string processName, string whichStd, bool append = true)
        {
            if(whichStd != "out" && whichStd != "err")
                throw new ArgumentException("whichStd must be either 'out' or 'err'");
            return new StreamWriter(GetLogFileName(processName, whichStd), append)
            {
                AutoFlush = true
            };
        }

        public void Start()
        {
            if (Process != null)
                return;
            Console.WriteLine(
                Ansi.ForegroundColor($"Starting: Name: {Data.Name}; Filename: {Data.Filename}", 0, 247, 247));
            OutWriter ??= GetStreamWriter(Data.Name, "out");
            ErrWriter ??= GetStreamWriter(Data.Name, "err");
            if (Data.Watch && FileSystemWatcher == null)
            {
                FileSystemWatcher = new FileSystemWatcher(Data.WorkingDirectory)
                {
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.CreationTime
                                   | NotifyFilters.LastWrite
                };
                FileSystemWatcher.Changed += (_, args) =>
                {
                    Daemon.DaemonLog($"File changed for {Data.Name}: {args.Name}, restarting in 500ms");
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
                FileName = Data.Filename,
                Arguments = string.Join(' ', Data.Arguments),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = Data.WorkingDirectory ?? Directory.GetCurrentDirectory()
            };
            process.StartInfo = startInfo;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                Console.WriteLine(Ansi.ForegroundColor(
                    $"Exited: {Data.Name}; ExitCode: {process.ExitCode}; AutoRestart: {Data.AutoRestart}; ShouldRestart: {ShouldRestart}; Stopped: {Stopped};",
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
                        Console.WriteLine($"{Data.Name} failed to restart.");
                    }
                }

#pragma warning disable 4014
                Daemon.ProcessEventAsync(DaemonEvents.ProcessStopped, Data.Name);
#pragma warning restore 4014
            };

            void Log(string whichStd, DataReceivedEventArgs eventArgs)
            {
                if (eventArgs.Data == null)
                    return;
                LogsLock.Wait();
                var str = Ansi.ForegroundColor($"{Data.Name} {whichStd}| ", (byte)(whichStd == "err" ? 255 : 0),
                              (byte)(whichStd == "out" ? 255 : 0), 0) + eventArgs.Data +
                          '\n';
                if (whichStd == "out")
                    OutWriter.Write(str);
                if (whichStd == "err")
                    ErrWriter.Write(str);
                if (Daemon.Config.LogProcessOutput)
                    Console.Write(str);
                LogsLock.Release();
                Daemon.DaemonConnection? connectionAt = null;
                try
                {
                    foreach (var con in Daemon.Connections)
                    {
                        if(!con.Events.HasFlag((whichStd == "out" ? DaemonEvents.OutLog : DaemonEvents.ErrLog)))
                            continue;
                        con.EventSemaphore ??= new SemaphoreSlim(1);
                        con.EventSemaphore.Wait();
                        connectionAt = con;
                        if ((whichStd == "out"
                                ? con.OutLogSubs.Contains(Data.Name)
                                : con.ErrLogSubs.Contains(Data.Name)))
                        {
                            var json = new Utf8JsonWriter(con.Stream);
                            json.WriteStartObject();
                            json.WriteString("Event", whichStd + "log");
                            json.WriteString("Process", Data.Name);
                            json.WriteString("Value", str);
                            json.WriteEndObject();
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
                finally
                {
                    connectionAt?.EventSemaphore?.Release();
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
            Daemon.ProcessEventAsync(DaemonEvents.ProcessStarted, Data.Name);
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