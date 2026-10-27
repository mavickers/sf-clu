using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LightPath.Sitefinity.CLU
{
    /// <summary>Encrypts a string with AES-GCM under a key derived from this machine's identifier.</summary>
    /// <remarks>
    /// Keeps a value unreadable where a file is displayed or copied; it is not a security boundary, because any process on the machine can read the identifier.
    /// A value encrypted on another machine fails to decrypt with a <see cref="CryptographicException"/>.
    /// </remarks>
    internal static class MachineSecret
    {
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private static readonly byte[] KeyInfo = Encoding.UTF8.GetBytes("sf-clu machine secret v1");

        public static string Decrypt(string cipherText)
        {
            var data = Convert.FromBase64String(cipherText);

            if (data.Length < NonceSize + TagSize) throw new CryptographicException("The value is too short to be an sf-clu secret.");

            var plain = new byte[data.Length - NonceSize - TagSize];

            using var aes = new AesGcm(DeriveKey(), TagSize);

            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize, plain.Length), data.AsSpan(data.Length - TagSize), plain);

            return Encoding.UTF8.GetString(plain);
        }

        private static byte[] DeriveKey() => HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(MachineId()), KeySize, null, KeyInfo);

        public static string Encrypt(string plainText)
        {
            var plain = Encoding.UTF8.GetBytes(plainText);
            var data = new byte[NonceSize + plain.Length + TagSize];
            var nonce = data.AsSpan(0, NonceSize);

            RandomNumberGenerator.Fill(nonce);

            using var aes = new AesGcm(DeriveKey(), TagSize);

            aes.Encrypt(nonce, plain, data.AsSpan(NonceSize, plain.Length), data.AsSpan(NonceSize + plain.Length));

            return Convert.ToBase64String(data);
        }

        private static string MachineId()
        {
            if (OperatingSystem.IsWindows()) return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string ?? throw new InvalidOperationException("MachineGuid is missing from HKLM\\SOFTWARE\\Microsoft\\Cryptography");
            if (OperatingSystem.IsLinux()) return File.ReadAllText("/etc/machine-id").Trim();
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("sf-clu secrets need Windows, macOS or Linux");

            using var process = Process.Start(new ProcessStartInfo("/usr/sbin/ioreg", "-rd1 -c IOPlatformExpertDevice") { RedirectStandardOutput = true, UseShellExecute = false })!;
            var output = process.StandardOutput.ReadToEnd();
            var match = Regex.Match(output, "\"IOPlatformUUID\" = \"([^\"]+)\"");

            process.WaitForExit();

            return match.Success ? match.Groups[1].Value : throw new InvalidOperationException("ioreg returned no IOPlatformUUID");
        }
    }
}
