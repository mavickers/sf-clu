using System.CommandLine;

namespace LightPath.Sitefinity.CLU
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine();

            var rootCommand = new RootCommand("Sitefinity Command Line Utility (CLU)");

            if (!args.Any()) args = new[] { "-h" };

            rootCommand.SetAction(_ => 1);

            GenKeysCommand.Initialize(rootCommand);
            GenSyncCommand.Initialize(rootCommand);
            RemoteCommand.Initialize(rootCommand);

            return await rootCommand.Parse(args).InvokeAsync();
        }
    }
}
