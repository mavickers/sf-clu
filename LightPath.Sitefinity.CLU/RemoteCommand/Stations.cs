using LightPath.Cargo;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private static readonly Dictionary<Type, string> StationSteps = new()
        {
            [typeof(BuildStation)] = StepBuild,
            [typeof(CheckoutStation)] = StepCheckout,
            [typeof(ChecksStation)] = StepChecks,
            [typeof(CleanStation)] = StepClean,
            [typeof(CopyStation)] = StepCopy,
            [typeof(FinishStation)] = StepFinish,
            [typeof(HostCheckStation)] = StepHostCheck,
            [typeof(PushStation)] = StepPush,
            [typeof(RestoreStation)] = StepRestore,
            [typeof(SecretsStation)] = StepSecrets,
            [typeof(SnapshotStation)] = StepSnapshot
        };

        /// <summary>Steps 1-9 for a build job; 7b and 8b only for the jobs that need them, and step 9 as the final station.</summary>
        /// <remarks>Abort-on-cancel is required: Cargo's default throws out of the bus on Ctrl+C and skips step 9, which leaves the lock and the offline page on the host.</remarks>
        private static Bus<RunContext> BuildBus(RunContext context)
        {
            var bus = Bus.New<RunContext>().WithAbortOnError().WithAbortOnCancel().WithStationObserver(new StepObserver(context));

            bus.WithStation<ChecksStation>().WithStation<SnapshotStation>().WithStation<HostCheckStation>().WithStation<PushStation>();
            bus.WithStation<CheckoutStation>().WithStation<CopyStation>().WithStation<RestoreStation>();
            if (context.Job == JobRebuild) bus.WithStation<CleanStation>();
            bus.WithStation<BuildStation>();
            if (context.Job == JobDeploy) bus.WithStation<SecretsStation>();

            return bus.WithFinalStation<FinishStation>();
        }

        private static RemoteFailure? FirstFailure(IEnumerable<Station.Result> results) => results.Where(result => result.WasFailure).Select(ToFailure).FirstOrDefault();

        /// <summary>Gives every failed result a step and an exit code; an exception other than <see cref="RemoteFailure"/> exits 1, a cancel 130.</summary>
        private static RemoteFailure ToFailure(Station.Result result)
        {
            var step = StationSteps[result.Station];

            return result.Exception switch
            {
                RemoteFailure { Step: not null } failure => failure,
                RemoteFailure failure => new RemoteFailure(step, failure.ExitCode, failure.Message),
                OperationCanceledException => new RemoteFailure(step, 130, "Cancelled"),
                { } exception => new RemoteFailure(step, 1, exception.Message),
                null => new RemoteFailure(step, 1, result.ActionMessage ?? "The step failed")
            };
        }

        private class BuildStation : StepStation
        {
            protected override void Run() => Build(Context, Context.Outcome);
        }

        private class CheckoutStation : StepStation
        {
            protected override void Run() => Context.MissingPackages = CheckOut(Context, Context.Sha!, Context.PrivatePackages, Context.Outcome);
        }

        private class ChecksStation : StepStation
        {
            protected override void Run()
            {
                Context.PrivatePackages = CheckPackages(Context);
                Context.SecretsFile = Context.Job == JobDeploy ? BuildSecretsFile(Context) : null;
            }
        }

        private class CleanStation : StepStation
        {
            protected override void Run() => RunMsbuildScript(Context, StepClean, BuildScript(Context, true), true);
        }

        private class CopyStation : StepStation
        {
            protected override void Run() => CopyPackages(Context, Context.MissingPackages);
        }

        /// <summary>Step 9; runs after a failure or a cancel too, and does nothing when step 3 did not take the lock.</summary>
        private class FinishStation : StepStation
        {
            protected override void Run()
            {
                if (Context.Check != null) Finish(Context, FirstFailure(PackageResults));
            }
        }

        private class HostCheckStation : StepStation
        {
            protected override void Run() => Context.Check = LockHost(Context);
        }

        private class PushStation : StepStation
        {
            protected override void Run() => PushSnapshot(Context, Context.Sha!);
        }

        private class RestoreStation : StepStation
        {
            protected override void Run() => RunMsbuildScript(Context, StepRestore, RestoreScript(Context), true);
        }

        private class SecretsStation : StepStation
        {
            protected override void Run() => DeliverSecrets(Context, Context.Check!, Context.SecretsFile!);
        }

        private class SnapshotStation : StepStation
        {
            protected override void Run() => Context.Sha = MakeSnapshot(Context);
        }

        /// <summary>Prints each step header, each failure and, with --verbose, each step time; step 9 stays silent when it has no lock to release.</summary>
        private class StepObserver(RunContext context) : IStationObserver
        {
            private bool _printed;

            public void Finished(Station.Result result)
            {
                if (_printed) WriteVerbose(context, $"   {StationSteps[result.Station]} took {result.Elapsed.TotalSeconds:0.0} s");

                _printed = false;

                if (!result.WasFailure) return;

                var failure = ToFailure(result);

                WriteError($"{failure.Step}: {failure.Message}");
            }

            public void Starting(Type station, int iteration)
            {
                _printed = station != typeof(FinishStation) || context.Check != null;

                if (_printed) WriteLine($"-- {StationSteps[station]}");
            }
        }

        /// <summary>The bus creates a new instance for every run, so a value that a later step needs goes on the context, never on the station.</summary>
        private abstract class StepStation : Station<RunContext>
        {
            protected RunContext Context => Package.Contents;

            public override Station.Action Process()
            {
                Run();

                return Station.Action.Next();
            }

            protected abstract void Run();
        }
    }
}
