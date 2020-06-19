using System;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using TerraFX.Interop;
using Voltium.Common;
using Voltium.Core.Memory;

namespace Voltium.Core.GpuResources
{
    /// <summary>
    /// Represents a GPU resource
    /// </summary>
    internal unsafe class GpuResource : CriticalFinalizerObject, IDisposable
    {
        internal GpuResource(
            ComPtr<ID3D12Resource> resource,
            InternalAllocDesc desc,
            GpuAllocator? allocator = null,
            AllocatorHeap heap = default,
            HeapBlock block = default
        )
        {
            _value = resource.Move();
            State = (ResourceState)desc.InitialState;
            ResourceFormat = (DataFormat)desc.Desc.Format;
            Heap = heap;
            Block = block;
            _allocator = allocator;

            if (desc.Desc.Dimension == D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER)
            {
                GpuAddress = UnderlyingResource->GetGPUVirtualAddress();
            }
        }

        private GpuResource() { }

        // this is a hack. TODO make it right
        internal static GpuResource FromBackBuffer(
            ComPtr<ID3D12Resource> resource
        )
        {
            var desc = resource.Get()->GetDesc();
            return new GpuResource
            {
                _value = resource.Move(),
                ResourceFormat = (DataFormat)desc.Format,
                State = (ResourceState)D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON
            };
        }

        /// <summary>
        /// The format of the buffer, if typed, else <see cref="DataFormat.Unknown"/>
        /// </summary>
        public DataFormat ResourceFormat { get; private set; }

        /// <summary>
        /// The underlying value of the resource
        /// </summary>
        public ID3D12Resource* UnderlyingResource => _value.Get();

        /// <summary>
        /// The current state of the resource
        /// </summary>
        public ResourceState State { get; internal set; }

        private ComPtr<ID3D12Resource> _value;

        /// <summary>
        /// The GPU address of the underlying resource
        /// </summary>
        public ulong GpuAddress { get; private set; }

        /// <summary>
        /// The CPU address of the underlying resource. This may be null if the resource
        /// is unmapped or not CPU accessible
        /// </summary>
        public unsafe void* CpuAddress { get; private set; }

        private GpuAllocator? _allocator;
        public AllocatorHeap Heap;
        public HeapBlock Block;

        /// <summary>
        /// If the resource is not currently mapped, maps the resource
        /// </summary>
        /// <param name="subresource">The subresource index to map</param>
        public unsafe void* Map(uint subresource)
        {
            // Apparently the range for map and unmap are for debugging purposes and yield no perf benefit. Maybe we could still support em
            void* pData;
            Guard.ThrowIfFailed(UnderlyingResource->Map(subresource, null, &pData));
            return pData;
        }

        /// <summary>
        /// If the resource is currently mapped, unmaps the resource
        /// </summary>
        /// <param name="subresource">The subresource index to unmap</param>
        public unsafe void Unmap(uint subresource)
        {
            // Apparently the range for map and unmap are for debugging purposes and yield no perf benefit. Maybe we could still support em
            UnderlyingResource->Unmap(subresource, null);
        }

        /// <inheritdoc cref="IDisposable"/>
        public void Dispose()
        {
            if (_allocator is object)
            {
                _allocator.Return(this);
            }
            else
            {
                _value.Dispose();
            }
            GC.SuppressFinalize(this);
        }

#if TRACE_DISPOSABLES || DEBUG
        /// <summary>
        /// go fuck yourself roslyn why the fucking fuckeroni do finalizers need xml comments fucking fuck off fucking twatty compiler
        /// </summary>
        ~GpuResource()
        {
            Guard.MarkDisposableFinalizerEntered();
            ThrowHelper.NeverReached();
        }
#endif
    }
}
