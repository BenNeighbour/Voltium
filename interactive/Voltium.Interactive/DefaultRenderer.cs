using System.Diagnostics;
using System.Drawing;
using System.Resources;
using TerraFX.Interop;
using Voltium.Common;
using Voltium.Core;
using Voltium.Core.Managers;
using static TerraFX.Interop.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DXGI_FORMAT;
using static TerraFX.Interop.D3D12_INPUT_CLASSIFICATION;
using System.Numerics;
using Voltium.Core.GpuResources;
using Voltium.Core.Pipeline;
using System.Runtime.CompilerServices;
using Voltium.Core.Managers.Shaders;
using Voltium.Core.Configuration.Graphics;
using Voltium.Core.Memory.GpuResources;
using System;
using Voltium.Core.Memory.GpuResources.ResourceViews;
using System.Runtime.InteropServices;

namespace Voltium.Interactive
{
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    public partial struct ObjectConstants
    {
        public Matrix4x4 World;
        public Material Material;
    }

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    public partial struct FrameConstants
    {
        public Matrix4x4 View;
        public Matrix4x4 Projection;
        public Vector4 AmbientLight;
        public Vector3 CameraPosition;
    }

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    public struct LightConstants
    {
        public DirectionalLight Light0;
        public DirectionalLight Light1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial struct DirectionalLight
    {
        public Vector3 Strength;
        private float _pad0;
        public Vector3 Direction;
        private float _pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial struct Material
    {
        public Vector4 DiffuseAlbedo;
        public Vector4 ReflectionFactor;
        public float Shininess;
    };

    [ShaderInput]
    public partial struct Vertex
    {
        public Vertex(Vector3 position, Vector3 normal)
        {
            Position = position;
            Normal = normal;
        }

        public Vertex(
            float vertexX, float vertexY, float vertexZ,
            float normalX, float normalY, float normalZ,
            float tangentX, float tangentY, float tangentZ,
            float texU, float texV)
        {
            Position = new(vertexX, vertexY, vertexZ);
            Normal = new(normalX, normalY, normalZ);
        }

        public Vector3 Position;
        public Vector3 Normal;
    }

    public unsafe class DefaultRenderer : Renderer
    {
        private GpuAllocator _allocator = null!;
        private Buffer<Vertex> _vertexBuffer;
        private Buffer<ushort> _indexBuffer;
        private int _zoomLevel;
        private Geometry _object;
        private GraphicsDevice _device = null!;

        public override void Init(GraphicsDevice device, GraphicalConfiguration config, in ScreenData screen)
        {
            _device = device;
            _allocator = _device.Allocator;

            _object = GemeotryGenerator.LoadSingleModel("logo.obj");
            //_object = GemeotryGenerator.CreateCube(0.5f);

            _vertexBuffer = _allocator.AllocateBuffer(_object.Vertices, BufferKind.Vertex, GpuMemoryKind.CpuUpload);
            _indexBuffer = _allocator.AllocateBuffer(_object.Indices, BufferKind.Index, GpuMemoryKind.CpuUpload);

            var rootParams = new[]
            {
                RootParameter.CreateDescriptor(RootParameterType.ConstantBufferView, 0, 0),
                RootParameter.CreateDescriptor(RootParameterType.ConstantBufferView, 1, 0),
                RootParameter.CreateDescriptor(RootParameterType.ConstantBufferView, 2, 0)
            };

            _rootSig = RootSignature.Create(device.Device, rootParams, default);

            var compilationFlags = new[]
            {
                DxcCompileFlags.DisableOptimizations,
                DxcCompileFlags.EnableDebugInformation,
                DxcCompileFlags.WriteDebugInformationToFile(),
                DxcCompileFlags.PackMatricesInRowMajorOrder
            };

            var vertexShader = ShaderManager.CompileShader("Shaders/SimpleVertexShader.hlsl", DxcCompileTarget.Vs_6_0, compilationFlags);
            var pixelShader = ShaderManager.CompileShader("Shaders/SimplePixelShader.hlsl", new DxcCompileTarget(ShaderType.Pixel, 6, 0), compilationFlags);

            var psoDesc = new GraphicsPipelineDesc(_rootSig, config.BackBufferFormat, config.DepthStencilFormat, vertexShader, pixelShader);

            _drawPso = PipelineManager.CreatePso<Vertex>(_device, "Default", psoDesc);

            _objectConstants = _allocator.AllocateBuffer<ObjectConstants>(2, BufferKind.Constant, GpuMemoryKind.CpuUpload);
            _frameConstants = _allocator.AllocateBuffer<FrameConstants>(2, BufferKind.Constant, GpuMemoryKind.CpuUpload);
            _sceneLight = _allocator.AllocateBuffer<LightConstants>(2, BufferKind.Constant, GpuMemoryKind.CpuUpload);

            _objectConstants.Map();
            _frameConstants.Map();
            _sceneLight.Map();

            SetHueDegrees(MathF.PI / 500);
        }

        public override void Resize(ScreenData screen)
        {
            var aspectRatio = (float)screen.Width / screen.Height;
            var fovAngleY = 70.0f * MathF.PI / 180.0f;

            _objectConstants[0] = new ObjectConstants
            {
                World = Matrix4x4.Identity,
                Material = new Material
                {
                    DiffuseAlbedo = (Vector4)RgbaColor.WhiteSmoke,
                    ReflectionFactor = new(0.3f),
                    Shininess = 1f
                }
            };

            _frameConstants[0] = new FrameConstants
            {
                View = Matrix4x4.CreateLookAt(
                    new Vector3(0.0f, 0.7f, 1.5f),
                    new Vector3(0.0f, -0.1f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f)
                ),
                Projection = Matrix4x4.CreatePerspectiveFieldOfView(fovAngleY, aspectRatio, 0.001f, 100f),
                AmbientLight = new Vector4(0.25f, 0.25f, 0.25f, 1.0f) / 2,
                CameraPosition = new Vector3(0.0f, 0.7f, 1.5f),
            };

            _sceneLight[0].Light0 = new DirectionalLight
            {
                Strength = new Vector3(1, 1, 1),
                Direction = new Vector3(0, -1, 0)
            };

            //_sceneLight.LocalCopy.Light1 = new DirectionalLight
            //{
            //    Strength = new Vector3(0.8f),
            //    Color = new Vector3(1, 1, 0),
            //    Direction = new Vector3(-0.57735f, 0.57735f, -0.57735f)
            //};
        }

        private RgbaColor _color = new RgbaColor(1, 0, 0, 1);
        private RootSignature _rootSig = null!;
        private PipelineStateObject _drawPso = null!;

        private Buffer<ObjectConstants> _objectConstants;
        private Buffer<FrameConstants> _frameConstants;
        private Buffer<LightConstants> _sceneLight;

        private Matrix4x4 _perFrameRotation = Matrix4x4.CreateRotationY(10f)/* * Matrix4x4.CreateRotationX(0.001f)*/;
        //private int _totalCount = 0;

        private bool _isFirst = false;
        public override void Update(ApplicationTimer timer)
        {
            // rotate a small amount each frame
            _objectConstants[0].World *= Matrix4x4.CreateRotationY(0.5f * (float)timer.ElapsedSeconds);

            // scale between 0 and 5 seconds
            //var scale = Matrix4x4.CreateScale((float)(Math.Abs((total % 10) - 5)) / 5);u

            float scale = _zoomLevel;

            if (scale < 0)
            {
                scale = 1 / Math.Abs(scale);
            }
            else if (scale == 0)
            {
                scale = 1;
            }

            var ind = _isFirst ? 0u : 1u;
            _isFirst = !_isFirst;
            _objectConstants[ind].World = _objectConstants[0].World * Matrix4x4.CreateScale(scale);
            _objectConstants[ind].Material = _objectConstants[0].Material;

            _frameConstants[ind] = _frameConstants[0];

            _sceneLight[ind] = _sceneLight[0];

            //_color = ChangeHue(_color);
        }

        public override PipelineStateObject GetInitialPso()
        {
            return _drawPso;
        }

        public override void Render(GraphicsContext recorder)
        {
            var renderTarget = _device.RenderTarget;
            var renderTargetView = _device.RenderTargetView;
            var depthStencilView = _device.DepthStencilView;

            recorder.SetGraphicsRootSignature(_rootSig);

            recorder.ResourceTransition(renderTarget, ResourceState.RenderTarget);

            recorder.SetRenderTarget(renderTargetView.CpuHandle, 1, depthStencilView.CpuHandle);

            recorder.ClearRenderTarget(renderTargetView, RgbaColor.White);
            recorder.ClearDepth(depthStencilView);

            recorder.SetVertexBuffers(_vertexBuffer);
            recorder.SetIndexBuffer(_indexBuffer);

            recorder.SetGraphicsConstantBufferDescriptor(0, _objectConstants, 0);
            recorder.SetGraphicsConstantBufferDescriptor(1, _frameConstants, 0);
            recorder.SetGraphicsConstantBufferDescriptor(2, _sceneLight, 0);

            recorder.SetTopology(Topology.TriangeList);
            recorder.DrawIndexed((uint)_object.Indices.Length);

            recorder.ResourceTransition(renderTarget, ResourceState.Present);
        }

        private void SetHueDegrees(float radians)
        {
            var cosA = MathF.Cos(radians);
            var sinA = MathF.Sin(radians);

            HueMatrix[0, 0] = cosA + ((1.0f - cosA) / 3.0f);
            HueMatrix[0, 1] = (1.0f / 3.0f * (1.0f - cosA)) - (MathF.Sqrt(1.0f / 3.0f) * sinA);
            HueMatrix[0, 2] = (1.0f / 3.0f * (1.0f - cosA)) + (MathF.Sqrt(1.0f / 3.0f) * sinA);
            HueMatrix[1, 0] = (1.0f / 3.0f * (1.0f - cosA)) + (MathF.Sqrt(1.0f / 3.0f) * sinA);
            HueMatrix[1, 1] = cosA + (1.0f / 3.0f * (1.0f - cosA));
            HueMatrix[1, 2] = (1.0f / 3.0f * (1.0f - cosA)) - (MathF.Sqrt(1.0f / 3.0f) * sinA);
            HueMatrix[2, 0] = (1.0f / 3.0f * (1.0f - cosA)) - (MathF.Sqrt(1.0f / 3.0f) * sinA);
            HueMatrix[2, 1] = (1.0f / 3.0f * (1.0f - cosA)) + (MathF.Sqrt(1.0f / 3.0f) * sinA);
            HueMatrix[2, 2] = cosA + (1.0f / 3.0f * (1.0f - cosA));
        }

        private float[,] HueMatrix = new float[3, 3];

        private RgbaColor ChangeHue(RgbaColor color)
        {
            static int Clamp(float v)
            {
                if (v < 0)
                {
                    return 0;
                }
                if (v > 255)
                {
                    return 255;
                }
                return (int)(v + 0.5f);
            }

            var r0 = (byte)(color.R * 255);
            var g0 = (byte)(color.G * 255);
            var b0 = (byte)(color.B * 255);

            var fr0 = (r0 * HueMatrix[0, 0]) + (g0 * HueMatrix[0, 1]) + (b0 * HueMatrix[0, 2]);
            var fg0 = (r0 * HueMatrix[1, 0]) + (g0 * HueMatrix[1, 1]) + (b0 * HueMatrix[1, 2]);
            var fb0 = (r0 * HueMatrix[2, 0]) + (g0 * HueMatrix[2, 1]) + (b0 * HueMatrix[2, 2]);

            return new RgbaColor(Clamp(fr0) / 255f, Clamp(fg0) / 255f, Clamp(fb0) / 255f, 1);
        }

        public override void Destroy()
        {
            _objectConstants.Unmap();
            _rootSig.Dispose();
            _device.Dispose();
        }

        public override void OnMouseScroll(int scroll)
        {
            _zoomLevel += scroll;
        }
    }
}
