using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Interop;
using Voltium.Common;
using Voltium.Common.Debugging;
using Voltium.Core.GpuResources;
using Voltium.Core.Managers;
using Voltium.Core.Memory.GpuResources;
using Voltium.Core.Pipeline;
using Voltium.TextureLoading;
using static TerraFX.Interop.D3D_PRIMITIVE_TOPOLOGY;
using Buffer = Voltium.Core.Memory.GpuResources.Buffer;

namespace Voltium.Core
{
    internal unsafe struct GpuContext : IDisposable
    {
        public GraphicsDevice Device;
        public ComPtr<ID3D12GraphicsCommandList> _list;
        public ComPtr<ID3D12CommandAllocator> _allocator;

        public ID3D12GraphicsCommandList* List => _list.Get();
        public ID3D12CommandAllocator* Allocator => _allocator.Get();

        private ResourceBarrier8 _barrierBuffer;
        private uint _currentBarrierCount;
        private const uint MaxNumBarriers = 8;

        public GpuContext(GraphicsDevice device, ComPtr<ID3D12GraphicsCommandList> list, ComPtr<ID3D12CommandAllocator> allocator)
        {
            Device = device;
            _list = list.Move();
            _allocator = allocator.Move();
            // We can't read past this many buffers as we skip init'ing them
            _currentBarrierCount = 0;

            // Don't bother zero'ing expensive buffer
            Unsafe.SkipInit(out _barrierBuffer);
        }

        public void AddBarrier(in D3D12_RESOURCE_BARRIER barrier)
        {
            if (_currentBarrierCount == MaxNumBarriers)
            {
                FlushBarriers();
            }

            _barrierBuffer[_currentBarrierCount++] = barrier;
        }

        public void FlushBarriers()
        {
            if (_currentBarrierCount == 0)
            {
                return;
            }

            fixed (D3D12_RESOURCE_BARRIER* pBarriers = _barrierBuffer)
            {
                List->ResourceBarrier(_currentBarrierCount, pBarriers);
            }

            _currentBarrierCount = 0;
        }

        private struct ResourceBarrier8
        {
            public D3D12_RESOURCE_BARRIER E0;
            public D3D12_RESOURCE_BARRIER E1;
            public D3D12_RESOURCE_BARRIER E2;
            public D3D12_RESOURCE_BARRIER E3;
            public D3D12_RESOURCE_BARRIER E4;
            public D3D12_RESOURCE_BARRIER E5;
            public D3D12_RESOURCE_BARRIER E6;
            public D3D12_RESOURCE_BARRIER E7;

            public ref D3D12_RESOURCE_BARRIER this[uint index]
                => ref Unsafe.Add(ref GetPinnableReference(), (int)index);

            public ref D3D12_RESOURCE_BARRIER GetPinnableReference() => ref MemoryMarshal.GetReference(MemoryMarshal.CreateSpan(ref E0, 0));
        }

        public void Dispose()
        {
            FlushBarriers();
            Device.End(ref this);
        }
    }

    /// <summary>
    /// Represents a context on which GPU commands can be recorded
    /// </summary>
    public unsafe partial struct CopyContext : IDisposable
    {
        private GpuContext _context;

        internal CopyContext(GpuContext context)
        {
            _context = context;
        }

        //AtomicCopyBufferUINT
        //AtomicCopyBufferUINT64
        //CopyBufferRegion
        //CopyResource
        //CopyTextureRegion
        //CopyTiles
        //EndQuery
        //ResolveQueryData
        //ResourceBarrier
        //SetProtectedResourceSession
        //WriteBufferImmediate

        /// <summary>
        /// Copy an entire resource
        /// </summary>
        /// <param name="source">The resource to copy from</param>
        /// <param name="dest">The resource to copy to</param>
        public void CopyResource(Buffer source, Buffer dest)
        {
            if (!source.Resource.State.HasFlag(ResourceState.CopySource))
            {
                ResourceTransition(source, ResourceState.CopySource, 0xFFFFFFFF);
            }
            if (!dest.Resource.State.HasFlag(ResourceState.CopyDestination))
            {
                ResourceTransition(dest, ResourceState.CopyDestination, 0xFFFFFFFF);
            }

            _context.FlushBarriers();
            _context.List->CopyResource(dest.Resource.UnderlyingResource, source.Resource.UnderlyingResource);
        }

        /// <summary>
        /// Copy an entire resource
        /// </summary>
        /// <param name="source">The resource to copy from</param>
        /// <param name="dest">The resource to copy to</param>
        public void CopyResource(Texture source, Texture dest)
        {
            ResourceTransition(source, ResourceState.CopySource, 0xFFFFFFFF);
            ResourceTransition(dest, ResourceState.CopyDestination, 0xFFFFFFFF);

            _context.FlushBarriers();
            _context.List->CopyResource(dest.Resource.UnderlyingResource, source.Resource.UnderlyingResource);
        }

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, T[] buffer, Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, Span<T> buffer, Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, ReadOnlySpan<T> buffer, Buffer destination) where T : unmanaged
        {
            var upload = allocator.AllocateBuffer(buffer.Length * sizeof(T), MemoryAccess.CpuUpload, ResourceState.GenericRead);
            upload.WriteData(buffer);

            CopyResource(upload, destination);
        }

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, T[] buffer, out Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, out destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, Span<T> buffer, out Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, out destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, ReadOnlySpan<T> buffer, out Buffer destination) where T : unmanaged
        {
            var upload = allocator.AllocateBuffer(buffer.Length * sizeof(T), MemoryAccess.CpuUpload, ResourceState.GenericRead);
            upload.WriteData(buffer);

            destination = allocator.AllocateBuffer(buffer.Length * sizeof(T), MemoryAccess.GpuOnly, ResourceState.CopyDestination);
            CopyResource(upload, destination);
        }

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="texture"></param>
        /// <param name="subresources"></param>
        /// <param name="tex"></param>
        /// <param name="destination"></param>
        public void UploadTexture(GpuAllocator allocator, ReadOnlySpan<byte> texture, ReadOnlySpan<SubresourceData> subresources, TextureDesc tex, out Texture destination)
        {
            destination = allocator.AllocateTexture(tex, ResourceState.CopyDestination);
            UploadTexture(allocator, texture, subresources, destination);
        }

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="texture"></param>
        /// <param name="subresources"></param>
        /// <param name="destination"></param>
        public void UploadTexture(GpuAllocator allocator, ReadOnlySpan<byte> texture, ReadOnlySpan<SubresourceData> subresources, Texture destination)
        {
            var upload = allocator.AllocateBuffer(
                (long)Windows.GetRequiredIntermediateSize(destination.Resource.UnderlyingResource, 0, (uint)subresources.Length),
                MemoryAccess.CpuUpload,
                ResourceState.GenericRead
            );

            fixed (byte* pTextureData = texture)
            fixed (SubresourceData* pSubresources = subresources)
            {
                // D3D12_SUBRESOURCE_DATA and SubresourceData are blittable, just SubresourceData contains an offset past the pointer rather than the pointer
                // Fix that here
                for (var i = 0; i < subresources.Length; i++)
                {
                    ((D3D12_SUBRESOURCE_DATA*)&pSubresources[i])->pData = pTextureData + pSubresources[i].DataOffset;
                }

                _context.FlushBarriers();
                _ = Windows.UpdateSubresources(
                    _context.List,
                    destination.Resource.UnderlyingResource,
                    upload.Resource.UnderlyingResource,
                    0,
                    0,
                    (uint)subresources.Length,
                    (D3D12_SUBRESOURCE_DATA*)pSubresources
                );
            }
        }



        /// <summary>
        /// Mark a resource barrier on the command list
        /// </summary>
        /// <param name="resource">The resource to transition</param>
        /// <param name="transition">The transition</param>
        /// <param name="subresource">The subresource to transition</param>
        public void ResourceTransition(Buffer resource, ResourceState transition, uint subresource = 0xFFFFFFFF)
            => ResourceTransition(resource.Resource, transition, subresource);

        /// <summary>
        /// Mark a resource barrier on the command list
        /// </summary>
        /// <param name="resource">The resource to transition</param>
        /// <param name="transition">The transition</param>
        /// <param name="subresource">The subresource to transition</param>
        public void ResourceTransition(Texture resource, ResourceState transition, uint subresource = 0xFFFFFFFF)
            => ResourceTransition(resource.Resource, transition, subresource);

        private void ResourceTransition(GpuResource resource, ResourceState transition, uint subresource = 0xFFFFFFFF)
        {
            // don't do unnecessary work
            // ResourceState.Common is 0 and must be transitioned to. Same applies for present (also 0)
            if (transition != ResourceState.Common && (resource.State & transition) == transition)
            {
                return;
            }

            Unsafe.SkipInit(out D3D12_RESOURCE_BARRIER barrier);
            {
                barrier.Type = D3D12_RESOURCE_BARRIER_TYPE.D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
                barrier.Flags = D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE;
                barrier.Anonymous.Transition = new D3D12_RESOURCE_TRANSITION_BARRIER
                {
                    pResource = resource.UnderlyingResource,
                    StateBefore = (D3D12_RESOURCE_STATES)resource.State,
                    StateAfter = (D3D12_RESOURCE_STATES)transition,
                    Subresource = subresource
                };
            };

            resource.State = transition;
            _context.AddBarrier(barrier);
        }

        /// <inheritdoc/>
        public void Dispose() => _context.Dispose();
    }

    /// <summary>
    /// Represents a context on which GPU commands can be recorded
    /// </summary>
    public unsafe partial struct ComputeContext : IDisposable
    {
        private GpuContext _context;

        internal ComputeContext(GpuContext context)
        {
            _context = context;
        }

        /// <inheritdoc/>
        public void Dispose() => _context.Dispose();
        //AtomicCopyBufferUINT
        //AtomicCopyBufferUINT64
        //CopyBufferRegion
        //CopyResource
        //CopyTextureRegion
        //CopyTiles
        //EndQuery
        //ResolveQueryData
        //ResourceBarrier
        //SetProtectedResourceSession
        //WriteBufferImmediate

        //BuildRaytracingAccelerationStructure
        //ClearState
        //ClearUnorderedAccessViewFloat
        //ClearUnorderedAccessViewUint
        //CopyRaytracingAccelerationStructure
        //DiscardResource
        //Dispatch
        //DispatchRays
        //EmitRaytracingAccelerationStructurePostbuildInfo
        //ExecuteIndirect
        //ExecuteMetaCommand
        //InitializeMetaCommand
        //ResolveQueryData
        //ResourceBarrier
        //SetComputeRoot32BitConstant
        //SetComputeRoot32BitConstants
        //SetComputeRootConstantBufferView
        //SetComputeRootDescriptorTable
        //SetComputeRootShaderResourceView
        //SetComputeRootSignature
        //SetComputeRootUnorderedAccessView
        //SetDescriptorHeaps
        //SetPipelineState
        //SetPipelineState1
        //SetPredication

        //BeginEvent
        //BeginQuery
        //ClearState
        //ClearUnorderedAccessViewFloat
        //ClearUnorderedAccessViewUint
        //Close
        //CopyBufferRegion
        //CopyResource
        //CopyTextureRegion
        //Dispatch
        //EndEvent
        //EndQuery
        //Reset
        //ResolveQueryData
        //ResourceBarrier
        //SetComputeRoot32BitConstant
        //SetComputeRoot32BitConstants
        //SetComputeRootConstantBufferView
        //SetComputeRootDescriptorTable
        //SetComputeRootShaderResourceView
        //SetComputeRootSignature
        //SetComputeRootUnorderedAccessView
        //SetDescriptorHeaps
        //SetMarker
        //SetPipelineState
        //SetPredication

        /// <summary>
        /// Sets a directly-bound constant buffer view descriptor to the compute pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="cbuffer">The <see cref="Buffer"/> containing the buffer to add</param>
        public void SetConstantBuffer(uint paramIndex, Buffer cbuffer)
            => SetConstantBuffer<byte>(paramIndex, cbuffer, 0);

        /// <summary>
        /// Sets a directly-bound constant buffer view descriptor to the compute pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="cbuffer">The <see cref="Buffer"/> containing the buffer to add</param>
        /// <param name="offset">The offset in bytes to start the view at</param>
        public void SetConstantBuffer<T>(uint paramIndex, Buffer cbuffer, uint offset = 0) where T : unmanaged
        {
            var alignedSize = (sizeof(T) + 255) & ~255;

            _context.List->SetComputeRootConstantBufferView(paramIndex, cbuffer.GpuAddress + (ulong)(alignedSize * offset));
        }

        /// <summary>
        /// Sets a directly-bound constant buffer view descriptor to the compute pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="cbuffer">The <see cref="Buffer"/> containing the buffer to add</param>
        /// <param name="offset">The offset in bytes to start the view at</param>
        public void SetConstantBufferByteOffset(uint paramIndex, Buffer cbuffer, uint offset = 0)
        {
            _context.List->SetComputeRootConstantBufferView(paramIndex, cbuffer.GpuAddress + offset);
        }

        /// <summary>
        /// Sets a descriptor table to the compute pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="handle">The <see cref="DescriptorHandle"/> containing the first view</param>
        public void SetRootDescriptorTable(uint paramIndex, DescriptorHandle handle)
        {
            _context.List->SetComputeRootDescriptorTable(paramIndex, handle.GpuHandle);
        }

        /// <summary>
        /// Set the compute root signature for the command list
        /// </summary>
        /// <param name="signature">The signature to set to</param>
        public void SetRootSignature(RootSignature signature)
        {
            _context.List->SetComputeRootSignature(signature.Value);
        }

        #region CopyContext Methods

        /// <summary>
        /// Copy an entire resource
        /// </summary>
        /// <param name="source">The resource to copy from</param>
        /// <param name="dest">The resource to copy to</param>
        public void CopyResource(Buffer source, Buffer dest)
            => this.AsCopyContext().CopyResource(source, dest);

        /// <summary>
        /// Copy an entire resource
        /// </summary>
        /// <param name="source">The resource to copy from</param>
        /// <param name="dest">The resource to copy to</param>
        public void CopyResource(Texture source, Texture dest)
            => this.AsCopyContext().CopyResource(source, dest);

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, T[] buffer, Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, Span<T> buffer, Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, ReadOnlySpan<T> buffer, Buffer destination) where T : unmanaged
            => this.AsCopyContext().UploadBuffer<T>(allocator, buffer, destination);

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, T[] buffer, out Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, out destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, Span<T> buffer, out Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, out destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, ReadOnlySpan<T> buffer, out Buffer destination) where T : unmanaged
            => this.AsCopyContext().UploadBuffer<T>(allocator, buffer, out destination);

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="texture"></param>
        /// <param name="subresources"></param>
        /// <param name="tex"></param>
        /// <param name="destination"></param>
        public void UploadTexture(GpuAllocator allocator, ReadOnlySpan<byte> texture, ReadOnlySpan<SubresourceData> subresources, TextureDesc tex, out Texture destination)
            => this.AsCopyContext().UploadTexture(allocator, texture, subresources, tex, out destination);

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="texture"></param>
        /// <param name="subresources"></param>
        /// <param name="destination"></param>
        public void UploadTexture(GpuAllocator allocator, ReadOnlySpan<byte> texture, ReadOnlySpan<SubresourceData> subresources, Texture destination)
        => this.AsCopyContext().UploadTexture(allocator, texture, subresources, destination);


        /// <summary>
        /// Mark a resource barrier on the command list
        /// </summary>
        /// <param name="resource">The resource to transition</param>
        /// <param name="transition">The transition</param>
        /// <param name="subresource">The subresource to transition</param>
        public void ResourceTransition(Buffer resource, ResourceState transition, uint subresource = 0xFFFFFFFF)
            => this.AsCopyContext().ResourceTransition(resource, transition, subresource);


        /// <summary>
        /// Mark a resource barrier on the command list
        /// </summary>
        /// <param name="resource">The resource to transition</param>
        /// <param name="transition">The transition</param>
        /// <param name="subresource">The subresource to transition</param>
        public void ResourceTransition(Texture resource, ResourceState transition, uint subresource = 0xFFFFFFFF)
            => this.AsCopyContext().ResourceTransition(resource, transition, subresource);

        #endregion
    }

    /// <summary>
    /// Represents a context on which GPU commands can be recorded
    /// </summary>
    public unsafe partial struct GraphicsContext : IDisposable
    {
        private GpuContext _context;

        internal GraphicsContext(GpuContext context)
        {
            _context = context;
        }

        private static bool AreCopyable(GpuResource source, GpuResource destination)
        {
            D3D12_RESOURCE_DESC srcDesc = source.UnderlyingResource->GetDesc();
            D3D12_RESOURCE_DESC destDesc = destination.UnderlyingResource->GetDesc();

            return srcDesc.Width == destDesc.Width
                   && srcDesc.Height == destDesc.Height
                   && srcDesc.DepthOrArraySize == destDesc.DepthOrArraySize
                   && srcDesc.Dimension == destDesc.Dimension;
        }

        /// <summary>
        /// Sets the current pipeline state
        /// </summary>
        /// <param name="pso">The <see cref="PipelineStateObject"/> to set</param>
        public void SetPipelineState(PipelineStateObject pso)
        {
            _context.List->SetPipelineState(pso.GetPso());
        }

        /// <summary>
        /// Sets the viewport and scissor rectangle
        /// </summary>
        /// <param name="width">The width, in pixels</param>
        /// <param name="height">The height, in pixels</param>
        public void SetViewportAndScissor(uint width, uint height)
        {
            SetViewports(new Viewport(0, 0, width, height, 0, 1));
            SetScissorRectangles(new Rectangle(0, 0, (int)width, (int)height));
        }

        /// <summary>
        /// Sets the viewport and scissor rectangle
        /// </summary>
        /// <param name="size">The size, in pixels</param>
        public void SetViewportAndScissor(Size size)
            => SetViewportAndScissor((uint)size.Width, (uint)size.Height);

        /// <summary>
        /// Sets the blend factor for the pipeline <see cref="BlendDesc"/>
        /// </summary>
        /// <param name="value">The value of the blend factor</param>
        public void SetBlendFactor(RgbaColor value)
        {
            _context.List->OMSetBlendFactor(&value.R);
        }

        /// <summary>
        /// Sets the stencil ref for the pipeline <see cref="BlendDesc"/>
        /// </summary>
        /// <param name="value">The value of the stencil ref</param>
        public void SetStencilRef(uint value)
        {
            _context.List->OMSetStencilRef(value);
        }

        /// <summary>
        /// Sets a directly-bound constant buffer view descriptor to the graphics pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="cbuffer">The <see cref="Buffer"/> containing the buffer to add</param>
        public void SetBuffer(uint paramIndex, Buffer cbuffer)
            => SetBuffer<byte>(paramIndex, cbuffer, 0);

        /// <summary>
        /// Sets a directly-bound constant buffer view descriptor to the graphics pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="cbuffer">The <see cref="Buffer"/> containing the buffer to add</param>
        /// <param name="offset">The offset in elements of <typeparamref name="T"/> to start the view at</param>
        public void SetBuffer<T>(uint paramIndex, Buffer cbuffer, uint offset = 0) where T : unmanaged
        {
            _context.List->SetGraphicsRootShaderResourceView(paramIndex, cbuffer.GpuAddress + (ulong)(sizeof(T) * offset));
        }

        /// <summary>
        /// Sets a directly-bound constant buffer view descriptor to the graphics pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="cbuffer">The <see cref="Buffer"/> containing the buffer to add</param>
        /// <param name="offset">The offset in bytes to start the view at</param>
        public void SetBufferByteOffset(uint paramIndex, Buffer cbuffer, uint offset = 0)
        {
            _context.List->SetGraphicsRootShaderResourceView(paramIndex, cbuffer.GpuAddress + offset);
        }

        /// <summary>
        /// Sets a directly-bound constant buffer view descriptor to the graphics pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="cbuffer">The <see cref="Buffer"/> containing the buffer to add</param>
        public void SetConstantBuffer(uint paramIndex, Buffer cbuffer)
            => SetConstantBuffer<byte>(paramIndex, cbuffer, 0);

        /// <summary>
        /// Sets a directly-bound constant buffer view descriptor to the graphics pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="cbuffer">The <see cref="Buffer"/> containing the buffer to add</param>
        /// <param name="offset">The offset in elements of <typeparamref name="T"/> to start the view at</param>
        public void SetConstantBuffer<T>(uint paramIndex, Buffer cbuffer, uint offset = 0) where T : unmanaged
        {
            var alignedSize = (sizeof(T) + 255) & ~255;

            _context.List->SetGraphicsRootConstantBufferView(paramIndex, cbuffer.GpuAddress + (ulong)(alignedSize * offset));
        }

        /// <summary>
        /// Sets a directly-bound constant buffer view descriptor to the graphics pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="cbuffer">The <see cref="Buffer"/> containing the buffer to add</param>
        /// <param name="offset">The offset in bytes to start the view at</param>
        public void SetConstantBufferByteOffset(uint paramIndex, Buffer cbuffer, uint offset = 0)
        {
            _context.List->SetGraphicsRootConstantBufferView(paramIndex, cbuffer.GpuAddress + offset);
        }

        /// <summary>
        /// Sets a descriptor table to the graphics pipeline
        /// </summary>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which this view represents</param>
        /// <param name="handle">The <see cref="DescriptorHandle"/> containing the first view</param>
        public void SetRootDescriptorTable(uint paramIndex, DescriptorHandle handle)
        {
            _context.List->SetGraphicsRootDescriptorTable(paramIndex, handle.GpuHandle);
        }

        /// <summary>
        /// Sets a group of 32 bit values to the graphics pipeline
        /// </summary>
        /// <typeparam name="T">The type of the elements used. This must have a size that is a multiple of 4</typeparam>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which these constants represents</param>
        /// <param name="value">The 32 bit values to set</param>
        /// <param name="offset">The offset, in 32 bit offsets, to bind this at</param>
        public void SetRoot32BitConstants<T>(uint paramIndex, T value, uint offset = 0) where T : unmanaged
        {
            if (sizeof(T) % 4 != 0)
            {
                ThrowHelper.ThrowArgumentException(
                    $"Type '{typeof(T).Name}' has size '{sizeof(T)}' but {nameof(SetRoot32BitConstants)} requires param '{nameof(value)} '" +
                    "to have size divisble by 4"
                );
            }

            _context.List->SetGraphicsRoot32BitConstants(paramIndex, (uint)sizeof(T) / 4, &value, offset);
        }


        /// <summary>
        /// Sets a 32 bit value to the graphics pipeline
        /// </summary>
        /// <typeparam name="T">The type of the element used. This must have a size that is 4</typeparam>
        /// <param name="paramIndex">The index in the <see cref="RootSignature"/> which these constants represents</param>
        /// <param name="value">The 32 bit value to set</param>
        /// <param name="offset">The offset, in 32 bit offsets, to bind this at</param>
        public void SetRoot32BitConstant<T>(uint paramIndex, T value, uint offset = 0) where T : unmanaged
        {
            if (sizeof(T) != 4)
            {
                ThrowHelper.ThrowArgumentException(
                    $"Type '{typeof(T).Name}' has size '{sizeof(T)}' but {nameof(SetRoot32BitConstant)} requires param '{nameof(value)} '" +
                    "to have size 4"
                );
            }

            _context.List->SetGraphicsRoot32BitConstant(paramIndex, Unsafe.As<T, uint>(ref value), offset);
        }

        /// <summary>
        /// Set the graphics root signature for the command list
        /// </summary>
        /// <param name="signature">The signature to set to</param>
        public void SetRootSignature(RootSignature signature)
        {
            _context.List->SetGraphicsRootSignature(signature.Value);
        }

        /// <summary>
        /// Sets a range of non-continuous render targets
        /// </summary>
        /// <param name="renderTargets">A span of <see cref="DescriptorHandle"/>s representing each render target</param>
        /// <param name="depthStencilHandle">The handle to the depth stencil descriptor</param>
        public void SetRenderTargets(Span<DescriptorHandle> renderTargets, DescriptorHandle? depthStencilHandle = null)
        {
            Debug.Assert(StackSentinel.SafeToStackalloc<D3D12_CPU_DESCRIPTOR_HANDLE>(renderTargets.Length));

            D3D12_CPU_DESCRIPTOR_HANDLE* pRenderTargets = stackalloc D3D12_CPU_DESCRIPTOR_HANDLE[renderTargets.Length];

            for (var i = 0; i < renderTargets.Length; i++)
            {
                pRenderTargets[i] = renderTargets[i].CpuHandle;
            }

            var depthStencil = depthStencilHandle.GetValueOrDefault();
            _context.FlushBarriers();
            _context.List->OMSetRenderTargets(
                (uint)renderTargets.Length,
                pRenderTargets,
                Windows.FALSE,
                depthStencilHandle is null ? null : &depthStencil.CpuHandle
            );
        }

        /// <summary>
        /// Sets a range of continuous render targets
        /// </summary>
        /// <param name="renderTargetHandle">The handle to the start of the continuous array of render targets</param>
        /// <param name="renderTargetCount">The number of render targets pointed to be <paramref name="renderTargetHandle"/></param>
        /// <param name="depthStencilHandle">The handle to the depth stencil descriptor</param>
        public void SetRenderTargets(in DescriptorHandle? renderTargetHandle = null, uint renderTargetCount = 1, in DescriptorHandle? depthStencilHandle = null)
        {
            var rtv = renderTargetHandle.GetValueOrDefault().CpuHandle;
            var dsv = depthStencilHandle.GetValueOrDefault().CpuHandle;

            var depthStencil = depthStencilHandle.GetValueOrDefault();
            _context.FlushBarriers();
            _context.List->OMSetRenderTargets(
                renderTargetHandle is null ? 0 : renderTargetCount,
                renderTargetHandle is null ? null : &rtv,
                Windows.TRUE,
                depthStencilHandle is null ? null : &dsv
            );
        }

        /// <summary>
        /// Sets the primitive toplogy for geometry
        /// </summary>
        /// <param name="topology">The <see cref="D3D_PRIMITIVE_TOPOLOGY"/> to use</param>
        public void SetTopology(Topology topology)
        {
            _context.List->IASetPrimitiveTopology((D3D_PRIMITIVE_TOPOLOGY)topology);
        }

        /// <summary>
        /// Sets the number of points in a control point patch
        /// </summary>
        /// <param name="count">The number of points per patch></param>
        public void SetControlPatchPointCount(byte count)
        {
            Guard.InRangeInclusive(1, 32, count);
            _context.List->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_1_CONTROL_POINT_PATCHLIST + (count - 1));
        }

        /// <summary>
        /// Clear the render target
        /// </summary>
        /// <param name="rtv">The render target to clear</param>
        /// <param name="color">The RGBA color to clear it to</param>
        /// <param name="rect">The rectangle representing the section to clear</param>
        public void ClearRenderTarget(DescriptorHandle rtv, RgbaColor color, Rectangle rect)
        {
            _context.List->ClearRenderTargetView(rtv.CpuHandle, &color.R, 1, (RECT*)&rect);
        }

        /// <summary>
        /// Clear the render target and the depth stencil
        /// </summary>
        /// <param name="rtv">The render target to clear</param>
        /// <param name="dsv">The depth stencil target to clear</param>
        /// <param name="color">The RGBA color to clear it to</param>
        /// <param name="renderTargetRects">The rectangles representing the sections to clear. By default, this will clear the entire resource</param>
        /// <param name="depth">The <see cref="float"/> value to set the depth resource to. By default, this is <c>1</c></param>
        /// <param name="stencil">The <see cref="byte"/> value to set the stencil resource to. By default, this is <c>0</c></param>
        /// <param name="depthRects">The rectangles representing the sections to clear. By default, this will clear the entire resource</param>
        public void ClearRenderTargetAndDepthStencil(
            DescriptorHandle rtv,
            DescriptorHandle dsv,
            RgbaColor color = default,
            float depth = 1,
            byte stencil = 0,
            ReadOnlySpan<Rectangle> renderTargetRects = default,
            ReadOnlySpan<Rectangle> depthRects = default)
        {
            fixed (Rectangle* pRt = renderTargetRects)
            fixed (Rectangle* pDs = depthRects)
            {
                _context.FlushBarriers();

                _context.List->ClearRenderTargetView(rtv.CpuHandle, &color.R, (uint)renderTargetRects.Length, (RECT*)pRt);


                _context.List->ClearDepthStencilView(
                    dsv.CpuHandle,
                    D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH | D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_STENCIL,
                    depth, stencil,
                    (uint)depthRects.Length,
                    (RECT*)pDs
                );
            }
        }

        /// <summary>
        /// Clear the render target
        /// </summary>
        /// <param name="rtv">The render target to clear</param>
        /// <param name="color">The RGBA color to clear it to</param>
        /// <param name="rect">The rectangles representing the sections to clear. By default, this will clear the entire resource</param>
        public void ClearRenderTarget(DescriptorHandle rtv, RgbaColor color, ReadOnlySpan<Rectangle> rect = default)
        {
            fixed (Rectangle* p = rect)
            {
                _context.FlushBarriers();
                _context.List->ClearRenderTargetView(rtv.CpuHandle, &color.R, (uint)rect.Length, (RECT*)p);
            }
        }

        /// <summary>
        /// Clear the depth element of the depth stencil
        /// </summary>
        /// <param name="dsv">The depth stencil target to clear</param>
        /// <param name="depth">The <see cref="float"/> value to set the depth resource to. By default, this is <c>1</c></param>
        /// <param name="rect">The rectangles representing the sections to clear. By default, this will clear the entire resource</param>
        public void ClearDepth(DescriptorHandle dsv, float depth = 1, ReadOnlySpan<Rectangle> rect = default)
        {
            fixed (Rectangle* p = rect)
            {
                _context.FlushBarriers();
                _context.List->ClearDepthStencilView(dsv.CpuHandle, D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH, depth, 0, (uint)rect.Length, (RECT*)p);
            }
        }

        /// <summary>
        /// Clear the stencil element of the depth stencil
        /// </summary>
        /// <param name="dsv">The depth stencil target to clear</param>
        /// <param name="stencil">The <see cref="byte"/> value to set the stencil resource to. By default, this is <c>0</c></param>
        /// <param name="rect">The rectangles representing the sections to clear. By default, this will clear the entire resource</param>
        public void ClearStencil(DescriptorHandle dsv, byte stencil = 0, ReadOnlySpan<Rectangle> rect = default)
        {
            fixed (Rectangle* p = rect)
            {
                _context.FlushBarriers();
                _context.List->ClearDepthStencilView(dsv.CpuHandle, D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_STENCIL, 0, stencil, (uint)rect.Length, (RECT*)p);
            }
        }

        /// <summary>
        /// Clear the render target
        /// </summary>
        /// <param name="dsv">The depth stencil target to clear</param>
        /// <param name="depth">The <see cref="float"/> value to set the depth resource to. By default, this is <c>1</c></param>
        /// <param name="stencil">The <see cref="byte"/> value to set the stencil resource to. By default, this is <c>0</c></param>
        /// <param name="rect">The rectangles representing the sections to clear. By default, this will clear the entire resource</param>
        public void ClearDepthStencil(DescriptorHandle dsv, float depth = 1, byte stencil = 0, ReadOnlySpan<Rectangle> rect = default)
        {
            fixed (Rectangle* p = rect)
            {
                _context.FlushBarriers();
                _context.List->ClearDepthStencilView(dsv.CpuHandle, D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_STENCIL | D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH, depth, stencil, (uint)rect.Length, (RECT*)p);
            }
        }

        /// <summary>
        /// Set the viewports
        /// </summary>
        /// <param name="viewport">The viewport to set</param>
        public void SetViewports(Viewport viewport)
        {
            _context.List->RSSetViewports(1, (D3D12_VIEWPORT*)&viewport);
        }

        /// <summary>
        /// Set the scissor rectangles
        /// </summary>
        /// <param name="rectangles">The rectangles to set</param>
        public void SetScissorRectangles(ReadOnlySpan<Rectangle> rectangles)
        {
            fixed (Rectangle* pRects = rectangles)
            {
                _context.List->RSSetScissorRects((uint)rectangles.Length, (RECT*)pRects);
            }
        }

        /// <summary>
        /// Set the scissor rectangles
        /// </summary>
        /// <param name="rectangle">The rectangle to set</param>
        public void SetScissorRectangles(Rectangle rectangle)
        {
            _context.List->RSSetScissorRects(1, (RECT*)&rectangle);
        }

        /// <summary>
        /// Set the viewports
        /// </summary>
        /// <param name="viewports">The viewports to set</param>
        public void SetViewports(ReadOnlySpan<Viewport> viewports)
        {
            fixed (Viewport* pViewports = viewports)
            {
                _context.List->RSSetViewports((uint)viewports.Length, (D3D12_VIEWPORT*)pViewports);
            }
        }

        /// <summary>
        /// Set the vertex buffers
        /// </summary>
        /// <param name="vertexResource">The vertex buffer to set</param>
        /// <param name="startSlot">The slot on the device array to start setting vertex buffers to</param>
        /// <typeparam name="T">The type of the vertex in <see cref="Buffer"/></typeparam>
        public void SetVertexBuffers<T>(Buffer vertexResource, uint startSlot = 0)
            where T : unmanaged
        {
            var desc = CreateVertexBufferView<T>(vertexResource);

            _context.FlushBarriers();
            _context.List->IASetVertexBuffers(startSlot, 1, &desc);
        }

        /// <summary>
        /// Set the vertex buffers
        /// </summary>
        /// <param name="vertexBuffers">The vertex buffers to set</param>
        /// <param name="startSlot">The slot on the device array to start setting vertex buffers to</param>
        /// <typeparam name="T">The type of the vertex in <see cref="Buffer"/></typeparam>
        public void SetVertexBuffers<T>(ReadOnlySpan<Buffer> vertexBuffers, uint startSlot = 0)
            where T : unmanaged
        {
            Debug.Assert(StackSentinel.SafeToStackalloc<D3D12_VERTEX_BUFFER_VIEW>(vertexBuffers.Length));

            D3D12_VERTEX_BUFFER_VIEW* views = stackalloc D3D12_VERTEX_BUFFER_VIEW[vertexBuffers.Length];
            for (int i = 0; i < vertexBuffers.Length; i++)
            {
                views[i] = CreateVertexBufferView<T>(vertexBuffers[i]);
            }

            _context.FlushBarriers();
            _context.List->IASetVertexBuffers(startSlot, (uint)vertexBuffers.Length, views);
        }

        private static D3D12_VERTEX_BUFFER_VIEW CreateVertexBufferView<T>(Buffer buffer)
            where T : unmanaged
        {
            return new D3D12_VERTEX_BUFFER_VIEW
            {
                BufferLocation = buffer.GpuAddress,
                SizeInBytes = buffer.Length,
                StrideInBytes = (uint)sizeof(T)
            };
        }

        /// <summary>
        /// Set the index buffer
        /// </summary>
        /// <param name="indexResource">The index buffer to set</param>
        /// <typeparam name="T">The type of the index in <see cref="Buffer"/></typeparam>
        public void SetIndexBuffer<T>(Buffer indexResource)
            where T : unmanaged
        {
            var desc = CreateIndexBufferView(indexResource);
            _context.List->IASetIndexBuffer(&desc);

            static D3D12_INDEX_BUFFER_VIEW CreateIndexBufferView(Buffer buffer)
            {
                return new D3D12_INDEX_BUFFER_VIEW
                {
                    BufferLocation = buffer.GpuAddress,
                    SizeInBytes = buffer.Length,
                    Format = GetDxgiIndexType()
                };

                static DXGI_FORMAT GetDxgiIndexType()
                {
                    if (typeof(T) == typeof(int))
                    {
                        return DXGI_FORMAT.DXGI_FORMAT_R32_SINT;
                    }
                    else if (typeof(T) == typeof(uint))
                    {
                        return DXGI_FORMAT.DXGI_FORMAT_R32_UINT;
                    }
                    else if (typeof(T) == typeof(short))
                    {
                        return DXGI_FORMAT.DXGI_FORMAT_R16_SINT;
                    }
                    else if (typeof(T) == typeof(ushort))
                    {
                        return DXGI_FORMAT.DXGI_FORMAT_R16_UINT;
                    }

                    ThrowHelper.ThrowNotSupportedException("Unsupported index type, must be UInt32/Int32/UInt16/Int16");
                    return default;
                }
            }
        }

        /// <summary>
        /// Resolves a multi-sampled resource to a single-sampled resource
        /// </summary>
        /// <param name="source">The multi-sampled source <see cref="Texture"/></param>
        /// <param name="dest">The single-sampled dest <see cref="Texture"/></param>
        /// <param name="sourceSubresource">The index of the subresource from <paramref name="source"/> to use</param>
        /// <param name="destSubresource">The index of the subresource from <paramref name="dest"/> to use</param>
        public void ResolveSubresource(Texture source, Texture dest, uint sourceSubresource = 0, uint destSubresource = 0)
        {
            DataFormat format = source.Format == DataFormat.Unknown ? dest.Format : source.Format;

            if (!source.Resource.State.HasFlag(ResourceState.ResolveSource))
            {
                ResourceTransition(source, ResourceState.ResolveSource, sourceSubresource);
            }
            if (!dest.Resource.State.HasFlag(ResourceState.ResolveDestination))
            {
                ResourceTransition(dest, ResourceState.ResolveDestination, destSubresource);
            }

            _context.FlushBarriers();
            _context.List->ResolveSubresource(dest.GetResourcePointer(), destSubresource, source.GetResourcePointer(), sourceSubresource, (DXGI_FORMAT)format);
        }

        /// <summary>
        /// Resolves a multi-sampled resource to a single-sampled resource
        /// </summary>
        /// <param name="source">The multi-sampled source <see cref="Texture"/></param>
        /// <param name="dest">The single-sampled dest <see cref="Texture"/></param>
        /// <param name="format">The <see cref="DataFormat"/> to resolve as</param>
        /// <param name="sourceSubresource">The index of the subresource from <paramref name="source"/> to use</param>
        /// <param name="destSubresource">The index of the subresource from <paramref name="dest"/> to use</param>
        public void ResolveSubresource(Texture source, Texture dest, DataFormat format, uint sourceSubresource = 0, uint destSubresource = 0)
        {
            if (!source.Resource.State.HasFlag(ResourceState.ResolveSource))
            {
                ResourceTransition(source, ResourceState.ResolveSource, sourceSubresource);
            }
            if (!dest.Resource.State.HasFlag(ResourceState.ResolveDestination))
            {
                ResourceTransition(dest, ResourceState.ResolveDestination, destSubresource);
            }

            _context.FlushBarriers();
            _context.List->ResolveSubresource(dest.GetResourcePointer(), destSubresource, source.GetResourcePointer(), sourceSubresource, (DXGI_FORMAT)format);
        }

        /// <summary>
        /// Submits a draw call
        /// </summary>
        public void Draw(int vertexCountPerInstance, int startVertexLocation = 0)
            => DrawInstanced((uint)vertexCountPerInstance, 1, (uint)startVertexLocation, 0);

        /// <summary>
        /// Submits a draw call
        /// </summary>
        public void Draw(uint vertexCountPerInstance, uint startVertexLocation = 0)
            => DrawInstanced(vertexCountPerInstance, 1, startVertexLocation, 0);

        /// <summary>
        /// Submits an indexed draw call
        /// </summary>
        public void DrawIndexed(uint indexCountPerInstance, uint startIndexLocation = 0, int baseVertexLocation = 0)
            => DrawIndexedInstanced(indexCountPerInstance, 1, startIndexLocation, baseVertexLocation, 0);

        /// <summary>
        /// Submits an indexed draw call
        /// </summary>
        public void DrawIndexed(int indexCountPerInstance, int startIndexLocation = 0, int baseVertexLocation = 0)
            => DrawIndexedInstanced((uint)indexCountPerInstance, 1, (uint)startIndexLocation, baseVertexLocation, 0);

        /// <summary>
        /// Submits an instanced draw call
        /// </summary>
        public void DrawInstanced(uint vertexCountPerInstance, uint instanceCount, uint startVertexLocation, uint startInstanceLocation)
        {
            _context.FlushBarriers();
            _context.List->DrawInstanced(
                vertexCountPerInstance,
                instanceCount,
                startVertexLocation,
                startInstanceLocation
            );
        }

        /// <summary>
        /// Submits an indexed and instanced draw call
        /// </summary>
        public void DrawIndexedInstanced(uint indexCountPerInstance, uint instanceCount, uint startIndexLocation, int baseVertexLocation, uint startInstanceLocation)
        {
            _context.FlushBarriers();
            _context.List->DrawIndexedInstanced(
                indexCountPerInstance,
                instanceCount,
                startIndexLocation,
                baseVertexLocation,
                startInstanceLocation
            );
        }

        #region CopyContext Methods

        /// <summary>
        /// Copy an entire resource
        /// </summary>
        /// <param name="source">The resource to copy from</param>
        /// <param name="dest">The resource to copy to</param>
        public void CopyResource(Buffer source, Buffer dest)
            => this.AsCopyContext().CopyResource(source, dest);

        /// <summary>
        /// Copy an entire resource
        /// </summary>
        /// <param name="source">The resource to copy from</param>
        /// <param name="dest">The resource to copy to</param>
        public void CopyResource(Texture source, Texture dest)
            => this.AsCopyContext().CopyResource(source, dest);

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, T[] buffer, Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, Span<T> buffer, Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, ReadOnlySpan<T> buffer, Buffer destination) where T : unmanaged
            => this.AsCopyContext().UploadBuffer<T>(allocator, buffer, destination);

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, T[] buffer, out Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, out destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, Span<T> buffer, out Buffer destination) where T : unmanaged
            => UploadBuffer(allocator, (ReadOnlySpan<T>)buffer, out destination);


        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="buffer"></param>
        /// <param name="destination"></param>
        public void UploadBuffer<T>(GpuAllocator allocator, ReadOnlySpan<T> buffer, out Buffer destination) where T : unmanaged
            => this.AsCopyContext().UploadBuffer<T>(allocator, buffer, out destination);

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="texture"></param>
        /// <param name="subresources"></param>
        /// <param name="tex"></param>
        /// <param name="destination"></param>
        public void UploadTexture(GpuAllocator allocator, ReadOnlySpan<byte> texture, ReadOnlySpan<SubresourceData> subresources, TextureDesc tex, out Texture destination)
            => this.AsCopyContext().UploadTexture(allocator, texture, subresources, tex, out destination);

        /// <summary>
        /// Uploads a buffer from the CPU to the GPU
        /// </summary>
        /// <param name="allocator"></param>
        /// <param name="texture"></param>
        /// <param name="subresources"></param>
        /// <param name="destination"></param>
        public void UploadTexture(GpuAllocator allocator, ReadOnlySpan<byte> texture, ReadOnlySpan<SubresourceData> subresources, Texture destination)
            => this.AsCopyContext().UploadTexture(allocator, texture, subresources, destination);

        /// <summary>
        /// Mark a resource barrier on the command list
        /// </summary>
        /// <param name="resource">The resource to transition</param>
        /// <param name="transition">The transition</param>
        /// <param name="subresource">The subresource to transition</param>
        public void ResourceTransition(Buffer resource, ResourceState transition, uint subresource = 0xFFFFFFFF)
            => this.AsCopyContext().ResourceTransition(resource, transition, subresource);

        /// <summary>
        /// Mark a resource barrier on the command list
        /// </summary>
        /// <param name="resource">The resource to transition</param>
        /// <param name="transition">The transition</param>
        /// <param name="subresource">The subresource to transition</param>
        public void ResourceTransition(Texture resource, ResourceState transition, uint subresource = 0xFFFFFFFF)
            => this.AsCopyContext().ResourceTransition(resource, transition, subresource);

        #endregion

        /// <inheritdoc/>
        public void Dispose() => _context.Dispose();
    }

    /// <summary>
    /// Represents the parameters used for a call to <see cref="ID3D12GraphicsCommandList.DrawInstanced"/>
    /// </summary>
    public readonly struct DrawArgs
    {
        /// <summary>
        /// Number of indices read from the vertex buffer for each instance
        /// </summary>
        public readonly uint VertexCountPerInstance;

        /// <summary>
        /// Number of instances to draw
        /// </summary>
        public readonly uint InstanceCount;

        /// <summary>
        /// The location of the first vertex read by the GPU from the vertex buffer
        /// </summary>
        public readonly uint StartVertexLocation;

        /// <summary>
        /// A value added to each vertex before reading per-instance data from a vertex buffer
        /// </summary>
        public readonly uint StartInstanceLocation;

        /// <summary>
        /// Creates a new instance of <see cref="IndexedDraw"/>
        /// </summary>
        public DrawArgs(
            uint vertexCountPerInstance,
            uint instanceCount,
            uint startVertexLocation,
            uint startInstanceLocation
        )
        {
            VertexCountPerInstance = vertexCountPerInstance;
            InstanceCount = instanceCount;
            StartVertexLocation = startVertexLocation;
            StartInstanceLocation = startInstanceLocation;
        }
    }

    /// <summary>
    /// Extensions for <see cref="GraphicsContext"/>, <see cref="ComputeContext"/>, and <see cref="CopyContext"/>
    /// </summary>
    public static class ContextExtensions
    {
        /// <summary>
        /// Returns the <see cref="CopyContext"/> for a given <see cref="GraphicsContext"/>
        /// </summary>
        /// <param name="context">The <see cref="GraphicsContext"/> to convert</param>
        /// <returns>A <see cref="CopyContext"/> recording to the same list as <paramref name="context"/></returns>
        public static ref CopyContext AsCopyContext(this ref GraphicsContext context) => ref Unsafe.As<GraphicsContext, CopyContext>(ref context);

        /// <summary>
        /// Returns the <see cref="CopyContext"/> for a given <see cref="ComputeContext"/>
        /// </summary>
        /// <param name="context">The <see cref="ComputeContext"/> to convert</param>
        /// <returns>A <see cref="CopyContext"/> recording to the same list as <paramref name="context"/></returns>
        public static ref CopyContext AsCopyContext(this ref ComputeContext context) => ref Unsafe.As<ComputeContext, CopyContext>(ref context);

        /// <summary>
        /// Returns the <see cref="ComputeContext"/> for a given <see cref="GraphicsContext"/>
        /// </summary>
        /// <param name="context">The <see cref="GraphicsContext"/> to convert</param>
        /// <returns>A <see cref="ComputeContext"/> recording to the same list as <paramref name="context"/></returns>
        public static ref ComputeContext AsComputeContext(this ref GraphicsContext context) => ref Unsafe.As<GraphicsContext, ComputeContext>(ref context);
    }
}
