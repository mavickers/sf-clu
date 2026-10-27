using System.Diagnostics;
using System.Text.RegularExpressions;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private static readonly Regex ErrorCountPattern = new(@"^\s*(\d+) Error\(s\)\s*$");
        private static readonly Regex WarningCountPattern = new(@"^\s*(\d+) Warning\(s\)\s*$");

        /// <summary>Step 8; the error and warning counts come from MSBuild's own summary lines and from no other output.</summary>
        private static void Build(RunContext context, BuildOutcome outcome)
        {
            var result = RunMsbuildScript(context, StepBuild, BuildScript(context, false), false);

            foreach (var line in result.Output)
            {
                var errors = ErrorCountPattern.Match(line);
                var warnings = WarningCountPattern.Match(line);

                if (errors.Success) outcome.Errors = int.Parse(errors.Groups[1].Value);
                if (warnings.Success) outcome.Warnings = int.Parse(warnings.Groups[1].Value);
            }

            if (result.ExitCode != 0) throw new RemoteFailure(StepBuild, result.ExitCode, "MSBuild failed");
        }

        /// <summary>Step 5; prints what the checkout discards on the host and returns the private package folders the mirror lacks.</summary>
        private static List<string> CheckOut(RunContext context, string sha, List<string> privatePackages, BuildOutcome outcome)
        {
            var result = RunScript(context, CheckoutScript(context, sha, privatePackages));
            var discarded = result.MarkerValues("tracked").ToList();
            var hostOnly = result.MarkerValues("hostonly").ToList();

            outcome.OfflineWritten = result.Marker("offline") != null;

            if (result.Marker("indexlock") != null) WriteLine(@"Removed a leftover .git\index.lock in the mirror");
            if (discarded.Count > 0) WriteLine($"Changes on the host that this checkout discards:\n  {string.Join("\n  ", discarded)}");
            if (hostOnly.Count > 0) WriteLine($"Files that exist only on the host and stay until deleted there:\n  {string.Join("\n  ", hostOnly)}");
            if (result.ExitCode != 0) throw new RemoteFailure(StepCheckout, result.ExitCode, "The checkout script failed");

            return result.MarkerValues("missing").ToList();
        }

        private static string Counts(BuildOutcome outcome) => outcome.Errors == null ? string.Empty : $"{outcome.Errors} error(s), {outcome.Warnings ?? 0} warning(s)";

        /// <summary>Step 9: run by the final station once the lock is held; a failure here is reported but never hides an earlier one.</summary>
        private static void Finish(RunContext context, RemoteFailure? failure)
        {
            var outcome = context.Outcome;
            var failurePage = failure != null && outcome.OfflineWritten ? OfflinePage("Build failed", $"sf-clu -remote {context.Job} failed in {failure.Step} with exit code {failure.ExitCode}: {failure.Message}. {Counts(outcome)}") : null;
            var result = RunScript(context, FinishScript(context, failure == null, failurePage));

            if (result.Marker("otherlock") is { } other) WriteError($"The lock now belongs to {DescribeLock(other)}; it was left in place");
            if (result.ExitCode != 0) throw new RemoteFailure(StepFinish, result.ExitCode, @$"exit code {result.ExitCode}. The offline page and the lock {context.Mirror}\.git\clu.lock may remain on the host.");
        }

        /// <summary>Step 3 for a build job: the host check, then a takeover when the lock is a leftover of a killed run.</summary>
        private static HostCheck LockHost(RunContext context)
        {
            var readAppData = context.Job == JobDeploy && context.Profile.SecretsPath == null;
            var check = CheckHost(context, true, readAppData, null);

            if (!check.LockedOut) return check;
            if (!IsLeftoverLock(check)) throw new RemoteFailure(StepHostCheck, 13, @$"{DescribeLock(check.Lock)} is running ({context.Mirror}\.git\clu.lock). Wait for it, or delete the lock on the host if that run is gone.");

            WriteLine($"Taking over the lock left by {DescribeLock(check.Lock)}");

            check = CheckHost(context, true, readAppData, check.Lock);

            if (check.LockedOut) throw new RemoteFailure(StepHostCheck, 13, $"{DescribeLock(check.Lock)} took the lock first");

            return check;
        }

        private static int RunBuild(RunContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var bus = BuildBus(context);
            var exitCode = 0;
            using var cancellation = new CancellationTokenSource();

            void OnCancel(object? sender, ConsoleCancelEventArgs args)
            {
                args.Cancel = true;
                cancellation.Cancel();
            }

            Console.CancelKeyPress += OnCancel;

            try
            {
                // GoAsync, not Go: Go passes no cancellation token, so a Ctrl+C would never stop the bus. The stations are synchronous.
                bus.GoAsync(context, cancellation.Token).GetAwaiter().GetResult();
            }
            finally
            {
                Console.CancelKeyPress -= OnCancel;
            }

            var failure = FirstFailure(bus.Package.Results);

            if (failure == null && context.Job == JobDeploy) exitCode = WaitForSite(context);

            var counts = Counts(context.Outcome);
            var summary = $"{(counts.Length > 0 ? $"{counts}, " : string.Empty)}{(int)stopwatch.Elapsed.TotalMinutes}:{stopwatch.Elapsed.Seconds:00} elapsed";

            if (failure != null) WriteError($"sf-clu -remote {context.Job} failed in {failure.Step} with exit code {failure.ExitCode}: {summary}");
            else if (exitCode != 0) WriteError($"sf-clu -remote {context.Job} built, but the site did not start: {summary}");
            else WriteLine($"sf-clu -remote {context.Job} succeeded: {summary}");

            return failure?.ExitCode ?? exitCode;
        }

        /// <summary>Runs a restore, Clean or build script; exit code 20 from the script means vswhere found no matching Build Tools.</summary>
        private static ProcessResult RunMsbuildScript(RunContext context, string step, string script, bool throwOnFailure)
        {
            var result = RunScript(context, script);

            if (result.ExitCode == 20) throw new RemoteFailure(step, 20, $"vswhere found no {VsWhereProduct} {VsWhereVersionRange} with {VsWhereWorkload} on the host. Install it, or set msbuildPath in the profile.");
            if (throwOnFailure && result.ExitCode != 0) throw new RemoteFailure(step, result.ExitCode, "The step failed");

            return result;
        }
    }
}
