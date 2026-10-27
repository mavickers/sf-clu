// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        /// <summary>The checks of --init and every --profile edit: the solution in its clone, then the host without the run lock. Each passed check prints one line.</summary>
        /// <remarks>A missing APPDATA, like a missing UserSecretsId, is a warning here: only --deploy needs it, and LockHost stops that job with exit code 12.</remarks>
        private static void CheckProfile(RemoteProfile profile, string name, bool verbose)
        {
            var context = NewContext(profile, name, ActionInit, verbose);
            var source = context.Source;

            WriteLine($"Solution: {source.Solution} in {context.RepoRoot}");
            WriteLine($"Sitefinity project: {source.ProjectName} (web folder {source.WebFolder})");
            WriteLine(source.SecretsId == null ? $"Warning: project {source.ProjectName} has no <UserSecretsId>, so --deploy stops until it has one" : $"Secrets ID: {source.SecretsId}");
            WriteLine($"Health path: {source.HealthPath ?? "not configured"}");
            WriteLine($"Startup path: {source.StatusPath}");

            HostCheck? check = null;

            try
            {
                check = CheckHost(context, false, profile.SecretsPath == null, null);
            }
            catch (RemoteFailure failure) when (failure.ExitCode == 12)
            {
                // Exit code 12 comes after the shell and physical path checks, so both passed.
            }

            WriteLine($"SSH to '{profile.Host}' and its default shell: ok");
            WriteLine($"Pool '{profile.Pool}' serves {HostWebFolder(context)}: ok");
            WriteLine(profile.SecretsPath != null ? "APPDATA: not needed (secretsPath is set)" : check != null ? $"APPDATA: {check.AppData}" : "Warning: APPDATA: not set; --deploy will need it, or secretsPath");

            var msbuildResult = RunScript(context, MsbuildCheckScript(context));

            if (msbuildResult.ExitCode == 255) throw new RemoteFailure(StepHostCheck, 255, $"ssh to '{profile.Host}' failed. Check its Host entry in ~/.ssh/config, the key and the network.");
            if (msbuildResult.ExitCode != 0) throw new RemoteFailure(StepHostCheck, msbuildResult.ExitCode, "The MSBuild check script failed");
            if (msbuildResult.Marker("msbuild") is not { } msbuild) throw new RemoteFailure(StepHostCheck, 20, profile.MsbuildPath != null ? $"msbuildPath {profile.MsbuildPath} does not exist on the host" : $"vswhere found no {VsWhereProduct} {VsWhereVersionRange} with {VsWhereWorkload} on the host. Install it, or set msbuildPath in the profile.");

            WriteLine($"MSBuild: {msbuild}");
        }
    }
}
