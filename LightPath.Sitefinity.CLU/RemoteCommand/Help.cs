using System.CommandLine;
using System.Text;
using static LightPath.Sitefinity.CLU.RemoteCommand.Properties;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private static readonly string[] KeyedMarkers = { ActionInit, ActionProfile, ActionAddSecret, ActionRemoveSecret };

        private enum HelpKind { Lines, Paragraphs, Rows }

        private sealed record HelpSection(string Title, HelpKind Kind, (string Term, string Text)[] Items);

        /// <summary>The help page of one job or command, or null for a token that has none.</summary>
        /// <remarks>Methods rather than static fields: initializers in different partial files run in no defined order.</remarks>
        private static HelpSection[]? FindHelpPage(string marker) => marker switch
        {
            JobBuild => BuildJobHelp(JobBuild),
            JobRebuild => BuildJobHelp(JobRebuild),
            JobDeploy => BuildJobHelp(JobDeploy),
            JobRecycle => RecycleHelp(),
            JobStatus => StatusHelp(),
            ActionInit => InitHelp(),
            ActionProfile => ProfileHelp(),
            ActionProfiles => ProfilesHelp(),
            ActionAddSecret => AddSecretHelp(),
            ActionRemoveSecret => RemoveSecretHelp(),
            _ => null
        };

        private static HelpSection HelpLines(string title, params string[] lines) => new(title, HelpKind.Lines, lines.Select(line => (string.Empty, line)).ToArray());

        private static HelpSection HelpOptions(string verbose) => HelpRows("Options", ("--verbose", verbose), ("-?, -h, --help", "Show help and usage information"));

        private static HelpSection HelpParagraphs(string title, params string[] paragraphs) => new(title, HelpKind.Paragraphs, paragraphs.Select(paragraph => (string.Empty, paragraph)).ToArray());

        private static HelpSection HelpRows(string title, params (string Term, string Text)[] rows) => new(title, HelpKind.Rows, rows);

        /// <summary>True for -remote with no job or command, or with a command that needs keys and has none: Program then shows the help page instead of a usage error.</summary>
        public static bool IsBareCommand(string[] args)
        {
            var rest = args.Skip(1).Where(arg => arg != "--verbose").ToList();

            return args.FirstOrDefault() == RemoteCmd.Name && (rest.Count == 0 || (rest.Count == 1 && KeyedMarkers.Contains(rest[0])));
        }

        private static string SettingsPathForHelp()
        {
            try
            {
                return SettingsPath();
            }
            catch (RemoteFailure)
            {
                return "remote.json in the sf-clu folder of the local application data folder";
            }
        }

        private static IEnumerable<string> Wrap(string text, int width)
        {
            var line = new StringBuilder();

            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    yield return line.ToString();

                    line.Clear();
                }

                if (line.Length > 0) line.Append(' ');

                line.Append(word);
            }

            if (line.Length > 0) yield return line.ToString();
        }

        /// <summary>Writes the page of the one job or command in the arguments, or the overview of -remote, in the layout of the System.CommandLine help.</summary>
        public static int WriteHelp(ParseResult parseResult, TextWriter output)
        {
            var markers = parseResult.Tokens.Select(token => token.Value).Where(token => FindHelpPage(token) != null).Distinct().ToList();
            var page = markers.Count == 1 ? FindHelpPage(markers[0])! : OverviewHelp();
            var width = Console.IsOutputRedirected ? int.MaxValue : Math.Max(Console.WindowWidth - 1, 60);

            foreach (var section in page)
            {
                var termWidth = section.Items.Max(item => item.Term.Length);
                var indent = new string(' ', termWidth + 4);

                output.WriteLine($"{section.Title}:");

                for (var index = 0; index < section.Items.Length; index++)
                {
                    var (term, text) = section.Items[index];

                    if (section.Kind == HelpKind.Lines) output.WriteLine($"  {text}");
                    else if (section.Kind == HelpKind.Paragraphs)
                    {
                        if (index > 0) output.WriteLine();

                        foreach (var line in Wrap(text, width - 2)) output.WriteLine($"  {line}");
                    }
                    else
                    {
                        var lines = Wrap(text, Math.Max(width - termWidth - 4, 30)).ToList();

                        output.WriteLine($"  {term.PadRight(termWidth)}  {lines.FirstOrDefault()}".TrimEnd());

                        foreach (var line in lines.Skip(1)) output.WriteLine($"{indent}{line}");
                    }
                }

                output.WriteLine();
            }

            return 0;
        }
    }
}
