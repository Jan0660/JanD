using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommandLine;
using JanD;

Dictionary<string, string> descriptionOverrides = new()
{
    ["list"] = @"List processes with their information like the memory usage, restart counter and process id.
The mini table of <span class=""jand-x"">x</span> or <span class=""jand-check"">√</span> represents:
- **R**: If the process is currently running.
- **E**: If the process is enabled.
- **A**: If the process has AutoRestart enabled."
};
string GenerateDoc(Type cmd)
{
    var verb = cmd.GetCustomAttribute<VerbAttribute>();
    List<ValueAttribute> valueAttributes = new();
    foreach (var prop in cmd.GetProperties())
    {
        if (prop.GetCustomAttribute<ValueAttribute>() != null)
            valueAttributes.Add(prop.GetCustomAttribute<ValueAttribute>());
    }

    List<OptionAttribute> optionAttributes = new();
    foreach (var prop in cmd.GetProperties())
    {
        if (prop.GetCustomAttribute<OptionAttribute>() != null)
            optionAttributes.Add(prop.GetCustomAttribute<OptionAttribute>());
    }

    return $@"### {verb!.Name}
**Aliases:** `{verb.Name}`{(verb.Aliases.Length == 0 ? "" : $", `{String.Join("`, `", verb.Aliases)}`")}
{new Func<string>(() => {
    var res = @"##### Options
";
    if (!valueAttributes.Any() && !optionAttributes.Any())
        return "";
    foreach (var att in valueAttributes)
        res += $@"- **{att.MetaName} (Position {att.Index})** {(att.Required ? "Required." : "")} {att.HelpText.Trim()}
";
    foreach (var att in optionAttributes)
        res += $@"- **--{att.LongName}** (Default: `{att.Default}`) {(att.Required ? "Required." : "")} {att.HelpText.Trim()}
";
    return res;
})()}
{(descriptionOverrides.TryGetValue(verb.Name, out var value) ? value : verb.HelpText)}";
}

List<Command> commands = new();
foreach (var cmd in typeof(Commands).GetNestedTypes())
{
    string group;
    var verb = cmd.GetCustomAttribute<VerbAttribute>();
    if (verb!.Hidden)
        group = "Advanced";
    else if (verb.Name.StartsWith("group"))
        group = "Group";
    else group = "Basic Commands";
    commands.Add(new(group, cmd));
}

void DoCommands(string group)
{
    foreach (var cmd in commands.Where(c => c.Group == group))
        Console.WriteLine(GenerateDoc(cmd.Type));
}

var groups = new[] { "Basic Commands", "Group", "Advanced" };
Console.WriteLine(@"---
title: Commands
---
");
foreach (var grp in groups)
{
    Console.WriteLine($"## {grp}");
    DoCommands(grp);
}
Console.WriteLine();

record Command(string Group, Type Type);