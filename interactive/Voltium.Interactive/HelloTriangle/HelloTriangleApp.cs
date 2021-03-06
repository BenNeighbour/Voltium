using System;
using System.Drawing;
using System.Numerics;
using Microsoft.Extensions.Logging;
using TerraFX.Interop;
using Voltium.Common;
using Voltium.Core;
using Voltium.Core.Contexts;
using Voltium.Core.Devices;
using Voltium.Core.Devices.Shaders;
using Voltium.Core.Pipeline;
using Buffer = Voltium.Core.Memory.Buffer;

namespace Voltium.Interactive.HelloTriangle
{
    // This is our vertex type used in the shader
    // [ShaderInput] triggers a source generator to create a shader input description which we need later
    [ShaderInput]
    internal partial struct HelloWorldVertex
    {
        public Vector3 Position;
        public Vector4 Color;
    }

        public sealed class HelloTriangleApp : Application
    {
        private GraphicsDevice _device = null!;
        private Output _output = null!;
        private PipelineStateObject _pso = null!;
        private Buffer _vertices;

        public unsafe override void Initialize(Size outputSize, IOutputOwner output)
        {
            var debug = new DebugLayerConfiguration()
                .WithDebugFlags(DebugFlags.DebugLayer)
                .WithDredFlags(DredFlags.All)
                .WithBreakpointLogLevel(LogLevel.None);

            _device = GraphicsDevice.Create(FeatureLevel.GraphicsLevel11_0, null, debug);
            _output = Output.Create(OutputConfiguration.Default, _device, output);

            OnResize(outputSize);

            ReadOnlySpan<HelloWorldVertex> vertices = stackalloc HelloWorldVertex[3]
            {
                new HelloWorldVertex { Position = new Vector3(+0.25f, -0.25f, +0.0f), Color = (Vector4)Rgba128.Blue },
                new HelloWorldVertex { Position = new Vector3(-0.25f, -0.25f, +0.0f), Color = (Vector4)Rgba128.Green },
                new HelloWorldVertex { Position = new Vector3(+0.0f, +0.25f, +0.0f), Color = (Vector4)Rgba128.Red },
            };

            // Allocate the vertices, using the overload which takes some initial data
            _vertices = _device.Allocator.AllocateUploadBuffer(vertices);

            // The pipeline description. We compile shaders at runtime here, which is simpler but less efficient
            var psoDesc = new GraphicsPipelineDesc
            {
                Topology = TopologyClass.Triangle,
                VertexShader = ShaderManager.CompileShader("HelloTriangle/Shader.hlsl", ShaderType.Vertex, entrypoint: "VertexMain"),
                PixelShader = ShaderManager.CompileShader("HelloTriangle/Shader.hlsl", ShaderType.Pixel, entrypoint: "PixelMain"),
                RenderTargetFormats = _output.Configuration.BackBufferFormat,
                DepthStencil = DepthStencilDesc.DisableDepthStencil,
                Inputs = InputLayout.FromType<HelloWorldVertex>(),
            };

            _pso = _device.PipelineManager.CreatePipelineStateObject(psoDesc, nameof(_pso));
        }

        public override void OnResize(Size newOutputSize) => _output.Resize(newOutputSize);
        public override void Update(ApplicationTimer timer) { /* This app doesn't do any updating */ }
        public unsafe override void Render()
        {
            var context = _device.BeginGraphicsContext(_pso);


            // We need to transition the back buffer to ResourceState.RenderTarget so we can draw to it
            using (context.ScopedBarrier(ResourceBarrier.Transition(_output.OutputBuffer, ResourceState.Present, ResourceState.RenderTarget)))
            {
                // Set that we render to the entire screen, clear the render target, set the vertex buffer, and set the topology we will use
                context.SetViewportAndScissor(_output.Resolution);
                context.SetAndClearRenderTarget(_output.OutputBufferView, Rgba128.CornflowerBlue);
                context.SetVertexBuffers<HelloWorldVertex>(_vertices);
                context.SetTopology(Topology.TriangleList);
                context.Draw(_vertices.LengthAs<HelloWorldVertex>());
            }

            context.Close();

            // Execute the context and wait for it to finish
            _device.Execute(context).Block();

            // Present the rendered frame to the output
            _output.Present();
        }

        public override void Dispose()
        {
            _pso.Dispose();
            _vertices.Dispose();
            _output.Dispose();
            _device.Dispose();
        }
    }
}
