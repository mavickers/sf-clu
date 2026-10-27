using System.CommandLine.Parsing;
using System.Text.RegularExpressions;
using LightPath.Factor;

namespace LightPath.Sitefinity.CLU
{
    public static partial class GenSyncCommand
    {
        private const string hostnamePattern = @"^([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$";

        public static Action<OptionResult> SecretValidator
        {
            get
            {
                return result =>
                {
                    var value = result.GetValueOrDefault<string>();

                    if (!string.IsNullOrEmpty(value) && value.Length < 16) result.AddError("Auth secret must be at least 16 characters long");
                };
            }
        }

        public static Action<OptionResult> SiteKeyValidator => result =>
        {
            if (!TryEvalSiteKey(result.GetValueOrDefault<string?>(), out var evalResult)) result.AddError(evalResult.Error.Messages.First());
        };

        private static bool TryEvalSiteKey(string? value, out Result<int> result)
        {
            result = Result.Success(int.MinValue);

            if (string.IsNullOrWhiteSpace(value)) return result.Succeeded(20).Return(true);
            if (int.TryParse(value, out var parsedValue) && parsedValue is > 0 and < 251) return result.Succeeded(parsedValue).Return(true);

            return result.Failed(int.MinValue)
                         .WithError(Result.ResultError.ValueError.WithMessage("Auth SiteKey must be an integer value between 1 and 250"))
                         .Return(false);
        }

        public static Action<ArgumentResult> TargetsValidator = result =>
        {
            var targets = result.GetValueOrDefault<List<TargetModel>>();

            if (!targets?.Any() ?? true) return;

            var parentSiteKey = result.Parent?.GetValue(Properties.SiteKeyOption);
            var byPassSiteKeyCompare = !TryEvalSiteKey(parentSiteKey, out var parsedSiteKeyResult);
            var hasDuplicateTargetIds = targets.Where(target => target.Id.HasValue).GroupBy(target => target.Id).Any(id => id.Count() > 1);
            var hasNoTargetSecrets = targets.All(target => string.IsNullOrWhiteSpace(target.Secret));
            var targetsSecret = TargetModel.RandomSecret;

            if (hasNoTargetSecrets) targets.ForEach(target => target.Secret = targetsSecret);
            else targets.Where(target => string.IsNullOrWhiteSpace(target.Secret)).ToList().ForEach(target => target.Secret = TargetModel.RandomSecret);

            if (hasDuplicateTargetIds)
            {
                result.AddError("Target IDs must be unique GUIDs");
             
                return;
            }

            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.Hostname.Trim()))
                {
                    result.AddError("Target hostnames are required");

                    return;
                }

                if (!Regex.IsMatch(target.Hostname.Trim(), hostnamePattern))
                {
                    result.AddError("Target hostnames must be valid domain names (e.g., dev.example.com)");

                    return;
                }

                if (target.Id.HasValue && target.Id == Guid.Empty)
                {
                    result.AddError("Target IDs must be valid and unique GUIDs");

                    return;
                }

                if (target.SiteKey.HasValue)
                {
                    if (target.SiteKey < 1 || target.SiteKey.Value > 250)
                    {
                        result.AddError("Target SiteKeys must be integer values between 1 and 250");

                        return;
                    }

                    if (!byPassSiteKeyCompare && target.SiteKey.Value <= parsedSiteKeyResult.Value)
                    {
                        result.AddError("Target SiteKeys must be greater than the parent SiteKey");

                        return;
                    }
                }

                if (!string.IsNullOrEmpty(target.Secret) && target.Secret.Length < 16)
                {
                    result.AddError("Target secrets must be at least 16 characters long");

                    return;
                }
            }
        };
    }
}
