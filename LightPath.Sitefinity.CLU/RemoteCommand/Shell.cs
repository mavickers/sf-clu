using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private static readonly Regex MarkerPattern = new(@"^clu-([a-z]+)(?: ([A-Za-z0-9+/=]*))?$");
        private static readonly object OutputLock = new();

        private static string Decode(string base64)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                return base64;
            }
        }

        private static string GitCapture(RunContext context, IDictionary<string, string>? environment, params string[] arguments)
        {
            var result = Run(context, "git", arguments, environment: environment, echo: false);

            if (result.ExitCode != 0) throw new RemoteFailure(null, result.ExitCode, $"git {arguments[0]} failed: {string.Join(" ", result.Error)}");

            return string.Join("\n", result.Output).Trim();
        }

        private static void Receive(RunContext context, ProcessResult result, string? line, bool isError, bool echo)
        {
            if (line == null) return;

            lock (OutputLock)
            {
                var marker = MarkerPattern.Match(line);

                if (marker.Success)
                {
                    result.Markers.Add(new(marker.Groups[1].Value, Decode(marker.Groups[2].Value)));

                    return;
                }

                var leadingBlank = line.Length == 0 && result.Output.Count == 0 && result.Error.Count == 0;

                (isError ? result.Error : result.Output).Add(line);

                if (echo && !leadingBlank) (isError ? Console.Error : Console.Out).WriteLine(context.MirrorPattern?.Replace(line, string.Empty) ?? line);
            }
        }

        /// <summary>Runs a program with each argument passed as-is, never through a local shell, and streams its output line by line.</summary>
        /// <remarks>Lines that start with "clu-" are markers from a host script: they are collected, not printed.</remarks>
        private static ProcessResult Run
        (
            RunContext context,
            string fileName,
            IEnumerable<string> arguments,
            string? input = null,
            IDictionary<string, string>? environment = null,
            bool echo = true
        )
        {
            var result = new ProcessResult();
            var startInfo = new ProcessStartInfo(fileName) { RedirectStandardError = true, RedirectStandardInput = input != null, RedirectStandardOutput = true, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8, UseShellExecute = false, WorkingDirectory = context.RepoRoot };

            if (input != null) startInfo.StandardInputEncoding = new UTF8Encoding(false);

            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            foreach (var (name, value) in environment ?? new Dictionary<string, string>()) startInfo.Environment[name] = value;

            WriteVerbose(context, $"> {fileName} {string.Join(" ", startInfo.ArgumentList.Select(argument => argument.Contains(' ') ? $"'{argument}'" : argument))}");

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (_, args) => Receive(context, result, args.Data, false, echo);
            process.ErrorDataReceived += (_, args) => Receive(context, result, args.Data, true, echo);

            try
            {
                process.Start();
            }
            catch (Win32Exception exception)
            {
                throw new RemoteFailure(null, 127, $"{fileName} could not be started: {exception.Message}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (input != null)
            {
                try
                {
                    process.StandardInput.Write(input);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The program exited before it read its input; its exit code reports why.
                }
            }

            process.WaitForExit();
            result.ExitCode = process.ExitCode;

            return result;
        }

        private static ProcessResult RunScript(RunContext context, string script) => Run(context, "ssh", new[] { context.Profile.Host!, StepCommand }, input: script);

        private static void WriteError(string message)
        {
            lock (OutputLock)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(message);
                Console.ResetColor();
            }
        }

        private static void WriteLine(string message)
        {
            lock (OutputLock) Console.Out.WriteLine(message);
        }

        private static void WriteVerbose(RunContext context, string message)
        {
            if (context.Verbose) WriteLine(message);
        }
    }
}
