using System.CommandLine;
using System.CommandLine.Parsing;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private static readonly string[] JobKeys = { "profile", "solution", "project", "host", "root", "pool", "site", "msbuild", "msbuildPath", "secretsPath" };
        private static readonly string[] JobMarkers = { JobBuild, JobRebuild, JobDeploy, JobRecycle, JobStatus };
        private static readonly string[] ProfileKeys = { "name", "default", "solution", "project", "host", "root", "pool", "site", "msbuild", "msbuildPath", "secretsPath" };

        private static Dictionary<string, string> ParseKeyValues(IEnumerable<string> tokens, string[] validKeys)
        {
            var values = new Dictionary<string, string>();

            foreach (var token in tokens)
            {
                var separator = token.IndexOf('=');

                if (separator <= 0) throw Usage($"Unknown token '{token}'. Use key=value with one of: {string.Join(", ", validKeys)}");

                var name = token[..separator];
                var key = validKeys.FirstOrDefault(validKey => validKey.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (key == null) throw Usage($"Unknown key '{name}'. Valid keys: {string.Join(", ", validKeys)}");
                if (!values.TryAdd(key, token[(separator + 1)..])) throw Usage($"The key '{key}' appears twice");
            }

            return values;
        }

        private static RemoteRequest ParseRequest(ParseResult parseResult)
        {
            var request = new RemoteRequest();
            var actions = new List<string>();
            var jobs = new List<string>();
            var loose = new List<string>();
            SecretGroup? group = null;

            foreach (var token in parseResult.Tokens.Where(token => token.Type != TokenType.Command).Select(token => token.Value))
            {
                if (token == "--verbose") request.Verbose = true;
                else if (JobMarkers.Contains(token)) jobs.Add(token);
                else if (token is ActionInit or ActionProfile or ActionProfiles) actions.Add(token);
                else if (token is "--add-secret" or "--remove-secret")
                {
                    group = new SecretGroup { Remove = token == "--remove-secret" };
                    request.SecretGroups.Add(group);
                    actions.Add(token);
                }
                else if (group != null) group.Tokens.Add(token);
                else loose.Add(token);
            }

            var markerList = string.Join(", ", JobMarkers);

            if (jobs.Count == 0 && actions.Count == 0) throw Usage($"Give one job marker ({markerList}), or --init, --profile, --profiles, --add-secret or --remove-secret");
            if (jobs.Count > 1) throw Usage($"Give exactly one job marker: {markerList}");
            if (jobs.Count == 1 && actions.Count > 0) throw Usage($"{actions[0]} cannot be combined with a job marker");
            if (actions.Contains(ActionInit) && actions.Count > 1) throw Usage("--init cannot be combined with --profile, --profiles, --add-secret or --remove-secret");
            if (actions.Contains(ActionProfile) && actions.Count > 1) throw Usage("--profile cannot be combined with --init, --profiles, --add-secret or --remove-secret");
            if (actions.Contains(ActionProfiles) && actions.Count > 1) throw Usage("--profiles cannot be combined with --init, --profile, --add-secret or --remove-secret");

            if (jobs.Count == 1)
            {
                request.Job = jobs[0];
                request.Properties = ParseKeyValues(loose, JobKeys);
            }
            else if (actions[0] is ActionInit or ActionProfile)
            {
                request.Action = actions[0];
                request.Properties = ParseKeyValues(loose, ProfileKeys);
            }
            else if (actions[0] == ActionProfiles)
            {
                if (loose.Count > 0) throw Usage("--profiles takes no key=value tokens");

                request.Action = ActionProfiles;
            }
            else
            {
                if (loose.Count > 0) throw Usage("Every key=value token must follow an --add-secret or --remove-secret");

                request.Action = ActionSecrets;
                ParseSecretGroups(request.SecretGroups);
            }

            return request;
        }

        /// <remarks>Error messages name a value= token by its position only, so a secret never reaches the terminal.</remarks>
        private static void ParseSecretGroups(List<SecretGroup> groups)
        {
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var marker = group.Remove ? "--remove-secret" : "--add-secret";
                string? pendingKey = null;

                foreach (var token in group.Tokens)
                {
                    var separator = token.IndexOf('=');
                    var name = separator > 0 ? token[..separator].ToLowerInvariant() : string.Empty;
                    var value = separator > 0 ? token[(separator + 1)..] : string.Empty;

                    if (pendingKey != null && name != "value") throw Usage($"key={pendingKey} in {marker} needs value= directly after it");

                    switch (name)
                    {
                        case "profile":
                            if (group.Profile != null) throw Usage($"profile= appears twice in one {marker} group");

                            group.Profile = value;
                            break;
                        case "key":
                            if (value.Length == 0) throw Usage($"key= in {marker} needs an entry name");
                            if (!seenKeys.Add(value)) throw Usage($"key={value} appears twice in this command");

                            if (group.Remove) group.Entries.Add(new(value, null));
                            else pendingKey = value;
                            break;
                        case "value":
                            if (group.Remove) throw Usage("--remove-secret takes no value= token");
                            if (pendingKey == null) throw Usage("A value= token in --add-secret has no key= directly before it");

                            group.Entries.Add(new(pendingKey, value));
                            pendingKey = null;
                            break;
                        default:
                            throw Usage($"Unknown token '{(separator > 0 ? token[..separator] + "=..." : token)}' in {marker}. A group holds profile=, key= and, for --add-secret, value=");
                    }
                }

                if (pendingKey != null) throw Usage($"key={pendingKey} in {marker} needs value= directly after it");
                if (string.IsNullOrEmpty(group.Profile)) throw Usage($"Every {marker} group needs profile=<name>");
                if (group.Entries.Count == 0) throw Usage($"Every {marker} group needs at least one key=");
            }
        }

        private static RemoteFailure Usage(string message) => new(null, 1, message);
    }
}
