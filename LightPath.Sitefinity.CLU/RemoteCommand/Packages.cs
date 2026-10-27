using System.Text;
using System.Xml;
using System.Xml.Linq;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        /// <summary>Step 1: every packages.config entry of the solution's projects needs its folder in the solution's packages/; returns the private ones the host cannot download.</summary>
        private static List<string> CheckPackages(RunContext context)
        {
            var files = context.Source.PackagesConfigs;
            var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                try
                {
                    foreach (var package in XDocument.Load(file).Root?.Elements("package") ?? Enumerable.Empty<XElement>()) packages.TryAdd($"{package.Attribute("id")?.Value}.{package.Attribute("version")?.Value}", package.Attribute("id")?.Value ?? string.Empty);
                }
                catch (XmlException exception)
                {
                    throw new RemoteFailure(StepChecks, 1, $"{file} is not valid XML: {exception.Message}");
                }
            }

            var missing = packages.Keys.Where(folder => !Directory.Exists(Path.Combine(context.RepoRoot, SolutionPath(context, "packages"), folder))).OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ToList();

            if (missing.Count > 0) throw new RemoteFailure(StepChecks, 1, $"packages/ lacks {missing.Count} package folder(s):\n  {string.Join("\n  ", missing)}\nRestore on this machine first: nuget restore {context.Source.Solution}");

            var mapping = ReadSourceMapping(context);
            var privatePackages = packages.Where(package => IsPrivatePackage(mapping, package.Value)).Select(package => package.Key).OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ToList();

            WriteLine($"{packages.Count} package folders present; {privatePackages.Count} private: {string.Join(", ", privatePackages)}");

            return privatePackages;
        }

        /// <summary>Step 6: copies the private package folders the mirror lacks, each to a temporary name renamed when complete.</summary>
        /// <remarks>Remote paths start with "/C:/": Win32-OpenSSH's sftp-server reads "C:/" as a path under the home folder.</remarks>
        private static void CopyPackages(RunContext context, List<string> missing)
        {
            if (missing.Count == 0)
            {
                WriteLine("No private package is missing on the host");

                return;
            }

            var remote = $"/{context.Mirror.Replace('\\', '/')}/{SolutionPath(context, "packages")}";
            var batch = new StringBuilder($"-mkdir \"{remote}\"\n");
            var batchPath = Path.Combine(SettingsFolder(), $"sftp-{Environment.ProcessId}.txt");

            foreach (var folder in missing)
            {
                var partial = $"{remote}/{folder}.clu-partial-{Guid.NewGuid():N}";

                batch.Append($"put -r \"{Path.Combine(context.RepoRoot, SolutionPath(context, "packages"), folder)}\" \"{partial}\"\nrename \"{partial}\" \"{remote}/{folder}\"\n");
            }

            Directory.CreateDirectory(SettingsFolder());
            File.WriteAllText(batchPath, batch.ToString());

            try
            {
                var result = Run(context, "sftp", new[] { "-b", batchPath, context.Profile.Host! });

                if (result.ExitCode != 0) throw new RemoteFailure(StepCopy, result.ExitCode, $"sftp failed copying {string.Join(", ", missing)}");
            }
            finally
            {
                File.Delete(batchPath);
            }
        }

        /// <summary>A package is private when its most specific source-mapping pattern sends it to no nuget.org source.</summary>
        private static bool IsPrivatePackage(List<KeyValuePair<string, string>> mapping, string id)
        {
            static int Specificity(string pattern) => pattern.EndsWith('*') ? pattern.Length - 1 : int.MaxValue;
            static bool Matches(string pattern, string id) => pattern.EndsWith('*') ? id.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase) : id.Equals(pattern, StringComparison.OrdinalIgnoreCase);

            var best = mapping.Where(entry => Matches(entry.Key, id)).GroupBy(entry => Specificity(entry.Key)).OrderByDescending(group => group.Key).FirstOrDefault();

            return best != null && !best.Any(entry => Uri.TryCreate(entry.Value, UriKind.Absolute, out var uri) && uri.Host.EndsWith("nuget.org", StringComparison.OrdinalIgnoreCase));
        }

        /// <returns>Pairs of package pattern and source URL from the NuGet.Config that the resolver found.</returns>
        private static List<KeyValuePair<string, string>> ReadSourceMapping(RunContext context)
        {
            if (context.Source.NuGetConfig == null) return new List<KeyValuePair<string, string>>();

            var root = LoadXml(Path.Combine(context.RepoRoot, context.Source.NuGetConfig)).Root;
            var sources = root?.Element("packageSources")?.Elements("add").ToDictionary(add => add.Attribute("key")?.Value ?? string.Empty, add => add.Attribute("value")?.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>();

            return root?.Element("packageSourceMapping")?.Elements("packageSource")
                .SelectMany(source => source.Elements("package").Select(package => new KeyValuePair<string, string>(package.Attribute("pattern")?.Value ?? string.Empty, sources.GetValueOrDefault(source.Attribute("key")?.Value ?? string.Empty, string.Empty))))
                .ToList() ?? new List<KeyValuePair<string, string>>();
        }
    }
}
