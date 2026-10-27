using System.CommandLine;
using static LightPath.Sitefinity.CLU.RemoteCommand.Properties;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        public static void Initialize(RootCommand rootCommand)
        {
            RemoteCmd.SetAction(Handler);

            foreach (var option in new[] { BuildOption, RebuildOption, DeployOption, RecycleOption, StatusOption, InitOption, ProfileOption, ProfilesOption, AddSecretOption, RemoveSecretOption, VerboseOption }) RemoteCmd.Options.Add(option);

            RemoteCmd.Arguments.Add(PropertiesArgument);

            rootCommand.Subcommands.Add(RemoteCmd);
        }
    }
}
