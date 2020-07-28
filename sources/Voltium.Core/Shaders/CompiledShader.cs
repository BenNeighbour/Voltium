using System;
using System.Buffers;
using Voltium.Common;

namespace Voltium.Core.Devices
{
    /// <summary>
    /// Represents a compiled shader
    /// </summary>
    [GenerateEquality]
    public readonly unsafe partial struct CompiledShader : IDisposable
    {
        /// <summary>
        /// Represents a <see cref="CompiledShader"/> containing no data
        /// </summary>
        public static CompiledShader Empty => default;

        /// <summary>
        /// The pointer to the beginning of the shader data
        /// </summary>
        public readonly ReadOnlyMemory<byte> ShaderData => _shaderData?.Memory ?? default;

        private readonly IMemoryOwner<byte> _shaderData;

        /// <summary>
        /// The size of the shader data, in bytes
        /// </summary>
        public int Length => ShaderData.Length;

        /// <summary>
        /// The type of the shader
        /// </summary>
        public readonly ShaderType Type;

        /// <summary>
        /// Creates a new instance of a <see cref="CompiledShader"/>
        /// </summary>
        public CompiledShader(IMemoryOwner<byte> shaderData, ShaderType type)
        {
            _shaderData = shaderData;
            Type = type;
        }

        /// <inheritdoc cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(CompiledShader other) => Type == other.Type && ShaderData.Span.SequenceEqual(other.ShaderData.Span);

        /// <summary>
        /// Returns a <c>readonly ref byte</c> to the start of the shader data
        /// </summary>
        public ref readonly byte GetPinnableReference() => ref ShaderData.Span.GetPinnableReference();

        /// <inheritdoc/>
        public void Dispose() => _shaderData.Dispose();
    }
}
