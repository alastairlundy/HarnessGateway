using System.CommandLine;
using LmProxy.AgentLauncher.Commands;

var rootCommand = new RootCommand("lmlaunch - Configure and launch agent harnesses")
{
    new LaunchCommand()
};

return rootCommand.Parse(args).Invoke();
