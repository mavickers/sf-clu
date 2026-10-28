// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private const string QuoteNote = "Key names are not case sensitive. A value is everything after the first '='. Bash and zsh remove a backslash, so quote Windows paths: root='D:\\builds'.";

        private static HelpSection[] AddSecretHelp() => new[]
        {
            HelpParagraphs("Description", "Adds overrides to a profile, or replaces them. --deploy delivers the developer's secrets.xml to the host; an override replaces the entry of the same name in that file, or is added to it. Use overrides for values that must differ on the host. The change takes effect on the next --deploy.", "Overrides are stored in remote.json encrypted with a key derived from an identifier of this machine, so a profile with overrides cannot be copied to another machine. --profiles shows the names of the overrides, never their values."),
            HelpLines("Usage", $"sf-clu -remote {ActionAddSecret} profile=<name> key=<entry> value=<value> [key=<entry> value=<value>...]", $"sf-clu -remote {ActionAddSecret} profile=<name> key=<entry> value=<value> {ActionRemoveSecret} profile=<name> key=<entry>"),
            HelpRows("Keys",
                ("profile=<name>", "Required, once per group. The profile that holds the overrides."),
                ("key=<entry>", "Required, one or more. The name of the secrets.xml entry. It can contain colons. The name comparison is not case sensitive."),
                ("value=<value>", "Required directly after each key=. The value of the entry. value= with nothing after it sets an empty string.")),
            HelpParagraphs("Rules", $"One command can hold several {ActionAddSecret} and {ActionRemoveSecret} groups. Each group starts at its marker and needs one profile= and at least one key=. A key= can appear only once in a command. The command changes nothing unless every group is valid.", "Error messages never print a value= token, so a secret does not reach the terminal. Quote a value that contains spaces or shell characters."),
            HelpLines("Examples", $"sf-clu -remote {ActionAddSecret} profile=dev key=Smtp:Host value=mail.example.com", $"sf-clu -remote {ActionAddSecret} profile=dev key=Smtp:Host value=mail.example.com key=Smtp:Port value=25", $"sf-clu -remote {ActionAddSecret} profile=dev key=Smtp:Host value=mail.example.com {ActionRemoveSecret} profile=dev key=OldEntry"),
            HelpOptions("Accepted, has no effect")
        };

        private static HelpSection[] InitHelp() => new[]
        {
            HelpParagraphs("Description", "Creates a profile. A profile describes one site on one build host: the local solution, the host, and the IIS site that serves it. --init runs the checks below, prints one line for each check that passes, and saves the profile only when every check passes.", $"The first saved profile becomes the default profile, which a job uses when it has no profile=. Profiles are stored in {SettingsPathForHelp()}."),
            HelpLines("Usage", $"sf-clu -remote {ActionInit} name=<name> solution=<.sln path> host=<ssh alias> root=<mirror root> pool=<app pool> site=<url> [<key>=<value>...] [default=true] [--verbose]"),
            HelpRows("Keys", ProfileKeyRows(true)),
            HelpRows("Checks", ProfileChecks()),
            HelpParagraphs("Warnings", "Two conditions print a warning and do not stop the save, because only --deploy needs them: a web project without <UserSecretsId>, and a pool without an APPDATA environment variable when the profile has no secretsPath."),
            HelpParagraphs("Notes", QuoteNote, "--init refuses a name that already exists. Change an existing profile with --profile."),
            HelpRows("Exit codes", ProfileExitCodes()),
            HelpLines("Examples", $"sf-clu -remote {ActionInit} name=dev solution=/Users/me/Repos/MySite/MySite.sln host=build-host root='D:\\builds' pool=MySite site=https://mysite.example.com/", $"sf-clu -remote {ActionInit} name=test solution=/Users/me/Repos/MySite/MySite.sln project=MySite.Web host=build-host root='D:\\builds' pool=MySiteTest site=https://test.mysite.example.com/ default=true"),
            HelpOptions("Print each command sent to the host (never a script)")
        };

        /// <summary>The checks that --init and --profile run before they save.</summary>
        private static (string, string)[] ProfileChecks() => new[]
        {
            ("1. Solution", "The solution is inside a git clone and has one Sitefinity web project, or project= names one."),
            ("2. SSH", "SSH to host works, and the SSH default shell of the host is cmd.exe."),
            ("3. Pool", "The pool has one IIS application, and its physical path is <root>\\<clone folder name>\\<web folder>."),
            ("4. MSBuild", "msbuildPath exists on the host, or vswhere finds the MSBuild 18 Build Tools with the web build workload.")
        };

        private static (string, string)[] ProfileExitCodes() => new[]
        {
            ("0", "The checks passed and the profile is saved"),
            ("1", "Usage error, or the solution check failed. Nothing is saved."),
            ("10", "The SSH default shell of the host is not cmd.exe"),
            ("11", "The pool has no single IIS application, or its physical path is not the mirror's web folder"),
            ("20", "MSBuild was not found on the host"),
            ("255", "ssh failed: the alias, the key or the network")
        };

        private static HelpSection[] ProfileHelp() => new[]
        {
            HelpParagraphs("Description", "Changes the keys of a profile. --profile runs the same checks as --init, prints one line for each check that passes, and saves the profile only when every check passes. When no profile has the name, --profile creates it, like --init.", $"Only the keys on the command change; the other keys keep their values. Profiles are stored in {SettingsPathForHelp()}."),
            HelpLines("Usage", $"sf-clu -remote {ActionProfile} name=<name> <key>=<value>... [default=true|false] [--verbose]"),
            HelpRows("Keys", ProfileKeyRows(false)),
            HelpRows("Checks", ProfileChecks()),
            HelpParagraphs("Notes", QuoteNote, "An empty value (msbuild=) clears an optional key. solution, host, root, pool and site cannot be cleared.", $"Change the secrets.xml overrides with {ActionAddSecret} and {ActionRemoveSecret}."),
            HelpRows("Exit codes", ProfileExitCodes()),
            HelpLines("Examples", $"sf-clu -remote {ActionProfile} name=dev host=build-host-2", $"sf-clu -remote {ActionProfile} name=dev msbuild=", $"sf-clu -remote {ActionProfile} name=test default=true"),
            HelpOptions("Print each command sent to the host (never a script)")
        };

        /// <summary>The rows of the profile keys for --init (required keys marked) or --profile (every key optional).</summary>
        private static (string, string)[] ProfileKeyRows(bool init) => new[]
        {
            ("name=<name>", init ? "Required. The profile name: letters, digits, '.', '_' and '-'." : "Required. The name of the profile to change."),
            ("solution=<.sln path>", (init ? "Required. " : string.Empty) + "Absolute local path of the .sln file. The solution must be inside a git clone; the mirror on the host is named after the clone's folder."),
            ("host=<ssh alias>", (init ? "Required. " : string.Empty) + "SSH alias of the build host, from ~/.ssh/config. No spaces, and it must not start with '-'."),
            ("root=<mirror root>", (init ? "Required. " : string.Empty) + "Absolute Windows path of the mirror root on the host, for example 'D:\\builds'. The mirror is <root>\\<clone folder name>. It must not contain ' \" % & | ^ < >."),
            ("pool=<app pool>", (init ? "Required. " : string.Empty) + "Name of the IIS application pool that serves the site."),
            ("site=<url>", (init ? "Required. " : string.Empty) + "http or https URL of the site. --deploy requests it to wait for Sitefinity to start."),
            ("project=<name>", (init ? "Optional. " : string.Empty) + "Name of the Sitefinity web project, as the .sln lists it. Needed only when the solution has zero or several Sitefinity web projects."),
            ("msbuild=<arguments>", (init ? "Optional. " : string.Empty) + "Extra MSBuild arguments, separated by spaces; double quotes group words. It must not set OutDir, because a Clean can delete the output folder."),
            ("msbuildPath=<path>", (init ? "Optional. " : string.Empty) + "Full Windows path of MSBuild.exe on the host. Replaces the vswhere lookup. It must be MSBuild 16.5 or later."),
            ("secretsPath=<path>", (init ? "Optional. " : string.Empty) + "Full Windows path of secrets.xml on the host (the file, not its folder). Without it, --deploy writes to the folder in the APPDATA variable of the pool."),
            (init ? "default=true" : "default=true|false", init ? "Optional. Makes this profile the default." : "true makes this profile the default; false removes the default when it is this profile.")
        };

        private static HelpSection[] ProfilesHelp() => new[]
        {
            HelpParagraphs("Description", $"Lists the profiles in {SettingsPathForHelp()}. The default profile is marked with '*'. Each profile shows its keys and the names of its secrets.xml overrides, never their values. A profile that fails validation is marked invalid with the reason."),
            HelpLines("Usage", $"sf-clu -remote {ActionProfiles}"),
            HelpParagraphs("Notes", "--profiles takes no key=value tokens. It reads remote.json only and does not connect to a host."),
            HelpOptions("Accepted, has no effect")
        };

        private static HelpSection[] RemoveSecretHelp() => new[]
        {
            HelpParagraphs("Description", $"Removes overrides from a profile. The next --deploy delivers the developer's secrets.xml without them. Add or replace overrides with {ActionAddSecret}."),
            HelpLines("Usage", $"sf-clu -remote {ActionRemoveSecret} profile=<name> key=<entry> [key=<entry>...]"),
            HelpRows("Keys",
                ("profile=<name>", "Required, once per group. The profile that holds the overrides."),
                ("key=<entry>", "Required, one or more. The name of an override to remove. The name comparison is not case sensitive.")),
            HelpParagraphs("Rules", $"One command can hold several {ActionAddSecret} and {ActionRemoveSecret} groups. Each group starts at its marker and needs one profile= and at least one key=. A {ActionRemoveSecret} group takes no value=. A name that the profile does not hold is an error, and the command then changes nothing."),
            HelpLines("Examples", $"sf-clu -remote {ActionRemoveSecret} profile=dev key=Smtp:Host", $"sf-clu -remote {ActionRemoveSecret} profile=dev key=Smtp:Host key=Smtp:Port"),
            HelpOptions("Accepted, has no effect")
        };
    }
}
