using TerraFX.Interop;
using Voltium.Common;
using Voltium.Core.Devices;
using Voltium.Core.Memory;
using static TerraFX.Interop.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.D3D12_DESCRIPTOR_HEAP_TYPE;

namespace Voltium.Core
{
    /// <summary>
    /// A heap of descriptors for resources
    /// </summary>
    public unsafe struct DescriptorHeap
    {
        private ComPtr<ID3D12DescriptorHeap> _heap;

        internal ID3D12DescriptorHeap* GetHeap() => _heap.Get();

        /// <summary>
        /// The type of the descriptor heap
        /// </summary>
        public DescriptorHeapType Type { get; private set; }

        /// <summary>
        /// The number of descriptors in the heap
        /// </summary>
        public uint NumDescriptors { get; private set; }

        private static D3D12_DESCRIPTOR_HEAP_DESC CreateDesc(
            D3D12_DESCRIPTOR_HEAP_TYPE type,
            uint numDescriptors,
            bool shaderVisible
        )
        {
            var desc = new D3D12_DESCRIPTOR_HEAP_DESC
            {
                Flags = shaderVisible ? D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE : D3D12_DESCRIPTOR_HEAP_FLAG_NONE,
                NodeMask = 0, // TODO: MULTI-GPU
                NumDescriptors = numDescriptors,
                Type = type
            };

            return desc;
        }

        /// <summary>
        /// Create a new <see cref="DescriptorHeap"/>
        /// </summary>
        /// <param name="device">The device to use during creation</param>
        /// <param name="type">The <see cref="DescriptorHeapType"/> for this heap</param>
        /// <param name="descriptorCount">The number of descriptors</param>
        public static DescriptorHeap Create(
            ComputeDevice device,
            DescriptorHeapType type,
            uint descriptorCount
        )
        {
            var desc = CreateDesc(
                (D3D12_DESCRIPTOR_HEAP_TYPE)type,
                descriptorCount,
                type is DescriptorHeapType.Sampler or DescriptorHeapType.ConstantBufferShaderResourceOrUnorderedAccessView
            );

            return new DescriptorHeap(device, desc);
        }


        private DescriptorHeap(ComputeDevice device, D3D12_DESCRIPTOR_HEAP_DESC desc)
        {
            ComPtr<ID3D12DescriptorHeap> heap = default;
            Guard.ThrowIfFailed(device.DevicePointer->CreateDescriptorHeap(&desc, heap.Iid, (void**)&heap));

            _heap = heap.Move();
            var cpu = _heap.Get()->GetCPUDescriptorHandleForHeapStart();
            var gpu = _heap.Get()->GetGPUDescriptorHandleForHeapStart();

            _firstHandle = new DescriptorHandle(device.DevicePointer->GetDescriptorHandleIncrementSize(desc.Type), cpu, gpu);
            _offset = 0;
            _count = desc.NumDescriptors;

            Type = (DescriptorHeapType)desc.Type;
            NumDescriptors = desc.NumDescriptors;

            DebugHelpers.SetName(_heap.Get(), nameof(ID3D12DescriptorHeap) + " " + desc.Type.ToString());
        }

        /// <summary>
        /// The description of the heap
        /// </summary>
        public D3D12_DESCRIPTOR_HEAP_DESC Desc => _heap.Get()->GetDesc();

        private uint _count;
        private uint _offset;
        private DescriptorHandle _firstHandle;

        /// <summary>
        /// Gets the next handle in the heap
        /// </summary>
        public DescriptorHandle GetNextHandle()
        {
            Guard.True(_offset < _count, "Too many descriptors");
            return _firstHandle + _offset++;
        }

        /// <summary>
        /// Gets the next <paramref name="count"/> handles in the heap
        /// </summary>
        public DescriptorHandle GetNextHandles(int count)
            => GetNextHandles((uint)count);

        /// <summary>
        /// Gets the next <paramref name="count"/> handles in the heap
        /// </summary>
        public DescriptorHandle GetNextHandles(uint count)
        {
            Guard.True(_offset + count <= _count, "Too many descriptors");
            var next = _firstHandle + _offset;
            _offset += count;
            return next;
        }
        /// <summary>
        /// Resets the heap for reuse
        /// </summary>
        public void ResetHeap() => _offset = 0;

        /// <inheritdoc cref="IComType.Dispose"/>
        public void Dispose() => _heap.Dispose();
    }

    /// <summary>
    /// Represents the type of the descriptors in a <see cref="DescriptorHeap"/>
    /// </summary>
    public enum DescriptorHeapType
    {
        /// <summary>
        /// The descriptor represents a constant buffer, shader resource, or unordered access view
        /// </summary>
        ConstantBufferShaderResourceOrUnorderedAccessView = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,

        /// <summary>
        /// The descriptor represents a sampler
        /// </summary>
        Sampler = D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER,

        /// <summary>
        /// The descriptor represents a render target view
        /// </summary>
        RenderTargetView = D3D12_DESCRIPTOR_HEAP_TYPE_RTV,

        /// <summary>
        /// The descriptor represents a depth stencil view
        /// </summary>
        DepthStencilView = D3D12_DESCRIPTOR_HEAP_TYPE_DSV,
    }
}