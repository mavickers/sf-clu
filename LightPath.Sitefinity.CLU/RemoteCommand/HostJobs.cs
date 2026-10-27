// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        /// <summary><c>--recycle</c> and <c>--status</c>: the host check without a lock, the pool job, then the lock and offline page as read by the check.</summary>
        private static int RunHostJob(RunContext context)
        {
            var check = CheckHost(context, false, false, null);
            var result = RunScript(context, PoolScript(context, context.Job == JobRecycle));
            var state = result.Marker("state") ?? "unknown";
            var exitCode = 0;

            if (result.ExitCode != 0) throw new RemoteFailure(StepJob, result.ExitCode, $"appcmd failed for pool '{context.Profile.Pool}'");

            WriteLine($"Pool '{context.Profile.Pool}': {state}");

            if (context.Job == JobRecycle && result.Marker("recycled") != null) WriteLine("Recycled; the next request starts a new w3wp.exe and a cold start of Sitefinity");
            else if (context.Job == JobRecycle)
            {
                WriteError(@$"The pool is {state}, so it was not recycled. Start it on the host with: %windir%\system32\inetsrv\appcmd.exe start apppool ""{context.Profile.Pool}""");
                exitCode = 1;
            }
            else if (!ReportHealth(context) || state != "Started") exitCode = 1;

            if (check.Lock != null) WriteLine(@$"Running: {DescribeLock(check.Lock)} ({context.Mirror}\.git\clu.lock)");
            if (check.Offline) WriteLine("app_offline.htm is in the web folder: the site answers with the offline page until a run removes it");

            return exitCode;
        }
    }
}
