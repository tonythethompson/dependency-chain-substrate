using DCS.Cli;

var cliArgs = Environment.GetCommandLineArgs()[1..];

if (cliArgs.Length == 0 || cliArgs.Contains("--help") || cliArgs.Contains("-h"))
{
    ProgramCommands.PrintHelp();
    return 0;
}

var command = cliArgs[0];

return command switch
{
    "analyze" => await ProgramCommands.RunAnalyze(cliArgs[1..]),
    "atlas"   => await ProgramCommands.RunAtlas(cliArgs[1..]),
    "dump-ir" => await ProgramCommands.RunDumpIr(cliArgs[1..]),
    "diff"    => await ProgramCommands.RunDiff(cliArgs[1..]),
    "viz"     => await ProgramCommands.RunViz(cliArgs[1..]),
    _         => UnknownCommand(command)
};

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"[DCS] Error: Unknown command: {command}. Run 'dcs --help' for usage.");
    return 2;
}
