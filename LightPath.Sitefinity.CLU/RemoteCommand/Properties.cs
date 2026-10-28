using System.CommandLine;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private const string ActionAddSecret = "--add-secret";
        private const string ActionInit = "--init";
        private const string ActionProfile = "--profile";
        private const string ActionProfiles = "--profiles";
        private const string ActionRemoveSecret = "--remove-secret";
        private const string ActionSecrets = "--add-secret/--remove-secret";
        private const string JobBuild = "--build";
        private const string JobDeploy = "--deploy";
        private const string JobRebuild = "--rebuild";
        private const string JobRecycle = "--recycle";
        private const string JobStatus = "--status";
        private const int LockMaxAgeMinutes = 60;
        private const string ReceivePack = "powershell -NoProfile -NonInteractive -Command git receive-pack";
        private const string SnapshotRef = "refs/clu/snapshot";
        private const string StepBuild = "step 8 (build)";
        private const string StepChecks = "step 1 (checks)";
        private const string StepCheckout = "step 5 (checkout)";
        private const string StepClean = "step 7b (clean)";
        private const string StepCommand = "powershell -NoProfile -NonInteractive -Command \"& ([scriptblock]::Create([Console]::In.ReadToEnd()))\"";
        private const string StepCopy = "step 6 (private packages)";
        private const string StepFinish = "step 9 (finish)";
        private const string StepHostCheck = "step 3 (host check)";
        private const string StepJob = "job";
        private const string StepPush = "step 4 (push)";
        private const string StepRestore = "step 7 (restore)";
        private const string StepSecrets = "step 8b (secrets delivery)";
        private const string StepSnapshot = "step 2 (snapshot)";
        private const string StepStartup = "step 10 (startup)";
        private const string VsWhereProduct = "Microsoft.VisualStudio.Product.BuildTools";
        private const string VsWhereVersionRange = "[18.0,19.0)";
        private const string VsWhereWorkload = "Microsoft.VisualStudio.Workload.WebBuildTools";
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

        public static class Properties
        {
            public static readonly Command RemoteCmd = new("-remote", "Build, deploy, recycle or check the site on a Windows build host over SSH");
            public static readonly Option<bool> BuildOption = Marker(JobBuild, "Take the site offline, send a snapshot of the working tree, restore and build on the host, and bring the site back");
            public static readonly Option<bool> RebuildOption = Marker(JobRebuild, "--build with a Clean between the restore and the build");
            public static readonly Option<bool> DeployOption = Marker(JobDeploy, "--build, then deliver secrets.xml and wait until Sitefinity has started");
            public static readonly Option<bool> RecycleOption = Marker(JobRecycle, "Recycle the app pool");
            public static readonly Option<bool> StatusOption = Marker(JobStatus, "Report the pool state, the health check at the web project's sf:HealthCheckApiEndpoint, a running job and the offline page");
            public static readonly Option<bool> InitOption = Marker(ActionInit, "Create a profile after checking the solution and the host: name=<name> solution=<.sln path> [default=true] and profile keys");
            public static readonly Option<bool> ProfileOption = Marker(ActionProfile, "Update a profile, with the same checks as --init: name=<name> [default=true] and profile keys");
            public static readonly Option<bool> ProfilesOption = Marker(ActionProfiles, "List the profiles and mark the default");
            public static readonly Option<bool> AddSecretOption = Marker(ActionAddSecret, "Add or replace secrets.xml overrides: profile=<name> key=<entry> value=<value> [key=... value=...]");
            public static readonly Option<bool> RemoveSecretOption = Marker(ActionRemoveSecret, "Remove secrets.xml overrides: profile=<name> key=<entry> [key=...]");
            public static readonly Option<bool> VerboseOption = Marker("--verbose", "Print each command sent to the host (never a script) and each step's time");
            public static readonly Argument<List<string>> PropertiesArgument = new("key=value")
            {
                Description = "Profile keys: solution (local path of the .sln), project (the Sitefinity project when the solution has several), host (SSH alias), root (mirror root on the host), pool, site (URL), msbuild (extra MSBuild arguments), msbuildPath, secretsPath (full path of secrets.xml on the host). A job also takes profile=<name>.",
                Arity = ArgumentArity.ZeroOrMore
            };

            private static Option<bool> Marker(string name, string description) => new(name) { Description = description, Arity = ArgumentArity.Zero };
        }
    }
}
