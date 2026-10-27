using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        /// <summary>For <c>--deploy</c>, in step 1: the developer's secrets.xml with the profile's overrides merged in, as the bytes the host receives.</summary>
        /// <remarks>An override replaces the entry whose name matches without regard to case, as .NET configuration keys do; other names are added.</remarks>
        private static byte[] BuildSecretsFile(RunContext context)
        {
            var path = LocalSecretsPath(context);
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            XDocument document;

            if (!File.Exists(path)) throw new RemoteFailure(StepChecks, 1, $"--deploy needs secrets.xml at {path}. Copy it from a Windows developer machine.");

            foreach (var (name, cipher) in context.Profile.Secrets)
            {
                try
                {
                    overrides[name] = MachineSecret.Decrypt(cipher);
                }
                catch (Exception exception) when (exception is CryptographicException or FormatException)
                {
                    throw new RemoteFailure(StepChecks, 1, $"The override '{name}' in profile '{context.ProfileName}' does not decrypt on this machine. Set it again with: sf-clu -remote --add-secret profile={context.ProfileName} key={name} value=<value>");
                }
            }

            try
            {
                document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException exception)
            {
                throw new RemoteFailure(StepChecks, 1, $"{path} is not valid XML: {exception.Message}");
            }

            var secrets = document.Root?.Element("secrets") ?? throw new RemoteFailure(StepChecks, 1, $"{path} has no <secrets> element");

            foreach (var (name, value) in overrides)
            {
                var existing = secrets.Elements("secret").FirstOrDefault(secret => string.Equals(secret.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));

                if (existing != null) existing.SetAttributeValue("value", value);
                else secrets.Add(new XElement("secret", new XAttribute("name", name), new XAttribute("value", value)));
            }

            using var stream = new MemoryStream();

            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) })) document.Save(writer);

            return stream.ToArray();
        }

        /// <summary>Writes the merged secrets.xml under the pool's APPDATA folder that the host check read in this run, or to the profile's secretsPath.</summary>
        private static void DeliverSecrets(RunContext context, HostCheck check, byte[] content)
        {
            var target = context.Profile.SecretsPath ?? $@"{check.AppData!.TrimEnd('\\')}\Microsoft\UserSecrets\{context.Source.SecretsId}\secrets.xml";
            var result = RunScript(context, SecretsScript(target, content));

            if (result.ExitCode != 0) throw new RemoteFailure(StepSecrets, result.ExitCode, $"Writing {target} on the host failed");

            WriteLine($"Wrote secrets.xml to {target} with {context.Profile.Secrets.Count} override(s)");
        }

        private static string LocalSecretsPath(RunContext context) => OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", context.Source.SecretsId!, "secrets.xml")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", context.Source.SecretsId!, "secrets.xml");
    }
}
