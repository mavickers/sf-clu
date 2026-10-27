using System.CommandLine;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        /// <remarks>Synchronous on purpose: System.CommandLine ends an async action two seconds after Ctrl+C, which would skip step 9 and leave the run lock on the host.</remarks>
        public static int Handler(ParseResult parseResult)
        {
            try
            {
                var request = ParseRequest(parseResult);

                if (request.Action == ActionInit) return InitProfile(request);
                if (request.Action == ActionProfile) return SaveProfile(request);
                if (request.Action == ActionProfiles) return ListProfiles();
                if (request.Action == ActionSecrets) return ApplySecretGroups(request);

                var context = CreateContext(request);

                return request.Job is JobRecycle or JobStatus ? RunHostJob(context) : RunBuild(context);
            }
            catch (RemoteFailure failure)
            {
                WriteError(failure.Step == null ? failure.Message : $"{failure.Step}: {failure.Message}");

                return failure.ExitCode;
            }
        }
    }
}
