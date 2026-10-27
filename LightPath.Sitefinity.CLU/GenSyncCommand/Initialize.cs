using System.CommandLine;
using static LightPath.Sitefinity.CLU.GenSyncCommand.Properties;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class GenSyncCommand
    {
        public static void Initialize(RootCommand rootCommand)
        {
            GenSyncCmd.SetAction(Handler);

            VerboseOption.Arity = ArgumentArity.Zero;
            VerboseOption.Required = false;
            VerboseOption.DefaultValueFactory = _ => false;

            SecretOption.Validators.Add(SecretValidator);
            SecretOption.Arity = ArgumentArity.ZeroOrOne;

            SiteKeyOption.Validators.Add(SiteKeyValidator);
            SiteKeyOption.Arity = ArgumentArity.ZeroOrOne;
            SiteKeyOption.DefaultValueFactory = _ => "20";

            TargetsArgument.Validators.Add(TargetsValidator);
            TargetsArgument.Arity = ArgumentArity.ZeroOrMore;

            GenSyncCmd.Options.Add(SiteKeyOption);
            GenSyncCmd.Options.Add(SecretOption);
            GenSyncCmd.Options.Add(VerboseOption);
            GenSyncCmd.Arguments.Add(TargetsArgument);
            
            rootCommand.Subcommands.Add(GenSyncCmd);
        }
    }
}
