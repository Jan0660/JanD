using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;

namespace JanD
{
    public class Config
    {
        public JanDProcess[] Processes { get; set; }
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

        public void Start()
        {
            var proc = this;
            if (proc.Process != null)
                return;
            Console.WriteLine(
                Ansi.ForegroundColor($"Starting: Name: {proc.Name}; Command: {proc.Command}", 0, 247, 247));
            var process = new Process();
            var index = proc.Command.IndexOf(' ');
            var last = proc.Command.Length;
            var startInfo = new ProcessStartInfo
            {
                FileName = proc.Command[..(index == -1 ? last : index)],
                Arguments = proc.Command[(index == -1 ? last : (proc.Command.IndexOf(' ') + 1))..],
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                UseShellExecute = false,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = proc.WorkingDirectory ?? Directory.GetCurrentDirectory()
            };
            process.StartInfo = startInfo;
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) =>
            {
                Console.WriteLine(Ansi.ForegroundColor(
                    $"Exited: {proc.Name}; ExitCode: {process.ExitCode}; AutoRestart: {proc.AutoRestart}; ShouldRestart: {proc.ShouldRestart};",
                    0, 247,
                    247));
                if (!Stopped)
                    WasStopped();
                if (proc.ShouldRestart)
                    proc.Start();
            };
            process.OutputDataReceived += (sender, eventArgs) =>
            {
                if (eventArgs.Data == null)
                    return;
                // todo: unbloat
                var str = Ansi.ForegroundColor(proc.Name + " OUT| ", 0, 255, 0) + eventArgs.Data + '\n';
                var writer = new StreamWriter(Path.Combine("./logs/") + proc.Name + "-out.log", true);
                writer.Write(str);
                writer.Close();
                writer.Dispose();
                Console.Write(str);
            };
            process.ErrorDataReceived += (sender, eventArgs) =>
            {
                if (eventArgs.Data == null)
                    return;
                var str = Ansi.ForegroundColor(proc.Name + " ERR| ", 255, 0, 0) + eventArgs.Data + '\n';
                var writer = new StreamWriter(Path.Combine("./logs/") + proc.Name + "-err.log", true);
                writer.Write(str);
                writer.Close();
                writer.Dispose();
                Console.Write(str);
            };
            process.Start();
            proc.Process = process;
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