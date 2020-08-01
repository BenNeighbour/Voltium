using System;
using System.Drawing;
using System.Numerics;
using Voltium.Core;
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
        private Output2D _output = null!;
        private GraphicsPipelineStateObject _pso = null!;
        private Buffer _vertices;

        public override string Name => nameof(HelloTriangleApp);

        public unsafe override void Initialize(Size outputSize, IOutputOwner output)
        {
            // Create the device and output
            _device = new GraphicsDevice(DeviceConfiguration.Default);
            _output = Output2D.Create(OutputConfiguration.Default, _device, output);
            OnResize(outputSize);

            // The vertices for our triangle
            ReadOnlySpan<HelloWorldVertex> vertices = stackalloc HelloWorldVertex[3]
            {
                new HelloWorldVertex { Position = new Vector3(+0.0f, +0.25f, +0.0f), Color = (Vector4)Rgba128.Red },
                new HelloWorldVertex { Position = new Vector3(-0.25f, -0.25f, +0.0f), Color = (Vector4)Rgba128.Green },
                new HelloWorldVertex { Position = new Vector3(+0.25f, -0.25f, +0.0f), Color = (Vector4)Rgba128.Blue },
            };

            // Allocate the vertices, using the overload which takes some initial data
            _vertices = _device.Allocator.AllocateBuffer(vertices);

            // The pipeline description. We compile shaders at runtime here, which is simpler but less efficient
            var psoDesc = new GraphicsPipelineDesc
            {
                RenderTargetFormats = _output.Configuration.BackBufferFormat,
                Topology = TopologyClass.Triangle,
                VertexShader = ShaderManager.CompileShader("HelloTriangle/Shader.hlsl", ShaderType.Vertex, entrypoint: "VertexMain"),
                PixelShader = ShaderManager.CompileShader("HelloTriangle/Shader.hlsl", ShaderType.Pixel, entrypoint: "PixelMain")
            };

            // Create the PSO, providing our shader type which had the necessary members generated by applying [ShaderInput]
            _pso = _device.PipelineManager.CreatePipelineStateObject<HelloWorldVertex>("Draw", psoDesc);
        }

        public override void OnResize(Size newOutputSize) => _output.Resize(newOutputSize); 

        public override void Update(ApplicationTimer timer) { /* This app doesn't do any updating */ }
        public override void Render()
        {
            var context = _device.BeginGraphicsContext(_pso);

            // We need to transition the back buffer to ResourceState.RenderTarget so we can draw to it
            context.ResourceTransition(_output.OutputBuffer, ResourceState.RenderTarget);
            // Set that we render to the entire screen, clear the render target, set the vertex buffer, and set the topology we will use
            context.SetViewportAndScissor(_output.Dimensions);
            context.SetAndClearRenderTarget(_output.OutputBufferView, Rgba128.CornflowerBlue);
            context.SetVertexBuffers<HelloWorldVertex>(_vertices);
            context.SetTopology(Topology.TriangeList);
            // Draw the 3 vertices
            context.Draw(3);
            // We need to transition the back buffer to ResourceState.Present so it can be presented
            context.ResourceTransition(_output.OutputBuffer, ResourceState.Present);

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
