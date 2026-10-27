using System.CommandLine;
using System.Security.Cryptography;
using static System.Convert;
using static System.Text.Encoding;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class GenKeysCommand
    {
        public static void Handler(ParseResult parseResult)
        {
            var rsa = new RSACryptoServiceProvider();
            var privateKey = rsa.ToXmlString(true);
            var publicKey = rsa.ToXmlString(false);

            var privateKeyEncoded = ToBase64String(UTF8.GetBytes(privateKey));
            var publicKeyEncoded = ToBase64String(UTF8.GetBytes(publicKey));

            Console.WriteLine("-----------");
            Console.WriteLine("PRIVATE KEY");
            Console.WriteLine("-----------");
            Console.WriteLine(privateKey);
            Console.WriteLine();
            Console.WriteLine(privateKeyEncoded);
            Console.WriteLine();
            Console.WriteLine("----------");
            Console.WriteLine("PUBLIC KEY");
            Console.WriteLine("----------");
            Console.WriteLine(publicKey);
            Console.WriteLine();
            Console.WriteLine(publicKeyEncoded);
        }
    }
}
