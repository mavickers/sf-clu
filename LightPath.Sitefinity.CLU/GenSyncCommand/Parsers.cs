using System.CommandLine.Parsing;

namespace LightPath.Sitefinity.CLU
{
    public static partial class GenSyncCommand
    {
        public static List<TargetModel> TargetsArgumentParser(ArgumentResult result)
        {
            var tokens = result.Tokens.ToList();
            var index = 0;
            var targets = new List<TargetModel>();
            TargetModel currentTarget = null;

            while (index < tokens.Count)
            {
                if (tokens[index].Value == "--target")
                {
                    if (currentTarget != null) targets.Add(currentTarget);

                    currentTarget = new TargetModel();
                    index++;

                    continue;
                }

                var args = tokens[index].Value.Split('=');
                var key = args[0].Trim();
                var value = args.Length > 1 ? string.Join("=", args.Skip(1)) : string.Empty;

                if (key == "hostname") currentTarget.Hostname = value;
                if (key == "name") currentTarget.Name = value;
                if (key == "secret") currentTarget.Secret = value;
                if (key == "sitekey") currentTarget.SiteKey = int.TryParse(value, out var siteKeyResult) ? siteKeyResult : int.MinValue;
                if (key == "id") currentTarget.Id = Guid.TryParse(value, out var id) ? id : Guid.Empty;

                index++;
            }

            targets.Add(currentTarget);

            return targets;
        }
    }
}
