using System.CommandLine;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class GenKeysCommand
    {
            public static readonly Command GenKeysCmd = new("-genkeys", "Generate public/private key set for authenticating API calls");
    }
}
