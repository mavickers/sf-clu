using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private static readonly string[] OptionalProfileKeys = { "project", "msbuild", "msbuildPath", "secretsPath" };
        private static readonly string[] RequiredProfileKeys = { "solution", "host", "root", "pool", "site" };
        private static readonly JsonSerializerOptions SettingsJsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        private static readonly char[] UnsafeRootCharacters = { '\'', '"', '%', '&', '|', '^', '<', '>' };

        private static string? GetProfileValue(RemoteProfile profile, string key) => key switch
        {
            "host" => profile.Host,
            "msbuild" => profile.Msbuild,
            "msbuildPath" => profile.MsbuildPath,
            "pool" => profile.Pool,
            "project" => profile.Project,
            "root" => profile.Root,
            "secretsPath" => profile.SecretsPath,
            "site" => profile.Site,
            "solution" => profile.Solution,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };

        private static bool IsWindowsPath(string value) => Regex.IsMatch(value, @"^[A-Za-z]:[\\/]") && !value.Any(char.IsControl);

        private static RemoteSettings LoadSettings()
        {
            var path = SettingsPath();
            RemoteSettings? settings;

            if (!File.Exists(path)) return new RemoteSettings();

            try
            {
                settings = JsonSerializer.Deserialize<RemoteSettings>(File.ReadAllText(path), SettingsJsonOptions);
            }
            catch (JsonException exception)
            {
                throw Usage($"{path} is not valid JSON: {exception.Message}");
            }

            settings ??= new RemoteSettings();
            settings.Profiles = new Dictionary<string, RemoteProfile>(settings.Profiles ?? new(), StringComparer.Ordinal);

            foreach (var (name, profile) in settings.Profiles)
            {
                try
                {
                    profile.Secrets = new Dictionary<string, string>(profile.Secrets ?? new(), StringComparer.OrdinalIgnoreCase);
                }
                catch (ArgumentException)
                {
                    throw Usage($"Profile '{name}' in {path} has two secrets overrides whose names differ only in case");
                }
            }

            if (settings.Default != null && !settings.Profiles.ContainsKey(settings.Default)) throw Usage($"The default profile '{settings.Default}' in {path} does not exist");

            return settings;
        }

        private static void SaveSettings(RemoteSettings settings)
        {
            var path = SettingsPath();
            var temporary = $"{path}.{Environment.ProcessId}.tmp";

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, SettingsJsonOptions));
            File.Move(temporary, path, true);
        }

        private static void SetProfileValue(RemoteProfile profile, string key, string? value)
        {
            switch (key)
            {
                case "host": profile.Host = value; break;
                case "msbuild": profile.Msbuild = value; break;
                case "msbuildPath": profile.MsbuildPath = value; break;
                case "pool": profile.Pool = value; break;
                case "project": profile.Project = value; break;
                case "root": profile.Root = value; break;
                case "secretsPath": profile.SecretsPath = value; break;
                case "site": profile.Site = value; break;
                case "solution": profile.Solution = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(key));
            }
        }

        private static string SettingsFolder()
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrEmpty(localData)) throw Usage("This machine has no local application data folder for sf-clu settings");

            return Path.Combine(localData, "sf-clu");
        }

        private static string SettingsPath() => Path.Combine(SettingsFolder(), "remote.json");

        private static string? ValidateProfile(RemoteProfile profile)
        {
            foreach (var key in RequiredProfileKeys.Concat(OptionalProfileKeys))
            {
                var value = GetProfileValue(profile, key);

                if (string.IsNullOrEmpty(value) && RequiredProfileKeys.Contains(key)) return $"{key} is missing";
                if (string.IsNullOrEmpty(value)) continue;

                var error = ValidateValue(key, value);

                if (error != null) return error;
            }

            return null;
        }

        private static string? ValidateValue(string key, string value) => key switch
        {
            "host" when value.Length == 0 || value.StartsWith('-') || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) => "host must be an SSH alias without spaces that does not start with '-'",
            "msbuild" when Regex.IsMatch(value, @"[/-]p(roperty)?:\s*""?OutDir\s*=", RegexOptions.IgnoreCase) => "msbuild must not set OutDir: a Clean of a web project can delete the output folder (a SpicNSpan target does)",
            "msbuildPath" when !IsWindowsPath(value) || !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) => "msbuildPath must be the full Windows path of MSBuild.exe on the host",
            "pool" when value.Length == 0 || value.Contains('"') || value.Any(char.IsControl) => "pool must be an IIS application pool name without '\"'",
            "project" when value.Any(char.IsControl) => "project must be the name of a project in the solution",
            "root" when !IsWindowsPath(value) || value.IndexOfAny(UnsafeRootCharacters) >= 0 => @"root must be an absolute Windows path such as C:\Repos, without ' "" % & | ^ < >",
            "secretsPath" when !IsWindowsPath(value) => "secretsPath must be the full Windows path of secrets.xml on the host",
            "site" when !Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) => "site must be an http or https URL",
            "solution" when !Path.IsPathFullyQualified(value) || !value.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || !File.Exists(value) => "solution must be the absolute local path of an existing .sln file",
            _ => null
        };
    }
}
