using System.Text.RegularExpressions;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private class BuildOutcome
        {
            public int? Errors { get; set; }
            public bool OfflineWritten { get; set; }
            public int? Warnings { get; set; }
        }

        private class HostCheck
        {
            public string? AppData { get; set; }
            public string? Lock { get; set; }
            public int LockAgeMinutes { get; set; }
            public bool LockedOut { get; set; }
            public bool Offline { get; set; }
        }

        private class ProcessResult
        {
            public List<string> Error { get; } = new();
            public int ExitCode { get; set; }
            public List<KeyValuePair<string, string>> Markers { get; } = new();
            public List<string> Output { get; } = new();

            public string? Marker(string name) => Markers.Where(marker => marker.Key == name).Select(marker => marker.Value).FirstOrDefault();

            public IEnumerable<string> MarkerValues(string name) => Markers.Where(marker => marker.Key == name).Select(marker => marker.Value);
        }

        public class RemoteFailure(string? step, int exitCode, string message) : Exception(message)
        {
            public int ExitCode { get; } = exitCode;
            public string? Step { get; } = step;
        }

        public class RemoteProfile
        {
            public string? Host { get; set; }
            public string? Msbuild { get; set; }
            public string? MsbuildPath { get; set; }
            public string? Pool { get; set; }
            public string? Project { get; set; }
            public string? Root { get; set; }
            public Dictionary<string, string> Secrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public string? SecretsPath { get; set; }
            public string? Site { get; set; }
            public string? Solution { get; set; }

            public RemoteProfile Clone()
            {
                var clone = (RemoteProfile)MemberwiseClone();

                clone.Secrets = new Dictionary<string, string>(Secrets, StringComparer.OrdinalIgnoreCase);

                return clone;
            }
        }

        public class RemoteRequest
        {
            public string? Action { get; set; }
            public string? Job { get; set; }
            public Dictionary<string, string> Properties { get; set; } = new();
            public List<SecretGroup> SecretGroups { get; } = new();
            public bool Verbose { get; set; }
        }

        public class RemoteSettings
        {
            public string? Default { get; set; }
            public Dictionary<string, RemoteProfile> Profiles { get; set; } = new();
        }

        private class RunContext
        {
            public HostCheck? Check { get; set; }
            public string Job { get; set; } = string.Empty;
            public string LockContent { get; set; } = string.Empty;
            public string Mirror { get; set; } = string.Empty;
            public Regex? MirrorPattern { get; set; }
            public List<string> MissingPackages { get; set; } = new();
            public BuildOutcome Outcome { get; } = new();
            public List<string> PrivatePackages { get; set; } = new();
            public RemoteProfile Profile { get; set; } = new();
            public string ProfileName { get; set; } = string.Empty;
            public string PushAddress => $"{Profile.Host}:{Profile.Root!.Replace('\\', '/').TrimEnd('/')}/{RepoName}";
            public string RepoName { get; set; } = string.Empty;
            public string RepoRoot { get; set; } = string.Empty;
            public byte[]? SecretsFile { get; set; }
            public string? Sha { get; set; }
            public Dictionary<string, string> SnapshotEnvironment { get; set; } = new();
            public SitefinitySource Source { get; set; } = new();
            public DateTimeOffset Started { get; } = DateTimeOffset.Now;
            public bool Verbose { get; set; }
        }

        public class SecretGroup
        {
            public List<KeyValuePair<string, string?>> Entries { get; } = new();
            public string? Profile { get; set; }
            public bool Remove { get; set; }
            public List<string> Tokens { get; } = new();
        }

        /// <summary>The values a run derives from the profile's solution; paths are relative to the clone root and use '/', except the absolute PackagesConfigs.</summary>
        private class SitefinitySource
        {
            public string CloneName { get; set; } = string.Empty;
            public string ClonePath { get; set; } = string.Empty;
            public string? HealthPath { get; set; }
            public string? NuGetConfig { get; set; }
            public List<string> PackagesConfigs { get; set; } = new();
            public string ProjectName { get; set; } = string.Empty;
            public string? SecretsId { get; set; }
            public string Solution { get; set; } = string.Empty;
            public string SolutionFolder { get; set; } = string.Empty;
            public string StatusPath { get; set; } = string.Empty;
            public string WebFolder { get; set; } = string.Empty;
        }
    }
}
