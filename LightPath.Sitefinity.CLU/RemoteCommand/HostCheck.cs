// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private const string DefaultShellHint = @"Set the host's SSH default shell to cmd.exe: remove HKLM:\SOFTWARE\OpenSSH DefaultShell, or set it to C:\Windows\System32\cmd.exe.";

        /// <summary>Step 3 and the first call of <c>--recycle</c> and <c>--status</c>: checks the host before anything on it changes.</summary>
        /// <remarks>An ssh failure (exit 255) is judged before the shell, and a missing shell marker means the step command never started PowerShell.</remarks>
        private static HostCheck CheckHost(RunContext context, bool takeLock, bool readAppData, string? staleLock)
        {
            var result = RunScript(context, HostCheckScript(context, takeLock, readAppData, staleLock));
            var shell = result.Marker("shell");

            if (result.ExitCode == 255) throw new RemoteFailure(StepHostCheck, 255, $"ssh to '{context.Profile.Host}' failed. Check its Host entry in ~/.ssh/config, the key and the network.");
            if (shell == null) throw new RemoteFailure(StepHostCheck, result.ExitCode, $"The step command did not start Windows PowerShell on the host. {DefaultShellHint}");

            switch (result.ExitCode)
            {
                case 0:
                    break;
                case 10:
                    throw new RemoteFailure(StepHostCheck, 10, $"The host's SSH default shell is '{shell}'. {DefaultShellHint}");
                case 11 when result.Marker("configured") is { } configured:
                    throw new RemoteFailure(StepHostCheck, 11, $"The IIS application for pool '{context.Profile.Pool}' serves {configured}, not the mirror's {result.Marker("expected")}");
                case 11:
                    throw new RemoteFailure(StepHostCheck, 11, $"Expected one IIS application with a root virtual directory for pool '{context.Profile.Pool}', found {result.Marker("apps") ?? "none"}. Setting up the site is a host step; the CLU does not change IIS.");
                case 12:
                    throw new RemoteFailure(StepHostCheck, 12, $"Pool '{context.Profile.Pool}' has no APPDATA environment variable for secrets.xml. Add one on the host (appcmd set apppool \"{context.Profile.Pool}\" /+environmentVariables.[name='APPDATA',value='<folder>']), or set secretsPath in the profile.");
                case 13:
                    return new HostCheck { Lock = result.Marker("locked"), LockAgeMinutes = int.TryParse(result.Marker("lockage"), out var age) ? age : 0, LockedOut = true };
                case 14:
                    throw new RemoteFailure(StepHostCheck, 14, $"{context.Mirror} has an origin remote, so it is a clone, not a mirror the CLU created. Use another root.");
                default:
                    throw new RemoteFailure(StepHostCheck, result.ExitCode, "The host check script failed");
            }

            if (result.Marker("init") != null) WriteLine($"Created the mirror repository {context.Mirror}");

            return new HostCheck { AppData = result.Marker("appdata"), Lock = result.Marker("locked"), Offline = result.Marker("offline") != null };
        }
    }
}
