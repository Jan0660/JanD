using System;
using System.Text.Json.Serialization;

#pragma warning disable 4014

namespace JanD
{
    public class Config
    {
        public JanDProcessData[] Processes { get; set; }
        // When adding config options remember to add the Description attribute
        // and check if it's type is covered in the `SetValueString` extension method, `config` command
        // and add it to the `get-config` daemon method

        [Description("Log IPC requests to the daemon's stdout.")]
        public bool LogIpc { get; set; } = true;

        [Description("Save configuration as (un)formatted JSON.")]
        public bool FormatConfig { get; set; } = true;

        [Description("Maximum non-zero exit code restarts.")]
        public int MaxRestarts { get; set; } = 15;

        [Description("Logs process output to daemon's stdout.")]
        public bool LogProcessOutput { get; set; } = true;

        [Description("Write daemon's logging output to a file.")]
        public bool DaemonLogSave { get; set; } = true;

        public string SavedVersion { get; set; } = Program.Version;
    }

    /// <summary>
    /// Used for describing properties. Used in the `config` command of the JanD CLI.
    /// </summary>
    public class DescriptionAttribute : Attribute
    {
        public string Value { get; set; }
        public DescriptionAttribute(string value) => Value = value;
    }
    
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(Config))]
    public partial class ConfigJsonContext : JsonSerializerContext
    {
    }
}