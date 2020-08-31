using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Toolkit.HighPerformance.Extensions;
using TerraFX.Interop;
using Voltium.Common;
using Voltium.Common.Strings;
using Voltium.Core.Devices.Shaders;
using Voltium.Core.Pipeline;
using static TerraFX.Interop.Windows;

namespace Voltium.Core.Devices
{
    // TODO: allow serializing to file sonehow
    /// <summary>
    /// In charge of creation, storing, and retrieving pipeline state objects (PSOs)
    /// </summary>
    [ThreadSafe]
    public unsafe class PipelineManager
    {
        private ComputeDevice _device;
        private ComPtr<ID3D12PipelineLibrary1> _psoLibrary;


        /// <summary>
        /// Creates a new <see cref="PipelineManager"/> for a device
        /// </summary>
        /// <param name="device">The <see cref="ComputeDevice"/> to create the manager for</param>
        public PipelineManager(ComputeDevice device)
        {
            _device = device;

            Span<byte> cache = GetCache();

            fixed (byte* pCache = cache)
            {
                using ComPtr<ID3D12PipelineLibrary1> psoLibrary = default;
                int hr = _device.DevicePointerAs<ID3D12Device2>()->CreatePipelineLibrary(pCache, (uint)cache.Length, psoLibrary.Iid, (void**)&psoLibrary);

                if (hr is D3D12_ERROR_DRIVER_VERSION_MISMATCH or D3D12_ERROR_ADAPTER_NOT_FOUND)
                {
                    // cache invalidated
                    Guard.ThrowIfFailed(_device.DevicePointerAs<ID3D12Device2>()->CreatePipelineLibrary(null, 0, psoLibrary.Iid, (void**)&psoLibrary));
                }
                else if (FAILED(hr))
                {
                    Guard.ThrowIfFailed(hr, "_device.DevicePointerAs<ID3D12Device2>()->CreatePipelineLibrary(pCache, (uint)cache.Length, psoLibrary.Iid, (void**)&psoLibrary);");
                }

                _psoLibrary = psoLibrary.Move();
            }
        }

        private Span<byte> GetCache()
        {
            if (File.Exists(CachePsoLibLocation))
            {
                return File.ReadAllBytes(CachePsoLibLocation);
            }

            return Span<byte>.Empty;
        }

        ///// <summary>
        ///// Creates a new named pipeline state object and registers it in the library for retrieval with
        ///// <see cref="RetrievePso(string, in GraphicsPipelineDesc)"/>
        ///// </summary>
        ///// <param name="desc">The descriptor for the pipeline state</param>
        ///// <param name="name">The name of the pipeline state</param>
        //public PipelineStateObject CreatePipelineStateObject<TShaderInput, TPipelineStream>(in TPipelineStream desc, string name) where TPipelineStream : unmanaged, IPipelineStreamType
        //{
        //    // Prevent readonly copy being made. These fields are all private
        //    Unsafe.AsRef(in desc)._Initialize();

        //    fixed (void* p = &desc)
        //    {
        //        var pso = new D3D12_PIPELINE_STATE_STREAM_DESC
        //        {
        //            pPipelineStateSubobjectStream = p,
        //            SizeInBytes = (nuint)sizeof(TPipelineStream)
        //        };

        //        using ComPtr<ID3D12PipelineState> state = default;
        //        _device.ThrowIfFailed(_device.DevicePointerAs<ID3D12Device2>()->CreatePipelineState(&pso, state.Iid, (void**)&state));
        //    }

        //    return null!;
        //}

        /// <summary>
        /// Creates a new named pipeline state object and registers it in the library for retrieval
        /// </summary>
        /// <param name="desc">The descriptor for the pipeline state</param>
        /// <param name="name">The name of the pipeline state</param>
        public PipelineStateObject CreatePipelineStateObject<TPipelineStream>(in TPipelineStream desc, string name) where TPipelineStream : /* workaround for roslyn issue - unmanaged, */ IPipelineStreamType
        {
            // Prevent readonly copy being made. These fields are all private
            Unsafe.AsRef(in desc)._Initialize();

            fixed (void* p = &Unsafe.As<TPipelineStream, byte>(ref Unsafe.AsRef(in desc)))
            fixed (char* pName = name)
            {
                var pso = new D3D12_PIPELINE_STATE_STREAM_DESC
                {
                    pPipelineStateSubobjectStream = p,
                    SizeInBytes = (uint)Unsafe.SizeOf<TPipelineStream>()
                };

                //PipelineStreamParser.DebugStream(&pso);




                using ComPtr<ID3D12PipelineState> state = default;

                int hr = _psoLibrary.Ptr->LoadPipeline((ushort*)pName, &pso, state.Iid, (void**)&state);
                if (hr == E_INVALIDARG)
                {
                    _device.ThrowIfFailed(_device.DevicePointerAs<ID3D12Device2>()->CreatePipelineState(&pso, state.Iid, (void**)&state));
                    _device.ThrowIfFailed(_psoLibrary.Ptr->StorePipeline((ushort*)pName, state.Ptr));
                }
                else
                {
                    _device.ThrowIfFailed(hr, "_psoLibrary.Ptr->LoadPipeline((ushort*)pName, &pso, state.Iid, (void**)&state)");
                }

                if (ComIdentity.TryGetManagedObject<ID3D12PipelineState, PipelineStateObject>(state.Ptr, out var pipeline))
                {
                    return pipeline;
                }
                else
                {
                    pipeline = new PipelineStateObject(state.Move());
                    ComIdentity.RegisterComObject(state.Ptr, pipeline);
                    return pipeline;
                }
            }
        }

//        /// <summary>
//        /// Creates a new named pipeline state object and registers it in the library for retrieval with
//        /// <see cref="RetrievePso(string, in GraphicsPipelineDesc)"/>
//        /// </summary>
//        /// <param name="graphicsDesc">The descriptor for the pipeline state</param>
//        /// <param name="name">The name of the pipeline state</param>
//        public GraphicsPipelineStateObject CreatePipelineStateObject<TShaderInput>(in GraphicsPipelineDesc graphicsDesc, string name) where TShaderInput : unmanaged, IBindableShaderType
//        {
//            try
//            {
//                var copy = graphicsDesc;
//                copy.Inputs = new InputLayout(default(TShaderInput).GetShaderInputs().Span);


//                return CreatePipelineStateObject(name, copy);
//            }
//            catch (Exception e)
//            {
//#if REFLECTION
//                // if this happens when ShaderInputAttribute is applied, our generator is bugged
//                bool hasGenAttr = typeof(TShaderInput).GetCustomAttribute<ShaderInputAttribute>() is object;

//                const string hasGenAttrMessage = "This appears to be a failure with the " +
//                    "IA input type generator ('Voltium.Analyzers.IAInputDescGenerator'). Please file a bug";

//                const string noGenAttrMessage = "You appear to have manually implemented the IA input methods. Ensure they do not throw when called on a defaulted struct" +
//                    "('default(TShaderInput).GetShaderInputs()')";

//                ThrowHelper.ThrowArgumentException(
//                    $"IA input type '{typeof(TShaderInput).Name}' threw an exception of type '{e.GetType().Name}'. " +
//                    $"Inspect InnerException to view this exception. {(hasGenAttr ? hasGenAttrMessage : noGenAttrMessage)}", e);
//#else
//                ThrowHelper.ThrowArgumentException(
//                    $"IA input type '{nameof(TShaderInput)}' threw an exception. " +
//                    $"Inspect InnerException to view this exception. Reflection is disabled so no further information could be gathered", e);

//                return default!;
//#endif
//            }
//        }

        private const string CacheExtension = ".cpsolib";
        private static readonly string CachePsoLibLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltium.Core.PipelineManagerCache") + CacheExtension;

        private void Cache()
        {
            var size = _psoLibrary.Ptr->GetSerializedSize();

            using var buff = RentedArray<byte>.Create(checked((int)size));
            using var file = File.Create(CachePsoLibLocation);

            fixed (byte* pBuff = buff)
            {
                Guard.ThrowIfFailed(_psoLibrary.Ptr->Serialize(pBuff, size));
            }

            file.Write(buff.Value.AsSpan(0, (int)size));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _psoLibrary.Dispose();
        }
    }
}
