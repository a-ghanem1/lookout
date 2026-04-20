// Polyfill required for C# 9 record types when targeting netstandard2.0.
#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
