using System.Security.Cryptography;
using System.Text;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        /// <summary>Step 2: commits the working tree as it is on disk without touching the branch, the index, the stash or the reflog.</summary>
        /// <remarks>
        /// The objects go to a scratch folder kept across runs, so the next push finds the commit the host's ref names and sends only new objects.
        /// commit-tree runs no hooks, and the fixed identity keeps it working in a clone with no user.email.
        /// </remarks>
        private static string MakeSnapshot(RunContext context)
        {
            var indexPath = GitCapture(context, null, "rev-parse", "--path-format=absolute", "--git-path", "index");
            var objectsPath = GitCapture(context, null, "rev-parse", "--path-format=absolute", "--git-path", "objects");
            var scratch = Path.Combine(SettingsFolder(), "snapshots", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(objectsPath)))[..16].ToLowerInvariant());
            var scratchObjects = Path.Combine(scratch, "objects");
            var temporaryIndex = Path.Combine(scratch, $"index-{Environment.ProcessId}");

            Directory.CreateDirectory(scratchObjects);
            if (File.Exists(indexPath)) File.Copy(indexPath, temporaryIndex, true);

            context.SnapshotEnvironment = new Dictionary<string, string> { ["GIT_ALTERNATE_OBJECT_DIRECTORIES"] = objectsPath, ["GIT_OBJECT_DIRECTORY"] = scratchObjects };

            var indexEnvironment = new Dictionary<string, string>(context.SnapshotEnvironment) { ["GIT_INDEX_FILE"] = temporaryIndex };
            var commitEnvironment = new Dictionary<string, string>(context.SnapshotEnvironment) { ["GIT_AUTHOR_EMAIL"] = "sf-clu@localhost", ["GIT_AUTHOR_NAME"] = "sf-clu", ["GIT_COMMITTER_EMAIL"] = "sf-clu@localhost", ["GIT_COMMITTER_NAME"] = "sf-clu" };

            try
            {
                GitCapture(context, indexEnvironment, "add", "-A");
                GitCapture(context, indexEnvironment, "rm", "-r", "--cached", "--quiet", "--ignore-unmatch", "--", WebPath(context, "App_Data/Sitefinity/Search"));

                var tree = GitCapture(context, indexEnvironment, "write-tree");
                var parentResult = Run(context, "git", new[] { "rev-parse", "--verify", "--quiet", "HEAD^{commit}" }, echo: false);
                var arguments = new List<string> { "commit-tree", tree, "--no-gpg-sign", "-m", $"sf-clu snapshot {context.Started:O}" };

                if (parentResult.ExitCode == 0) arguments.AddRange(new[] { "-p", parentResult.Output.First().Trim() });

                var sha = GitCapture(context, commitEnvironment, arguments.ToArray());

                WriteLine($"Snapshot {sha[..10]} of the working tree{(parentResult.ExitCode == 0 ? $" on {parentResult.Output.First()[..10]}" : string.Empty)}");

                return sha;
            }
            finally
            {
                File.Delete(temporaryIndex);
            }
        }

        /// <summary>Step 4: force-pushes the snapshot to the CLU's own ref; git sends only the objects the host lacks.</summary>
        /// <remarks>PowerShell starts receive-pack because cmd.exe, the host's default shell, would pass git's single quotes around the path through.</remarks>
        private static void PushSnapshot(RunContext context, string sha)
        {
            var result = Run(context, "git", new[] { "push", "--quiet", "--no-verify", $"--receive-pack={ReceivePack}", context.PushAddress, $"+{sha}:{SnapshotRef}" }, environment: context.SnapshotEnvironment);

            if (result.ExitCode != 0) throw new RemoteFailure(StepPush, result.ExitCode, $"git push to {context.PushAddress} failed; the site still serves the previous build");

            // The scratch store keeps the pushed snapshot for the next push; the two-week expiry spares objects a concurrent snapshot from this clone has not yet committed.
            Run(context, "git", new[] { "prune", "--expire=2.weeks.ago", sha }, environment: context.SnapshotEnvironment, echo: false);
        }
    }
}
