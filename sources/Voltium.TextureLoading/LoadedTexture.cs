using System;
using Voltium.Common;
using Voltium.Core.Memory;

namespace Voltium.TextureLoading
{
    /// <summary>
    /// Represents a DDS texture that has been loaded into memory and parsed
    /// </summary>
    public readonly struct FormatTexture
    {
        internal FormatTexture(
            ReadOnlyMemory<byte> bitData,
            TextureDesc desc,
            uint mipCount,
            LoaderFlags loaderFlags,
            bool isCubeMap,
            ReadOnlyMemory<SubresourceData> subresourceData,
            AlphaMode alphaMode,
            TexType underlyingTextureType)
        {
            Data = bitData;
            Desc = desc;
            MipCount = mipCount;
            LoaderFlags = loaderFlags;
            IsCubeMap = isCubeMap;
            SubresourceData = subresourceData;
            AlphaMode = alphaMode;
            UnderlyingTextureType = underlyingTextureType;
        }

        /// <summary>
        /// The original type of the texture. Note, as texture formats may be normalised by the loader, this may not reflect the type
        /// or format of <see cref="Data"/>
        /// </summary>
        public TexType UnderlyingTextureType { get; }

        /// <summary>
        /// The buffer that contains the data referenced by <see cref="SubresourceData"/>
        /// </summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>
        /// The <see cref="TextureDesc"/> for the texture
        /// </summary>
        public TextureDesc Desc { get; }

        /// <summary>
        /// Flags used by the loader
        /// </summary>
        public LoaderFlags LoaderFlags { get; }

        /// <summary>
        /// Whether the texture is a cube map
        /// </summary>
        public bool IsCubeMap { get; }

        /// <summary>
        /// The number of MIPs present
        /// </summary>
        public uint MipCount { get; }

        /// <summary>
        /// The subresource data, relative to <see cref="Data"/>, for upload
        /// </summary>
        public ReadOnlyMemory<SubresourceData> SubresourceData { get; }

        /// <summary>
        /// The alpha mode of the texture
        /// </summary>
        public AlphaMode AlphaMode { get; }


        /// <inheritdoc cref="object"/>
        public override string ToString()
        {
            using RentedStringBuilder val = StringHelpers.RentStringBuilder();

            val.AppendLine($"UnderlyingTextureType: {UnderlyingTextureType}");
            val.AppendLine($"BitData: {Data}");
            val.AppendLine($"ResourceDimension: {Desc.Dimension}");
            val.AppendLine($"Height: {Desc.Height}");
            val.AppendLine($"Width: {Desc.Width}");
            val.AppendLine($"DepthOrArraySize: {Desc.DepthOrArraySize}");
            val.AppendLine($"MipCount: {MipCount}");
            val.AppendLine($"Format: {Desc.Format}");
            val.AppendLine($"LoaderFlags: {LoaderFlags}");
            val.AppendLine($"IsCubeMap: {IsCubeMap}");
            val.AppendLine($"AlphaMode: {AlphaMode}");

            val.AppendLine("\nSubresource Data: ");
            foreach (SubresourceData managedSubresourceData in SubresourceData.Span)
            {
                val.Append('\t').AppendLine(managedSubresourceData.ToString().Replace("\n", "\n\t"));
            }

            val.Append('\n');


            return val.ToString();
        }
    }
}
