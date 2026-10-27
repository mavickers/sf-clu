using System.CommandLine;
using System.Text;
using System.Text.Json;

namespace LightPath.Sitefinity.CLU
{
    public static partial class GenSyncCommand
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static async Task<int> Handler(ParseResult parseResult, CancellationToken cancellationToken)
        {
            var withVerbose = parseResult.GetValue(Properties.VerboseOption) ?? false;

            try
            {
                var secret = parseResult.GetValue(Properties.SecretOption) ?? TargetModel.RandomSecret;
                var siteKey = int.TryParse(parseResult.GetValue(Properties.SiteKeyOption), out var parsedSiteKey) ? parsedSiteKey : -1;
                var targets = parseResult.GetValue(Properties.TargetsArgument);

                if (siteKey < 1) throw new Exception("Local SiteKey is invalid");

                var config = GenerateConfiguration(targets, siteKey, secret);
                var jsonString = JsonSerializer.Serialize(config, JsonOptions);

                await Console.Out.WriteAsync(jsonString);

                return 0;
            }
            catch (Exception exception)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                await Console.Error.WriteLineAsync($"Error: {exception.Message}");
                Console.ResetColor();

                if (withVerbose) await Console.Error.WriteLineAsync($"Stack Trace: {exception.StackTrace}");

                return 1;
            }
        }

        private static object GenerateConfiguration(List<TargetModel> targets, int? siteKey, string secret)
        {
            var authConfig = new
            {
                siteKey,
                secret,
                targets = targets.Select(t => new
                {
                    t.Id,
                    t.Name,
                    host = $"https://{t.Hostname}",
                    t.Secret
                }).ToList()
            };

            var targetConfigs = new Dictionary<string, object>();

            foreach (var target in targets)
            {
                targetConfigs[target.Name] = new
                {
                    target.SiteKey,
                    target.Secret,
                    targets = new List<object>()
                };
            }

            var authConfigJson = JsonSerializer.Serialize(authConfig, JsonOptions);

            var configSection = new
            {
                auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(authConfigJson)),
                targets = targetConfigs.ToDictionary(kvp => kvp.Key, kvp => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(kvp.Value, JsonOptions))))
            };

            return new
            {
                configs = configSection,
                auth = authConfig,
                targets = targetConfigs
            };
        }

    }
}
