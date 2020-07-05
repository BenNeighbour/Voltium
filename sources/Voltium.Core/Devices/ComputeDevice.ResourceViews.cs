using System.Runtime.CompilerServices;
using TerraFX.Interop;
using Voltium.Core.GpuResources;
using Voltium.Core.Memory.GpuResources;
using static TerraFX.Interop.Windows;
using Buffer = Voltium.Core.Memory.GpuResources.Buffer;

namespace Voltium.Core.Devices
{
    public unsafe partial class ComputeDevice
    {
        /// <summary>
        /// The <see cref="DescriptorHeap"/> for shader resource views, constant buffer views,
        /// and unordered access views
        /// </summary>
        protected DescriptorHeap ResourceDescriptors;

        private const int ResourceCount = 1024;
        private protected virtual void CreateDescriptorHeaps()
        {
            ResourceDescriptors = DescriptorHeap.Create(this, DescriptorHeapType.ConstantBufferShaderResourceOrUnorderedAccessView, ResourceCount);
        }

        /// <summary>
        /// Creates a shader resource view to a <see cref="Texture"/>
        /// </summary>
        /// <param name="resource">The <see cref="Texture"/> resource to create the view for</param>
        public DescriptorHandle CreateShaderResourceView(Texture resource)
        {
            var handle = ResourceDescriptors.GetNextHandle();

            DevicePointer->CreateShaderResourceView(resource.Resource.UnderlyingResource, null, handle.CpuHandle);

            return handle;
        }

        /// <summary>
        /// Creates a shader resource view to a <see cref="Texture"/>
        /// </summary>
        /// <param name="resource">The <see cref="Texture"/> resource to create the view for</param>
        /// <param name="desc">The <see cref="TextureShaderResourceViewDesc"/> describing the metadata used to create the view</param>
        public DescriptorHandle CreateShaderResourceView(Texture resource, in TextureShaderResourceViewDesc desc)
            => CreateShaderResourceView(resource, desc.Format, desc.MipLevels, desc.MostDetailedMip, desc.ResourceMinLODClamp, desc.PlaneSlice);

        /// <summary>
        /// Creates a shader resource view to a <see cref="Texture"/>
        /// </summary>
        /// <param name="resource">The <see cref="Texture"/> resource to create the view for</param>
        /// <param name="format"></param>
        /// <param name="mipLevels"></param>
        /// <param name="mostDetailedMip"></param>
        /// <param name="minLODClamp"></param>
        /// <param name="planeSlice"></param>
        public DescriptorHandle CreateShaderResourceView(Texture resource, DataFormat format, uint mipLevels = 0, uint mostDetailedMip = 0, float minLODClamp = 0, uint planeSlice = 0)
        {
            // multisampled textures can be created without a desc
            if (resource.Msaa.SampleCount > 1)
            {
                return CreateShaderResourceView(resource);
            }

            D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc;

            switch (resource.Dimension)
            {
                case TextureDimension.Tex1D:
                    srvDesc.Anonymous.Texture1D.MipLevels = mipLevels;
                    srvDesc.Anonymous.Texture1D.MostDetailedMip = mostDetailedMip;
                    srvDesc.Anonymous.Texture1D.ResourceMinLODClamp = minLODClamp;
                    srvDesc.ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE1D;
                    break;

                case TextureDimension.Tex2D:
                    srvDesc.Anonymous.Texture2D.MipLevels = mipLevels;
                    srvDesc.Anonymous.Texture2D.MostDetailedMip = mostDetailedMip;
                    srvDesc.Anonymous.Texture2D.ResourceMinLODClamp = minLODClamp;
                    srvDesc.Anonymous.Texture2D.PlaneSlice = planeSlice;
                    srvDesc.ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D;
                    break;

                case TextureDimension.Tex3D:
                    srvDesc.Anonymous.Texture3D.MipLevels = mipLevels;
                    srvDesc.Anonymous.Texture3D.MostDetailedMip = mostDetailedMip;
                    srvDesc.Anonymous.Texture3D.ResourceMinLODClamp = minLODClamp;
                    srvDesc.ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE3D;
                    break;
            }

            srvDesc.Format = (DXGI_FORMAT)format;
            srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING; // TODO

            var handle = ResourceDescriptors.GetNextHandle();

            DevicePointer->CreateShaderResourceView(resource.Resource.UnderlyingResource, &srvDesc, handle.CpuHandle);

            return handle;
        }


        /// <summary>
        /// Creates a shader resource view to a <see cref="Buffer"/>
        /// </summary>
        /// <param name="resource">The <see cref="Buffer"/> resource to create the view for</param>
        /// <param name="lengthInElements"><inheritdoc cref="BufferShaderResourceViewDesc.ElementStride"/></param>
        /// <param name="offset"></param>
        /// <param name="format"></param>
        /// <param name="isRaw"></param>
        public DescriptorHandle CreateShaderResourceView<T>(Buffer resource, uint lengthInElements, uint offset = 0, DataFormat format = DataFormat.Unknown, bool isRaw = false) where T : unmanaged
            => CreateShaderResourceView(resource, lengthInElements, (uint)sizeof(T), offset, format, isRaw);

        /// <summary>
        /// Creates a shader resource view to a <see cref="Buffer"/>
        /// </summary>
        /// <param name="resource">The <see cref="Buffer"/> resource to create the view for</param>
        /// <param name="lengthInElements"></param>
        /// <param name="elementSize"></param>
        /// <param name="offset"></param>
        /// <param name="format"></param>
        /// <param name="isRaw"></param>
        public DescriptorHandle CreateShaderResourceView(Buffer resource, uint lengthInElements, uint elementSize, uint offset = 0, DataFormat format = DataFormat.Unknown, bool isRaw = false)
        {
            Unsafe.SkipInit(out D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc);
            srvDesc.Format = (DXGI_FORMAT)format;
            srvDesc.ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_BUFFER;
            srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING; // TODO
            srvDesc.Anonymous.Buffer.FirstElement = offset;
            srvDesc.Anonymous.Buffer.Flags = isRaw ? D3D12_BUFFER_SRV_FLAGS.D3D12_BUFFER_SRV_FLAG_RAW : D3D12_BUFFER_SRV_FLAGS.D3D12_BUFFER_SRV_FLAG_NONE;
            srvDesc.Anonymous.Buffer.NumElements = lengthInElements;
            srvDesc.Anonymous.Buffer.StructureByteStride = elementSize;

            var handle = ResourceDescriptors.GetNextHandle();

            DevicePointer->CreateShaderResourceView(resource.Resource.UnderlyingResource, &srvDesc, ResourceDescriptors.GetNextHandle().CpuHandle);

            return handle;
        }

        /// <summary>
        /// Creates a shader resource view to a <see cref="Buffer"/>
        /// </summary>
        /// <param name="resource">The <see cref="Buffer"/> resource to create the view for</param>
        /// <param name="desc">The <see cref="BufferShaderResourceViewDesc"/> describing the metadata used to create the view</param>
        public DescriptorHandle CreateShaderResourceView(Buffer resource, in BufferShaderResourceViewDesc desc)
            => CreateShaderResourceView(resource, desc.ElementCount, desc.ElementStride, desc.Offset, desc.Format, desc.IsRaw);

        /// <summary>
        /// Creates a shader resource view to a <see cref="Buffer"/>
        /// </summary>
        /// <param name="resource">The <see cref="Buffer"/> resource to create the view for</param>
        public DescriptorHandle CreateShaderResourceView(Buffer resource)
        {
            var handle = ResourceDescriptors.GetNextHandle();

            DevicePointer->CreateShaderResourceView(resource.Resource.UnderlyingResource, null, handle.CpuHandle);

            return handle;
        }
    }
}
