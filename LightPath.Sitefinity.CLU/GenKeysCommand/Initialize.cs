using System.CommandLine;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class GenKeysCommand
    {
        public static void Initialize(RootCommand rootCommand)
        {
            GenKeysCmd.SetAction(Handler);

            rootCommand.Subcommands.Add(GenKeysCmd);
        }
    }
}
