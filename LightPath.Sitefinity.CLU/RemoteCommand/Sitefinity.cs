using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private const string DefaultStatusPath = "/sitefinity/status";
        private static readonly Regex SolutionProjectPattern = new(@"^Project\(""\{[^}]+\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+\.csproj)""", RegexOptions.IgnoreCase);

        /// <summary>Joins the solution folder's prefix from git and a path relative to the solution folder; null when the result leaves the clone.</summary>
        private static string? CloneRelative(string prefix, string relative)
        {
            var segments = new List<string>();

            foreach (var segment in $"{prefix}/{relative}".Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".") continue;
                if (segment != "..") segments.Add(segment);
                else if (segments.Count == 0) return null;
                else segments.RemoveAt(segments.Count - 1);
            }

            return string.Join("/", segments);
        }

        /// <returns>The web folder of the mirror on the host, as a Windows path.</returns>
        /// <summary>The first NuGet.Config, matched without regard to case, in the solution folder or a parent folder up to the clone root.</summary>
        private static string? FindNuGetConfig(string clonePath, string solutionFolder)
        {
            for (var folder = solutionFolder; ; folder = folder.Contains('/') ? folder[..folder.LastIndexOf('/')] : string.Empty)
            {
                var file = Directory.EnumerateFiles(Path.Combine(clonePath, folder)).FirstOrDefault(path => Path.GetFileName(path).Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase));

                if (file != null) return InFolder(folder, Path.GetFileName(file));
                if (folder.Length == 0) return null;
            }
        }

        private static string HostWebFolder(RunContext context) => context.Source.WebFolder.Length == 0 ? context.Mirror : $@"{context.Mirror}\{WindowsPath(context.Source.WebFolder)}";

        private static string InFolder(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

        private static bool IsWebApplication(XDocument project) => project.Descendants().Any(element => element.Name.LocalName == "Import" && (element.Attribute("Project")?.Value ?? string.Empty).EndsWith("Microsoft.WebApplication.targets", StringComparison.OrdinalIgnoreCase));

        private static XDocument LoadXml(string path)
        {
            try
            {
                return XDocument.Load(path);
            }
            catch (XmlException exception)
            {
                throw Usage($"{path} is not valid XML: {exception.Message}");
            }
        }

        private static bool ReferencesSitefinity(XDocument project, string folder)
        {
            static bool IsSitefinity(string? name) => name != null && (name.StartsWith("Telerik.Sitefinity", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Progress.Sitefinity", StringComparison.OrdinalIgnoreCase));

            if (project.Descendants().Any(element => element.Name.LocalName == "Reference" && (element.Attribute("Include")?.Value.Split(',')[0].Trim() ?? string.Empty).StartsWith("Telerik.Sitefinity", StringComparison.OrdinalIgnoreCase))) return true;

            var packages = Path.Combine(folder, "packages.config");

            return File.Exists(packages) && (LoadXml(packages).Root?.Elements("package").Any(package => IsSitefinity(package.Attribute("id")?.Value)) ?? false);
        }

        /// <summary>Finds the clone and the Sitefinity web project of a solution, and the values a run derives from them.</summary>
        /// <remarks>Every run calls this again, so a renamed web project or a changed key on a branch is seen by the next run; nothing derived is stored in the profile.</remarks>
        private static SitefinitySource ResolveSource(string solution, string? projectName, bool verbose)
        {
            var solutionFolder = Path.GetDirectoryName(solution)!;
            var gitResult = Run(new RunContext { RepoRoot = solutionFolder, Verbose = verbose }, "git", new[] { "-C", solutionFolder, "rev-parse", "--show-toplevel", "--show-prefix" }, echo: false);

            if (gitResult.ExitCode != 0) throw Usage($"{solution} is not inside a git clone: {string.Join(" ", gitResult.Error)}");

            var clonePath = gitResult.Output[0].Trim();
            var prefix = gitResult.Output.Count > 1 ? gitResult.Output[1].Trim() : string.Empty;
            var projects = File.ReadLines(solution)
                .Select(line => SolutionProjectPattern.Match(line))
                .Where(match => match.Success)
                .Select(match => (Name: match.Groups[1].Value, Path: Path.GetFullPath(Path.Combine(solutionFolder, match.Groups[2].Value.Replace('\\', Path.DirectorySeparatorChar)))))
                .ToList();
            var candidates = projects.Where(project => File.Exists(project.Path))
                .Select(project => (project.Name, project.Path, Document: LoadXml(project.Path)))
                .Select(project => (project.Name, project.Path, project.Document, IsWeb: IsWebApplication(project.Document), IsSitefinity: ReferencesSitefinity(project.Document, Path.GetDirectoryName(project.Path)!)))
                .ToList();
            var matches = candidates.Where(project => project.IsWeb && project.IsSitefinity).ToList();

            if (projectName == null && matches.Count > 1) throw Usage($"{solution} has {matches.Count} web application projects that reference Sitefinity: {string.Join(", ", matches.Select(project => project.Name))}. Name one with project=<name>");
            if (projectName == null && matches.Count == 0)
            {
                var webProjects = candidates.Where(project => project.IsWeb).Select(project => project.Name).ToList();

                throw Usage($"{solution} has no web application project that references Sitefinity{(webProjects.Count > 0 ? $". Its web application projects: {string.Join(", ", webProjects)}. Name one with project=<name>" : string.Empty)}");
            }

            var chosen = projectName == null ? matches[0] : candidates.FirstOrDefault(project => project.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            if (chosen.Name == null) throw Usage($"{solution} has no project named '{projectName}'. Its projects: {string.Join(", ", projects.Select(project => project.Name))}");
            if (!chosen.IsWeb) WriteLine($"Warning: project {chosen.Name} does not import Microsoft.WebApplication.targets, so it is not a web application project; it is used because project= names it");
            if (!chosen.IsSitefinity) WriteLine($"Warning: project {chosen.Name} references no Telerik.Sitefinity or Progress.Sitefinity package or assembly; it is used because project= names it");

            var projectFolder = Path.GetDirectoryName(chosen.Path)!;
            var webFolder = CloneRelative(prefix, Path.GetRelativePath(solutionFolder, projectFolder)) ?? throw Usage($"Project {chosen.Name} is outside the clone {clonePath}");
            var webConfig = Directory.EnumerateFiles(projectFolder).FirstOrDefault(file => Path.GetFileName(file).Equals("Web.config", StringComparison.OrdinalIgnoreCase));
            var appSettings = webConfig == null ? new Dictionary<string, string>() : (LoadXml(webConfig).Root?.Element("appSettings")?.Elements("add") ?? Enumerable.Empty<XElement>())
                .Where(add => add.Attribute("key") != null)
                .GroupBy(add => add.Attribute("key")!.Value)
                .ToDictionary(group => group.Key, group => group.Last().Attribute("value")?.Value.Trim() ?? string.Empty);
            var secretsId = chosen.Document.Descendants().FirstOrDefault(element => element.Name.LocalName == "UserSecretsId")?.Value.Trim();
            var healthPath = appSettings.GetValueOrDefault("sf:HealthCheckApiEndpoint");
            var statusPath = appSettings.GetValueOrDefault("sf:appStatusUrl");
            var cloneSolutionFolder = CloneRelative(prefix, ".")!;

            return new SitefinitySource
            {
                CloneName = Path.GetFileName(clonePath.TrimEnd('/', '\\')),
                ClonePath = clonePath,
                HealthPath = string.IsNullOrEmpty(healthPath) ? null : healthPath,
                NuGetConfig = FindNuGetConfig(clonePath, cloneSolutionFolder),
                PackagesConfigs = candidates.Select(project => Path.Combine(Path.GetDirectoryName(project.Path)!, "packages.config")).Where(File.Exists).Distinct().ToList(),
                ProjectName = chosen.Name,
                SecretsId = string.IsNullOrEmpty(secretsId) ? null : secretsId,
                Solution = CloneRelative(prefix, Path.GetFileName(solution))!,
                SolutionFolder = cloneSolutionFolder,
                StatusPath = string.IsNullOrEmpty(statusPath) ? DefaultStatusPath : statusPath,
                WebFolder = webFolder
            };
        }

        /// <returns>A path under the solution folder, relative to the clone root.</returns>
        private static string SolutionPath(RunContext context, string name) => InFolder(context.Source.SolutionFolder, name);

        /// <returns>A path under the web folder, relative to the clone root.</returns>
        private static string WebPath(RunContext context, string name) => InFolder(context.Source.WebFolder, name);

        private static string WindowsPath(string path) => path.Replace('/', '\\');
    }
}
