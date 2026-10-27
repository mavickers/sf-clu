using System.CommandLine;

namespace LightPath.Sitefinity.CLU
{
    public static partial class GenSyncCommand
    {
        public static class Properties
        {
            public static readonly Command GenSyncCmd = new("-gensync", "Generate Site Sync Configuration");
            public static readonly Option<bool?> VerboseOption = new("--verbose") { Description = "Enable verbose output (optional)" };
            public static readonly Option<string?> SecretOption = new("--secret") { Description = "Specify the auth node secret (optional)" };
            public static readonly Option<string?> SiteKeyOption = new("--sitekey") { Description = "Specify the auth node site key(optional)" };
            public static readonly Argument<List<TargetModel>> TargetsArgument = new("targets") { Description = "Specify the auth node targets (optional)", CustomParser = TargetsArgumentParser };
        }
    }
}
