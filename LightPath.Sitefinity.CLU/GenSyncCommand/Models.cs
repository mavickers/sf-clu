using System.Security.Cryptography;

namespace LightPath.Sitefinity.CLU
{
    public static partial class GenSyncCommand
    {
        public class TargetModel
        {
            private const string SecretChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~|?<>!#$%&'()*+,;=@[]^{}:/";

            private string? _name { get; set; }
            private int? _siteKey { get; set; }
            private Guid? _id { get; set; }

            public string Hostname { get; set; } = string.Empty;
            public string? Name { get => _name ?? Hostname; set => _name = value; }
            public string? Secret { get; set; }
            public int? SiteKey { get => _siteKey ?? 50; set => _siteKey = value; }
            public Guid? Id { get => _id ??= Guid.NewGuid(); set => _id = value; }

            public static string RandomSecret => RandomNumberGenerator.GetString(SecretChars, 64);
        }
    }
}
