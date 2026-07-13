// Compiler shim so C# 9 'init' accessors (used by MisakiSharp's MToken) compile
// against Unity's netstandard2.1 profile, which lacks this type. The original
// sample carries the same shim in DownloadConfiguration.cs, which we don't port.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
