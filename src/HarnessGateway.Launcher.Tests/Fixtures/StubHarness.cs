#:property PublishAot=false

// Cross-platform stub harness. The tests spawn this file-based app through
// the real LaunchRunner, so what it dumps is what a spawned harness actually
// received rather than a plan-level assertion. argv[0] is the dump path;
// everything after it is the child's own argv.
using System.Collections;
using System.Text;

string[] arguments = args.Length > 0 ? args[1..] : [];
StringBuilder dump = new();

foreach (string argument in arguments)
{
    dump.Append("ARG:").AppendLine(argument);
}

int environmentCount = 0;
foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
{
    dump.Append("ENV:")
        .Append((string)entry.Key)
        .Append('=')
        .AppendLine(entry.Value?.ToString() ?? string.Empty);
    environmentCount++;
}

if (args.Length > 0)
{
    await File.WriteAllTextAsync(args[0], dump.ToString());
}

// The file is the test-readable record; echo it too so a manual run of the
// fixture is a useful probe in its own right.
Console.Write(dump.ToString());
Console.WriteLine($"Stub harness: {arguments.Length} arguments, {environmentCount} environment variables.");
