// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private const string JobVerbose = "Print each command that runs on the client (> ssh ..., > git ...) and the time of each step. Host scripts are never printed.";

        /// <summary>The page of --build, --rebuild or --deploy: one list of steps, with the steps that only one of them runs.</summary>
        private static HelpSection[] BuildJobHelp(string job)
        {
            var deploy = job == JobDeploy;
            var description = job switch
            {
                JobRebuild => "Runs --build with a Clean between the restore and the build. Use it when a stale output file breaks the build.",
                JobDeploy => "Runs --build, delivers the developer's secrets.xml with the profile's overrides to the host, and waits until Sitefinity has started. The web project needs a <UserSecretsId>, because it names the folder of secrets.xml: ~/.microsoft/usersecrets/<UserSecretsId>/secrets.xml on macOS, %APPDATA%\\Microsoft\\UserSecrets\\<UserSecretsId>\\secrets.xml on Windows.",
                _ => "Takes the site offline, sends a snapshot of the working tree to the mirror on the host, restores and builds there with MSBuild, and brings the site back."
            };
            var steps = new List<(string, string)>
            {
                ("1. Checks", "Client. packages/ has a folder for every packages.config entry. Finds the private packages (those that no nuget.org source serves)." + (deploy ? " Merges secrets.xml with the profile's overrides." : string.Empty)),
                ("2. Snapshot", "Client. Commits the working tree as it is on disk, without changing the branch, the index or the stash. <web folder>/App_Data/Sitefinity/Search is left out, so the search indexes on the host remain."),
                ("3. Host check", "Host. Checks the SSH default shell and that the pool serves the mirror's web folder" + (deploy ? ", and the pool's APPDATA" : string.Empty) + ". Creates the mirror on the first run and takes the run lock. Nothing on the site changes before this step succeeds."),
                ("4. Push", "Pushes the snapshot to the mirror. The site keeps serving the previous build."),
                ("5. Checkout", "Host. Writes app_offline.htm and checks out the snapshot. Lists the tracked files the checkout discards and the Sitefinity configuration files that exist only on the host."),
                ("6. Private packages", "Copies the private package folders that the mirror lacks, with sftp."),
                ("7. Restore", "Host. MSBuild /t:Restore, including packages.config projects, with the NuGet.Config of the clone.")
            };

            if (job == JobRebuild) steps.Add(("7b. Clean", "Host. MSBuild /t:Clean with the arguments of the build."));

            steps.Add(("8. Build", "Host. MSBuild with /p:Configuration=Debug and the profile's msbuild arguments. Prints the error and warning counts."));

            if (deploy) steps.Add(("8b. Secrets", "Host. Writes the merged secrets.xml to <APPDATA>\\Microsoft\\UserSecrets\\<UserSecretsId>\\secrets.xml, or to secretsPath."));

            steps.Add(("9. Finish", "Host. After a success, deletes app_offline.htm. After a failure from step 5 on, replaces it with a page that names the failed step. Releases the run lock. Runs after a failure or Ctrl+C too."));

            if (deploy) steps.Add(("10. Startup", "Client. Requests site every 2 seconds for at most 10 minutes, until Sitefinity answers with something other than its startup response. A status below 500 is success."));

            var exitCodes = new List<(string, string)>
            {
                ("0", "Success"),
                ("1", "Usage error, or a failed client check: no single Sitefinity web project, or missing package folders" + (deploy ? ", or no secrets.xml on this machine, or the site did not start" : string.Empty)),
                ("10", "The SSH default shell of the host is not cmd.exe"),
                ("11", "The pool has no single IIS application, or its physical path is not the mirror's web folder")
            };

            if (deploy) exitCodes.Add(("12", "The pool has no APPDATA variable and the profile has no secretsPath"));

            exitCodes.AddRange(new[]
            {
                ("13", "Another run holds the run lock (<mirror>\\.git\\clu.lock)"),
                ("14", "The mirror folder has an origin remote, so it is a clone and not a mirror"),
                ("20", "vswhere found no MSBuild on the host, and the profile has no msbuildPath"),
                ("127", "git, ssh or sftp could not be started on this machine"),
                ("130", "Cancelled with Ctrl+C"),
                ("255", "ssh failed: the alias, the key or the network"),
                ("other", "The exit code of the failed command: MSBuild, git, sftp or a host script")
            });

            return new[]
            {
                HelpParagraphs("Description", description, "The job uses the default profile, or the profile that profile= names. Keys on the command change the profile for this run only."),
                HelpLines("Usage", $"sf-clu -remote {job} [profile=<name>] [<key>=<value>...] [--verbose]"),
                HelpRows("Keys", JobKeyRows()),
                HelpRows("Steps", steps.ToArray()),
                HelpParagraphs("Notes", "While app_offline.htm is in the web folder, every request gets that page. After a failure it remains and names the failed step, so a browser shows why the site is down; the next successful run removes it.", "Ctrl+C stops the run after the current step, and step 9 still runs. A lock older than 60 minutes, or one left by a process on this machine that no longer runs, is taken over.", QuoteNote),
                HelpRows("Exit codes", exitCodes.ToArray()),
                HelpLines("Examples", $"sf-clu -remote {job}", $"sf-clu -remote {job} profile=test", $"sf-clu -remote {job} msbuild=/p:RunCodeAnalysis=false --verbose"),
                HelpOptions(JobVerbose)
            };
        }

        /// <summary>The rows of the keys a job takes: profile= and a one-run override of each profile key.</summary>
        private static (string, string)[] JobKeyRows() => new[]
        {
            ("profile=<name>", "The profile to use. Without it, the job uses the default profile (marked '*' by --profiles)."),
            ("<key>=<value>", "Changes one profile key for this run only; nothing is saved. Keys: solution, project, host, root, pool, site, msbuild, msbuildPath, secretsPath. The --init checks do not run for the change; step 3 checks the host. See sf-clu -remote --init -h for each key.")
        };

        private static HelpSection[] OverviewHelp() => new[]
        {
            HelpParagraphs("Description", "Builds, deploys, recycles or checks a Sitefinity web project on a Windows build host over SSH. The tool sends a snapshot of the working tree to a mirror repository on the host, then restores and builds there with MSBuild. IIS on the host serves the web folder of the mirror.", $"A profile names the local solution, the host and the IIS site. Create one with {ActionInit} before the first job. Profiles are stored in {SettingsPathForHelp()}."),
            HelpLines("Usage",
                "sf-clu -remote <job> [profile=<name>] [<key>=<value>...] [--verbose]",
                $"sf-clu -remote {ActionInit} name=<name> solution=<.sln path> host=<ssh alias> root=<mirror root> pool=<app pool> site=<url> [<key>=<value>...]",
                $"sf-clu -remote {ActionProfile} name=<name> <key>=<value>...",
                $"sf-clu -remote {ActionProfiles}",
                $"sf-clu -remote {ActionAddSecret} profile=<name> key=<entry> value=<value> [key=<entry> value=<value>...]",
                $"sf-clu -remote {ActionRemoveSecret} profile=<name> key=<entry> [key=<entry>...]"),
            HelpRows("Jobs",
                (JobBuild, "Take the site offline, send a snapshot of the working tree, restore and build on the host, and bring the site back"),
                (JobRebuild, "--build with a Clean between the restore and the build"),
                (JobDeploy, "--build, then deliver secrets.xml and wait until Sitefinity has started"),
                (JobRecycle, "Recycle the app pool"),
                (JobStatus, "Report the pool state, the health check, a running job and the offline page")),
            HelpRows("Profiles",
                (ActionInit, "Create a profile after checking the solution and the host"),
                (ActionProfile, "Change the keys of a profile, with the same checks as --init"),
                (ActionProfiles, "List the profiles and mark the default"),
                (ActionAddSecret, "Add or replace secrets.xml overrides that --deploy delivers"),
                (ActionRemoveSecret, "Remove secrets.xml overrides")),
            HelpOptions(JobVerbose),
            HelpParagraphs("More help", "Each job and command has its own page with its keys, steps or checks, exit codes and examples: sf-clu -remote <job or command> -h, for example sf-clu -remote --init -h.")
        };

        private static HelpSection[] RecycleHelp() => new[]
        {
            HelpParagraphs("Description", "Recycles the IIS application pool of the profile, if the pool is started. The next request starts a new worker process and a cold start of Sitefinity. It also reports a running job and an existing app_offline.htm."),
            HelpLines("Usage", $"sf-clu -remote {JobRecycle} [profile=<name>] [<key>=<value>...] [--verbose]"),
            HelpRows("Keys", JobKeyRows()),
            HelpRows("Steps",
                ("3. Host check", "Host. Checks the SSH default shell and that the pool serves the mirror's web folder. Does not take the run lock, so it works while a build runs."),
                ("Recycle", "Host. Recycles the pool when its state is Started. Otherwise prints the appcmd command that starts it.")),
            HelpRows("Exit codes",
                ("0", "The pool was recycled"),
                ("1", "Usage error, or the pool is not started"),
                ("10", "The SSH default shell of the host is not cmd.exe"),
                ("11", "The pool has no single IIS application, or its physical path is not the mirror's web folder"),
                ("255", "ssh failed: the alias, the key or the network"),
                ("other", "The exit code of appcmd or the host script")),
            HelpLines("Examples", $"sf-clu -remote {JobRecycle}", $"sf-clu -remote {JobRecycle} profile=test"),
            HelpOptions(JobVerbose)
        };

        private static HelpSection[] StatusHelp() => new[]
        {
            HelpParagraphs("Description", "Reports the state of the site without changing it: the state of the pool, the health check, a running job (the holder of the run lock) and an existing app_offline.htm.", "The health check requests the path in sf:HealthCheckApiEndpoint in the <appSettings> of the web project's Web.config, read from the clone. It expects HTTP 200 and reads the Healthy value in the response body, because a site can answer 200 when it is unhealthy. It lists the failed checks. A web project without that key prints Healthy: not configured, and health then has no effect on the exit code."),
            HelpLines("Usage", $"sf-clu -remote {JobStatus} [profile=<name>] [<key>=<value>...] [--verbose]"),
            HelpRows("Keys", JobKeyRows()),
            HelpRows("Steps",
                ("3. Host check", "Host. Checks the SSH default shell and that the pool serves the mirror's web folder. Does not take the run lock, so it works while a build runs."),
                ("Pool", "Host. Prints the state of the pool."),
                ("Health", "Client. Requests the health path on site and prints the verdict.")),
            HelpParagraphs("Notes", "The health service also needs healthCheckConfig enabled=\"True\" in Sitefinity's SystemConfig.config. The tool does not read that file, so a health path with the service turned off gives no verdict."),
            HelpRows("Exit codes",
                ("0", "The pool is started, and the site is healthy or has no health path"),
                ("1", "Usage error, the pool is not started, the site is unhealthy, or the health check gave no verdict"),
                ("10", "The SSH default shell of the host is not cmd.exe"),
                ("11", "The pool has no single IIS application, or its physical path is not the mirror's web folder"),
                ("255", "ssh failed: the alias, the key or the network"),
                ("other", "The exit code of appcmd or the host script")),
            HelpLines("Examples", $"sf-clu -remote {JobStatus}", $"sf-clu -remote {JobStatus} profile=test"),
            HelpOptions(JobVerbose)
        };
    }
}
