using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Reflection;

namespace LightPath.Sitefinity.CLU
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine();

            var rootCommand = new RootCommand("Sitefinity Command Line Utility (CLU)");

            if (!args.Any()) args = new[] { "-h" };

            rootCommand.SetAction(_ => 1);

            GenKeysCommand.Initialize(rootCommand);
            GenSyncCommand.Initialize(rootCommand);
            RemoteCommand.Initialize(rootCommand);

            var helpOption = rootCommand.Options.OfType<HelpOption>().Single();

            helpOption.Action = new VersionedHelpAction((HelpAction)helpOption.Action!);

            return await rootCommand.Parse(args).InvokeAsync();
        }

        /// <summary>Prints the tool version above the help text of every command.</summary>
        private class VersionedHelpAction(HelpAction help) : SynchronousCommandLineAction
        {
            public override bool ClearsParseErrors => true;

            public override int Invoke(ParseResult parseResult)
            {
                var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];

                parseResult.InvocationConfiguration.Output.WriteLine($"sf-clu {version}");
                parseResult.InvocationConfiguration.Output.WriteLine();

                return help.Invoke(parseResult);
            }
        }
    }
}
