using TerraFX.Interop;
using Voltium.Common;

namespace Voltium.Core.Pipeline
{
    /// <summary>
    /// A <see cref="PipelineStateObject"/> for a grpahics pipeline
    /// </summary>
    public unsafe sealed class GraphicsPipelineStateObject : PipelineStateObject
    {
        /// <summary>
        /// The <see cref="GraphicsPipelineDesc"/> for this pipeline
        /// </summary>
        public readonly GraphicsPipelineDesc Desc;

        internal override unsafe ID3D12RootSignature* GetRootSig()
            => Desc.RootSignature is null ? null : Desc.RootSignature.Value;

        internal GraphicsPipelineStateObject(UniqueComPtr<ID3D12PipelineState> pso, GraphicsPipelineDesc desc) : base(pso.As<ID3D12Object>())
        {
            Desc = desc;
        }
    }
}
