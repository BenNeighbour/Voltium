using System;
using System.Diagnostics;
using TerraFX.Interop;
using Voltium.Common;
using Voltium.Core.Memory;

namespace Voltium.Core.Memory
{

    internal unsafe readonly struct ActionOrFnPtr
    {
        public readonly Action? Action;
        public readonly delegate*<void> FnPtr;

        public ActionOrFnPtr(Action? action)
        {
            Action = action;
            FnPtr = null;
        }

        public ActionOrFnPtr(delegate*<void> fnPtr)
        {
            Action = null;
            FnPtr = fnPtr;
        }

        public void Invoke()
        {
            if (Action is not null)
            {
                Action();
            }
            else
            {
                FnPtr();
            }
        }
    }

    internal unsafe readonly struct ActionOrFnPtr<T>
    {
        public readonly Action<T>? Action;
        public readonly delegate*<T, void> FnPtr;

        public ActionOrFnPtr(Action<T>? action)
        {
            Action = action;
            FnPtr = null;
        }

        public ActionOrFnPtr(delegate*<T, void> fnPtr)
        {
            Action = null;
            FnPtr = fnPtr;
        }

        public void Invoke(T t)
        {
            if (Action is not null)
            {
                Action(t);
            }
            else
            {
                FnPtr(t);
            }
        }
    }


    /// <summary>
    /// Represents a single-dimension untyped buffer of GPU data
    /// </summary>
    public unsafe struct Buffer : IInternalD3D12Object, IDisposable
    {
        ID3D12Object* IInternalD3D12Object.GetPointer() => _resource.GetPointer();

        private GpuResource _resource;
        private void* _cpuAddress;
        private ulong _gpuAddress;

        /// <summary>
        /// The size, in bytes, of the buffer
        /// </summary>
        public readonly uint Length;

        internal Buffer(ulong length, GpuResource resource)
        {
            _resource = resource;

            Length = (uint)length;
            _cpuAddress = null;
            _gpuAddress = resource.GpuAddress;
        }

        private void ThrowIfDead()
        {
            if (_resource is null)
            {
                ThrowHelper.ThrowObjectDisposedException("buffer");
            }
        }

        /// <summary>
        /// The buffer data. This may be empty if the data is not CPU writable
        /// </summary>
        public Span<byte> Data
        {
            get
            {
                if (_cpuAddress == null)
                {
                    _cpuAddress = _resource is null ? null : _resource.Map(0);
                }

                return new Span<byte>(_cpuAddress, (int)Length);
            }
        }

        /// <summary>
        /// Maps the resource
        /// </summary>
        public void Map()
        {
            if (_cpuAddress != null)
            {
                return;
            }
            _cpuAddress = _resource.Map(0);
        }

        /// <summary>
        /// Unmaps the resource
        /// </summary>
        public void Unmap()
        {
            if (_cpuAddress == null)
            {
                return;
            }
            _resource.Unmap(0);
            _cpuAddress = null;
        }

        /// <summary>
        /// Writes the <typeparamref name="T"/> to the buffer
        /// </summary>
        /// <typeparam name="T">The type to write</typeparam>
        public void WriteData<T>(ref T data, uint offset, bool leaveMapped = false) where T : unmanaged
        {
            Map();

            ((T*)_cpuAddress)[offset] = data;

            if (!leaveMapped)
            {
                Unmap();
            }
        }

        /// <summary>
        /// Writes the <typeparamref name="T"/> to the buffer
        /// </summary>
        /// <typeparam name="T">The type to write</typeparam>
        public void WriteConstantBufferData<T>(ref T data, uint offset, bool leaveMapped = false) where T : unmanaged
        {
            Map();

            var alignedSize = (sizeof(T) + 255) & ~255;

            *(T*)((byte*)_cpuAddress + (alignedSize * offset)) = data;

            if (!leaveMapped)
            {
                Unmap();
            }
        }

        /// <summary>
        /// Writes the <typeparamref name="T"/> to the buffer
        /// </summary>
        /// <typeparam name="T">The type to write</typeparam>
        public void WriteDataByteOffset<T>(ref T data, uint offset, bool leaveMapped = false) where T : unmanaged
        {
            Map();

            *(T*)((byte*)_cpuAddress + offset) = data;

            if (!leaveMapped)
            {
                Unmap();
            }
        }

        /// <summary>
        /// Writes the <typeparamref name="T"/> to the buffer
        /// </summary>
        /// <typeparam name="T">The type to write</typeparam>
        public void WriteData<T>(ReadOnlySpan<T> data, bool leaveMapped = false) where T : unmanaged
        {
            Map();

            data.CopyTo(new Span<T>(_cpuAddress, (int)Length));

            if (!leaveMapped)
            {
                Unmap();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _resource?.Dispose();
            _resource = null!;
            _cpuAddress = null;
            _gpuAddress = 0;
        }

        // required for binding directly without desc heap
        internal ulong GpuAddress
        {
            get
            {
                ThrowIfDead();
                return _gpuAddress;
            }
        }

        internal void* CpuAddress
        {
            get
            {
                ThrowIfDead();
                return _cpuAddress;
            }
        }

        internal GpuResource Resource => _resource;

        internal ID3D12Resource* GetResourcePointer() => _resource.GetResourcePointer();

    }

    //public unsafe static class BufferExtensions
    //{
    //public static void CopyTo<T>(this Span<T> src, in Buffer<T> dest) where T : unmanaged
    //    => CopyTo((ReadOnlySpan<T>)src, dest);

    //public static void CopyTo<T>(this ReadOnlySpan<T> src, in Buffer<T> dest) where T : unmanaged
    //{
    //    if (dest.Kind != BufferKind.Constant && dest.GetElementSize() == sizeof(T))
    //    {
    //        src.CopyTo(MemoryMarshal.Cast<byte, T>(dest.GetUnderlyingDataSpan()));
    //    }
    //    else
    //    {
    //        for (var i = 0; i < src.Length; i++)
    //        {
    //            dest[(uint)i] = src[i];
    //        }
    //    }
    //}

    //public static void CopyTo<T>(this Buffer<T> src, Span<T> dest) where T : unmanaged
    //{
    //    if (src.Kind != BufferKind.Constant && src.GetElementSize() == sizeof(T))
    //    {
    //        src.GetUnderlyingDataSpan().CopyTo(MemoryMarshal.Cast<T, byte>(dest));
    //    }
    //    else
    //    {
    //        for (var i = 0; i < src.Count; i++)
    //        {
    //            src[(uint)i] = dest[i];
    //        }
    //    }
    //}
    //}
}
