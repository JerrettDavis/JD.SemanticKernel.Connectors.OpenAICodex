// Polyfills for C# 9+ features (record types, init-only setters) when targeting netstandard2.0.
// The compiler emits references to these types; they are not present in the netstandard2.0 BCL.
#if NETSTANDARD2_0
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
#endif
