using System;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Voltium.Common;
using Voltium.Core;
using Voltium.Core.Configuration.Graphics;
using Voltium.Core.Memory;
using Voltium.Core.Devices;
using Voltium.Core.Devices.Shaders;
using Voltium.Core.Pipeline;
using Voltium.ModelLoading;
using Voltium.TextureLoading;
using Voltium.Common.Pix;
using Buffer = Voltium.Core.Memory.Buffer;
using Voltium.RenderEngine;
using SixLabors.ImageSharp;
using System.Diagnostics.CodeAnalysis;

namespace Voltium.Interactive.BasicRenderPipeline
{
    [StructLayout(LayoutKind.Sequential)]
    public partial struct ObjectConstants
    {
        public Matrix4x4 World;
        public Matrix4x4 Tex;
        public Material Material;
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial struct FrameConstants
    {
        public Matrix4x4 View;
        public Matrix4x4 Projection;
        public Vector4 AmbientLight;
        public Vector3 CameraPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LightConstants
    {
        public DirectionalLight Light0;
        public DirectionalLight Light1;
        public DirectionalLight Light2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial struct DirectionalLight
    {
        public Vector3 Strength;
        private float _pad0;
        public Vector3 Direction;
        private float _pad1;
    }

    public unsafe class BasicSceneRenderer : GraphicsRenderPass
    {
        private Buffer[] _vertexBuffer = null!;
        private Buffer[] _indexBuffer = null!;
        private Texture _texture;
        private RenderObject<TexturedVertex>[] _texturedObjects = null!;
        private GraphicsDevice _device = null!;
        private DescriptorAllocation _textureHandle;

        private DescriptorHeap _rtvs, _dsvs;
        //private Texture _normals;
        //private DescriptorHandle _normalHandle;

        private RootSignature _rootSig = null!;

        private ObjectConstants[] _objectConstants = null!;
        private FrameConstants _frameConstants;
        private LightConstants _sceneLight;

        private Buffer _obj;
        private Buffer _frame;
        private Buffer _light;

        private const DataFormat DepthStencilFormat = DataFormat.Depth32Single;
        private const DataFormat RenderTargetFormat = DataFormat.R8G8B8A8UnsignedNormalized;

        public BasicSceneRenderer(GraphicsDevice device)
        {
            _device = device;

            _rtvs = _device.CreateDescriptorHeap(DescriptorHeapType.RenderTargetView, 1);
            _dsvs = _device.CreateDescriptorHeap(DescriptorHeapType.RenderTargetView, 1);
            _textureHandle = _device.AllocateResourceDescriptors(2);

            _texturedObjects = ModelLoader.LoadGl_Old("Assets/Gltf/Handgun_NoTangent.gltf");
            //_texturedObjects = new[] { GeometryGenerator.CreateCube(0.5f) };

            var texture = TextureLoader.LoadTextureDesc("Assets/Textures/handgun_c.dds");
            var normals = TextureLoader.LoadTextureDesc("Assets/Textures/handgun_n.dds");

            _vertexBuffer = new Buffer[_texturedObjects.Length];
            _indexBuffer = new Buffer[_texturedObjects.Length];

            using (var list = _device.BeginUploadContext())
            {
                for (var i = 0; i < _texturedObjects.Length; i++)
                {
                    _vertexBuffer[i] = list.UploadBuffer(_texturedObjects[i].Vertices);
                    _vertexBuffer[i].SetName("VertexBuffer");

                    _indexBuffer[i] = list.UploadBuffer(_texturedObjects[i].Indices);
                    _indexBuffer[i].SetName("IndexBuffer");
                }

                _texture = list.UploadTexture(texture);
                _texture.SetName("Gun texture");
            }

            //list.UploadTexture(normals.Data.Span, normals.SubresourceData.Span, normals.Desc, ResourceState.PixelShaderResource, out _normals);
            //_normals.SetName("Gun normals");

           _device.CreateShaderResourceView(_texture, _textureHandle[0]);
            //_normalHandle = _device.CreateShaderResourceView(_normals);
            _objectConstants = new ObjectConstants[_texturedObjects.Length];

            _obj = _device.Allocator.AllocateBuffer(MathHelpers.AlignUp(sizeof(ObjectConstants), 256) * _texturedObjects.Length, MemoryAccess.CpuUpload);
            _obj.SetName("ObjectConstants buffer");

            _frame = _device.Allocator.AllocateBuffer(sizeof(FrameConstants), MemoryAccess.CpuUpload);
            _frame.SetName("FrameConstants buffer");

            _light = _device.Allocator.AllocateBuffer(sizeof(LightConstants), MemoryAccess.CpuUpload);
            _light.SetName("LightConstants buffer");

            CreatePipelines();
            InitializeConstants();
        }

        //[MemberNotNull(nameof(_tex), nameof(_texMsaa8x))]
        public void CreatePipelines()
        {
            var rootParams = new[]
            {
                RootParameter.CreateDescriptor(RootParameterType.ConstantBufferView, 0, 0),
                RootParameter.CreateDescriptor(RootParameterType.ConstantBufferView, 1, 0),
                RootParameter.CreateDescriptor(RootParameterType.ConstantBufferView, 2, 0),
                RootParameter.CreateDescriptorTable(DescriptorRangeType.ShaderResourceView, 0, 1, 0)
            };

            var samplers = new[]
            {
                new StaticSampler(
                    TextureAddressMode.Clamp,
                    SamplerFilterType.Anistropic,
                    shaderRegister: 0,
                    registerSpace: 0,
                    ShaderVisibility.All,
                    StaticSampler.OpaqueWhite
                )
            };

            _rootSig = _device.CreateRootSignature(rootParams, samplers);

            var compilationFlags = new[]
            {
                ShaderCompileFlag.PackMatricesInRowMajorOrder,
                ShaderCompileFlag.AllResourcesBound,
                ShaderCompileFlag.EnableDebugInformation,
                ShaderCompileFlag.WriteDebugInformationToFile()
                //ShaderCompileFlag.DefineMacro("NORMALS")
            };

            var vertexShader = ShaderManager.CompileShader("Shaders/SimpleTexture/TextureVertexShader.hlsl", ShaderModel.Vs_6_0, compilationFlags);
            var pixelShader = ShaderManager.CompileShader("Shaders/SimpleTexture/TexturePixelShader.hlsl", ShaderModel.Ps_6_0, compilationFlags);

            var psoDesc = new GraphicsPipelineDesc
            {
                RootSignature = _rootSig,
                RenderTargetFormats = RenderTargetFormat,
                DepthStencilFormat = DepthStencilFormat,
                VertexShader = vertexShader,
                PixelShader = pixelShader,
                Topology = TopologyClass.Triangle,
                Inputs = InputLayout.FromType<TexturedVertex>()
            };

            _tex = _device.PipelineManager.CreatePipelineStateObject(psoDesc, "Texture");

            //psoDesc.Msaa = MultisamplingDesc.X8;
            _texMsaa8x = _device.PipelineManager.CreatePipelineStateObject(psoDesc, "Texture_MSAA8X");
        }

        private PipelineStateObject _tex = null!;
        private PipelineStateObject _texMsaa8x = null!;

        public void InitializeConstants()
        {
            for (var i = 0; i < _texturedObjects.Length; i++)
            {
                var geometry = _texturedObjects[i];
                _objectConstants[i] = new ObjectConstants
                {
                    World = geometry.World,
                    Tex = Matrix4x4.Identity,
                    Material = geometry.Material
                };
            }

            _frameConstants = new FrameConstants
            {
                View = Matrix4x4.CreateLookAt(
                    new Vector3(0.0f, 0.7f, 1.5f),
                    new Vector3(0.0f, -0.1f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f)
                ),
                Projection = Matrix4x4.Identity,
                AmbientLight = new Vector4(0.25f, 0.25f, 0.35f, 1.0f) / 2,
                CameraPosition = new Vector3(0.0f, 0.7f, 1.5f),
            };

            _sceneLight.Light0 = new DirectionalLight
            {
                Strength = new Vector3(0.5f),
                Direction = new Vector3(0.57735f, -0.57735f, 0.57735f)
            };

            _sceneLight.Light1 = new DirectionalLight
            {
                Strength = new Vector3(0.5f),
                Direction = new Vector3(-0.57735f, -0.57735f, 0.57735f)
            };

            _sceneLight.Light2 = new DirectionalLight
            {
                Strength = new Vector3(0.5f),
                Direction = new Vector3(0.0f, -0.707f, -0.707f)
            };
        }


        public void WriteConstantBuffers()
        {
            for (var i = 0U; i < _objectConstants.Length; i++)
            {
                _obj.WriteConstantBufferData(ref _objectConstants[i], i);
            }

            _frame.WriteConstantBufferData(ref _frameConstants, 0);
            _light.WriteConstantBufferData(ref _sceneLight, 0);
        }

        public override bool Register(ref RenderPassBuilder builder, ref Resolver resolver)
        {
            var resources = new PipelineResources();
            var settings = resolver.GetComponent<PipelineSettings>();

            resources.SceneColor = builder.CreatePrimaryOutputRelativeTexture(
                TextureDesc.CreateRenderTargetDesc(DataFormat.R8G8B8A8UnsignedNormalized, Rgba128.CornflowerBlue, settings.Msaa),
                ResourceState.RenderTarget,
                debugName: nameof(resources.SceneColor)
            );

            resources.SceneDepth = builder.CreatePrimaryOutputRelativeTexture(
                TextureDesc.CreateDepthStencilDesc(DataFormat.Depth32Single, 1.0f, 0, false, settings.Msaa),
                ResourceState.DepthWrite,
                debugName: nameof(resources.SceneDepth)
            );

            resolver.CreateComponent(resources);

            DefaultPipelineState = settings.Msaa.IsMultiSampled ? _texMsaa8x : _tex;

            var fovAngleY = 70.0f * MathF.PI / 180.0f;
            _frameConstants.Projection = Matrix4x4.CreatePerspectiveFieldOfView(fovAngleY, settings.AspectRatio, 0.001f, 100f);

            return true;
        }

        public override void Record(GraphicsContext recorder, ref Resolver resolver)
        {
            WriteConstantBuffers();

            using var _ = recorder.ScopedEvent(Argb32.Red, "BasicSceneRenderer");

            var resources = resolver.GetComponent<PipelineResources>();
            var settings = resolver.GetComponent<PipelineSettings>();

            var sceneColor = resolver.ResolveResource(resources.SceneColor);
            var sceneDepth = resolver.ResolveResource(resources.SceneDepth);

            _device.CreateRenderTargetView(sceneColor, _rtvs[0]);
            _device.CreateDepthStencilView(sceneDepth, _dsvs[0]);

            recorder.SetViewportAndScissor(sceneColor.Resolution);

            recorder.SetAndClearRenderTarget(_rtvs[0], Rgba128.CornflowerBlue, _dsvs[0]);

            recorder.SetConstantBuffer(1, _frame);
            recorder.SetConstantBuffer(2, _light);
            recorder.SetRootDescriptorTable(3, _textureHandle[0]);

            recorder.SetTopology(Topology.TriangleList);

            using (Profiler.BeginProfileBlock("Render Object"))
            using (recorder.ScopedEvent(Argb32.AliceBlue, "Render Objects"))
            {
                for (var i = 0u; i < _texturedObjects.Length; i++)
                {
                    recorder.SetConstantBuffer<ObjectConstants>(0, _obj, i);
                    recorder.SetVertexBuffers<TexturedVertex>(_vertexBuffer[i]);
                    recorder.SetIndexBuffer<uint>(_indexBuffer[i]);

                    recorder.DrawIndexed(_texturedObjects[i].Indices.Length);
                }
            }
        }
    }
}
