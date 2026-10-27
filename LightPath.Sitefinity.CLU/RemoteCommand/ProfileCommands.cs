using System.Text.RegularExpressions;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private const string InitCommandHint = "sf-clu -remote --init name=<name> solution=<.sln path> host=<ssh alias> root=<mirror root> pool=<app pool> site=<url>";

        private static int ApplySecretGroups(RemoteRequest request)
        {
            var settings = LoadSettings();
            var messages = new List<string>();

            foreach (var group in request.SecretGroups)
            {
                if (!settings.Profiles.TryGetValue(group.Profile!, out var profile)) throw Usage($"No profile named '{group.Profile}'. List them with sf-clu -remote --profiles");

                foreach (var (key, value) in group.Entries)
                {
                    var existing = profile.Secrets.Keys.FirstOrDefault(name => name.Equals(key, StringComparison.OrdinalIgnoreCase));

                    if (group.Remove && existing == null) throw Usage($"Profile '{group.Profile}' has no override named '{key}'. Nothing was changed.");
                    if (existing != null) profile.Secrets.Remove(existing);
                    if (!group.Remove) profile.Secrets.Add(key, MachineSecret.Encrypt(value!));
                }

                var names = string.Join(", ", group.Entries.Select(entry => entry.Key));

                messages.Add(group.Remove ? $"Profile '{group.Profile}': removed {names}" : $"Profile '{group.Profile}': set {names}");
            }

            SaveSettings(settings);
            messages.ForEach(WriteLine);

            return 0;
        }

        private static int InitProfile(RemoteRequest request)
        {
            var settings = LoadSettings();
            var name = ReadProfileName(request);

            if (settings.Profiles.ContainsKey(name)) throw Usage($"Profile '{name}' exists. Change it with sf-clu -remote --profile name={name} <key>=<value>");

            return StoreProfile(settings, name, new RemoteProfile(), request);
        }

        private static int ListProfiles()
        {
            var settings = LoadSettings();

            if (settings.Profiles.Count == 0)
            {
                WriteLine($"No profiles in {SettingsPath()}. Create one with: {InitCommandHint}");

                return 0;
            }

            foreach (var (name, profile) in settings.Profiles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var error = ValidateProfile(profile);

                WriteLine($"{(name == settings.Default ? "*" : " ")} {name}{(name == settings.Default ? " (default)" : string.Empty)}{(error == null ? string.Empty : $" (invalid: {error})")}");

                foreach (var key in RequiredProfileKeys.Concat(OptionalProfileKeys))
                {
                    var value = GetProfileValue(profile, key);

                    if (!string.IsNullOrEmpty(value)) WriteLine($"    {key}={value}");
                }

                if (profile.Secrets.Count > 0) WriteLine($"    secrets: {string.Join(", ", profile.Secrets.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))}");
            }

            return 0;
        }

        private static string ReadProfileName(RemoteRequest request)
        {
            if (!request.Properties.TryGetValue("name", out var name) || !Regex.IsMatch(name, @"^[A-Za-z0-9._-]+$")) throw Usage($"{request.Action} needs name=<name> made of letters, digits, '.', '_' or '-'. Example: {InitCommandHint}");

            return name;
        }

        private static int SaveProfile(RemoteRequest request)
        {
            var settings = LoadSettings();
            var name = ReadProfileName(request);

            return StoreProfile(settings, name, settings.Profiles.TryGetValue(name, out var existing) ? existing.Clone() : new RemoteProfile(), request);
        }

        /// <summary>Applies the request's keys, runs the checks of <see cref="CheckProfile"/>, and saves the profile only when every check passes.</summary>
        private static int StoreProfile(RemoteSettings settings, string name, RemoteProfile profile, RemoteRequest request)
        {
            bool? makeDefault = null;

            if (request.Properties.TryGetValue("default", out var isDefault)) makeDefault = bool.TryParse(isDefault, out var parsed) ? parsed : throw Usage("default must be true or false");

            foreach (var (key, value) in request.Properties.Where(pair => pair.Key is not ("name" or "default")))
            {
                var error = value.Length == 0 ? (RequiredProfileKeys.Contains(key) ? $"{key} cannot be empty" : null) : ValidateValue(key, value);

                if (error != null) throw Usage(error);

                SetProfileValue(profile, key, value.Length == 0 ? null : value);
            }

            var profileError = ValidateProfile(profile);

            if (profileError != null) throw Usage($"Profile '{name}' is not saved: {profileError}. A profile needs solution, host, root, pool and site.");

            try
            {
                CheckProfile(profile, name, request.Verbose);
            }
            catch (RemoteFailure failure)
            {
                throw new RemoteFailure(failure.Step, failure.ExitCode, $"{failure.Message.TrimEnd('.')}. Profile '{name}' is not saved.");
            }

            settings.Profiles[name] = profile;

            if (makeDefault == true) settings.Default = name;
            else if (makeDefault == false && settings.Default == name) settings.Default = null;
            else if (makeDefault == null) settings.Default ??= name;

            SaveSettings(settings);
            WriteLine($"Saved profile '{name}'{(settings.Default == name ? " (default)" : string.Empty)} in {SettingsPath()}");

            return 0;
        }
    }
}
