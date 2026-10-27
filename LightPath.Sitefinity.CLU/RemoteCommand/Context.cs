using System.Text.RegularExpressions;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private static RunContext CreateContext(RemoteRequest request)
        {
            var settings = LoadSettings();
            var name = request.Properties.GetValueOrDefault("profile") ?? settings.Default ?? throw Usage($"No profile= was given and there is no default profile. Create one with: {InitCommandHint}");

            if (!settings.Profiles.TryGetValue(name, out var stored)) throw Usage($"No profile named '{name}'. List them with sf-clu -remote --profiles");

            var profile = stored.Clone();

            foreach (var (key, value) in request.Properties.Where(pair => pair.Key != "profile"))
            {
                var error = value.Length == 0 ? (RequiredProfileKeys.Contains(key) ? $"{key} cannot be empty" : null) : ValidateValue(key, value);

                if (error != null) throw Usage(error);

                SetProfileValue(profile, key, value.Length == 0 ? null : value);
            }

            if (string.IsNullOrEmpty(profile.Solution)) throw Usage($"Profile '{name}' has no solution. Add it with sf-clu -remote --profile name={name} solution=<path of the .sln>, or create a new profile with: {InitCommandHint}");

            var profileError = ValidateProfile(profile);

            if (profileError != null) throw Usage($"Profile '{name}': {profileError}. Update it with sf-clu -remote --profile name={name} <key>=<value>");

            var context = NewContext(profile, name, request.Job!, request.Verbose);

            if (context.Job == JobDeploy && context.Source.SecretsId == null) throw Usage($"--deploy needs a <UserSecretsId> in project {context.Source.ProjectName}, because it names the folder of secrets.xml");

            WriteLine($"Profile: {name}");
            WriteLine($"Solution: {Path.GetFullPath(profile.Solution!)}");
            WriteLine($"Project: {context.Source.ProjectName}");

            return context;
        }

        private static string DescribeLock(string? content)
        {
            var fields = ParseLock(content);
            var started = DateTimeOffset.TryParse(fields.GetValueOrDefault("started"), out var time) ? time.ToLocalTime() : (DateTimeOffset?)null;
            var when = started == null ? "at an unknown time" : started.Value.Date == DateTime.Today ? $"at {started:HH:mm}" : $"at {started:yyyy-MM-dd HH:mm}";

            return $"a {fields.GetValueOrDefault("job", "run")} started {when} on {fields.GetValueOrDefault("host", "an unknown machine")}";
        }

        private static bool IsLeftoverLock(HostCheck check)
        {
            var fields = ParseLock(check.Lock);

            if (check.LockAgeMinutes >= LockMaxAgeMinutes) return true;
            if (!string.Equals(fields.GetValueOrDefault("host"), Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!int.TryParse(fields.GetValueOrDefault("pid"), out var processId)) return false;

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);

                return process.HasExited;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        /// <summary>The context for a job or for the checks of --init and --profile: derives the source from the solution and the mirror from the profile's root.</summary>
        private static RunContext NewContext(RemoteProfile profile, string name, string job, bool verbose)
        {
            var source = ResolveSource(profile.Solution!, profile.Project, verbose);

            if (source.CloneName.IndexOfAny(UnsafeRootCharacters) >= 0) throw Usage($"The clone folder name '{source.CloneName}' contains a character the push address cannot carry");

            var context = new RunContext { Job = job, Profile = profile, ProfileName = name, RepoName = source.CloneName, RepoRoot = source.ClonePath, Source = source, Verbose = verbose };

            context.Mirror = $@"{profile.Root!.Replace('/', '\\').TrimEnd('\\')}\{source.CloneName}";
            context.MirrorPattern = new Regex(@"(?<!file:///)" + Regex.Escape(context.Mirror + "\\"), RegexOptions.IgnoreCase); // file:/// URLs keep the full path
            context.LockContent = $"host={Environment.MachineName}\npid={Environment.ProcessId}\njob={context.Job}\nstarted={context.Started:O}\n";

            return context;
        }

        private static Dictionary<string, string> ParseLock(string? content) => (content ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0])
            .ToDictionary(group => group.Key, group => group.First()[1]);
    }
}
