using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Voltium.Common
{
    internal static class ListExtensions
    {
        public static ref T GetRef<T>(this List<T> list, int index) => ref CollectionsMarshal.AsSpan(list)[index];
        public static ref T GetRefUnsafe<T>(this List<T> list, int index) => ref Unsafe.Add(ref MemoryMarshal.GetReference(CollectionsMarshal.AsSpan(list)), index);
        public static Span<T> AsSpan<T>(this List<T> list) => CollectionsMarshal.AsSpan(list);
        public static ReadOnlySpan<T> AsROSpan<T>(this List<T> list) => CollectionsMarshal.AsSpan(list);
    }
}
