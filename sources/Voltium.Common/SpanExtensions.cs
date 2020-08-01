using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Voltium.Extensions
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public static class SpanExtensions
    {
        public static int ByteLength<T>(this Span<T> span) => Unsafe.SizeOf<T>() * span.Length;
        public static int ByteLength<T>(this Memory<T> memory) => Unsafe.SizeOf<T>() * memory.Length;

        public static int ByteLength<T>(this ReadOnlySpan<T> span) => Unsafe.SizeOf<T>() * span.Length;
        public static int ByteLength<T>(this ReadOnlyMemory<T> memory) => Unsafe.SizeOf<T>() * memory.Length;
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
