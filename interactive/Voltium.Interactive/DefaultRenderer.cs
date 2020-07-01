using System.Diagnostics;
using System.Drawing;
using System.Resources;
using Voltium.Core;
using Voltium.Core.Managers;
using System.Numerics;
using Voltium.Core.GpuResources;
using Voltium.Core.Pipeline;
using System.Runtime.CompilerServices;
using Voltium.Core.Managers.Shaders;
using Voltium.Core.Configuration.Graphics;
using Voltium.Core.Memory.GpuResources;
using System;
using System.Runtime.InteropServices;
using Buffer = Voltium.Core.Memory.GpuResources.Buffer;
using Voltium.Core.D3D12;
using Voltium.ModelLoading;
using Voltium.TextureLoading;
using System.Linq;
using Voltium.Common;

namespace Voltium.Interactive
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

    public unsafe class BasicSceneRenderer : Renderer
    {
        private GpuAllocator _allocator = null!;
        private Buffer[] _vertexBuffer = null!;
        private Buffer[] _indexBuffer = null!;
        private Texture _texture;
        private Texture _normals;
        private int _zoomDelta;
        private Mesh<TexturedVertex>[] _texturedObjects = null!;
        private GraphicsDevice _device = null!;
        private DescriptorHandle _texHandle;
        private DescriptorHandle _normalHandle;
        private GraphicalConfiguration _config = null!;

        private MsaaDesc _msaaDesc = MsaaDesc.None;

        public override void Init(GraphicsDevice device, GraphicalConfiguration config, in Size screen)
        {
            PipelineManager.Reset();

            _device = device;
            _config = config;
            _allocator = _device.Allocator;

            _texturedObjects = ModelLoader.LoadGl("Assets/Gltf/Handgun_Tangent.gltf");
            var texture = TextureLoader.CreateTexture("Assets/Textures/handgun_c.dds");
            var normals = TextureLoader.CreateTexture("Assets/Textures/handgun_n.dds");

            _vertexBuffer = new Buffer[_texturedObjects.Length];
            _indexBuffer = new Buffer[_texturedObjects.Length];

            using (var list = _device.BeginCopyContext())
            {
                var buffIndex = 0;
                for (var i = 0; i < _texturedObjects.Length; i++, buffIndex++)
                {
                    list.UploadBuffer(_allocator, _texturedObjects[i].Vertices, out _vertexBuffer[buffIndex]);
                    list.UploadBuffer(_allocator, _texturedObjects[i].Indices, out _indexBuffer[buffIndex]);
                }

                list.UploadTexture(_allocator, texture.Data.Span, texture.SubresourceData.Span, texture.Desc, out _texture);
                list.UploadTexture(_allocator, normals.Data.Span, normals.SubresourceData.Span, normals.Desc, out _normals);
            }

            var srvDesc = new TextureShaderResourceViewDesc
            {
                MipLevels = texture.MipCount,
                Format = texture.Desc.Format,
                MostDetailedMip = 0
            };

            _texHandle = _device.CreateShaderResourceView(_texture, srvDesc);
            _normalHandle = _device.CreateShaderResourceView(_normals, srvDesc);
            _objectConstants = new ObjectConstants[_texturedObjects.Length];

            _obj = _allocator.AllocateBuffer(MathHelpers.AlignUp(sizeof(ObjectConstants), 256) * _texturedObjects.Length, MemoryAccess.CpuUpload);
            _frame = _allocator.AllocateBuffer(sizeof(FrameConstants), MemoryAccess.CpuUpload);
            _light = _allocator.AllocateBuffer(sizeof(LightConstants), MemoryAccess.CpuUpload);

            CreatePipelines();
            InitializeConstants();

            Resize(screen);
        }

        public override void Resize(Size newScreenData)
        {
            var dsDesc = TextureDesc.CreateDepthStencilDesc(DataFormat.D32Single, (uint)newScreenData.Height, (uint)newScreenData.Width, 1, 0, _msaaDesc);
            var rtDesc = TextureDesc.CreateRenderTargetDesc(_config.BackBufferFormat, (uint)newScreenData.Height, (uint)newScreenData.Width, RgbaColor.CornflowerBlue, _msaaDesc);

            _depthStencil = _allocator.AllocateTexture(dsDesc, ResourceState.DepthWrite);
            _renderTarget = _allocator.AllocateTexture(rtDesc, ResourceState.RenderTarget);

            var dsv = new TextureDepthStencilViewDesc
            {
                Format = dsDesc.Format,
                IsMultiSampled = _msaa,
                MipIndex = 0,
                PlaneSlice = 0
            };

            var rtv = new TextureRenderTargetViewDesc
            {
                Format = _texture.Format,
                MipIndex = 0,
                PlaneSlice = 0
            };

            _depthStencilView = _device.CreateDepthStencilView(_depthStencil, dsv);
            _renderTargetView = _device.CreateRenderTargetView(_renderTarget, rtv);

            var aspectRatio = (float)newScreenData.Width / newScreenData.Height;
            var fovAngleY = 70.0f * MathF.PI / 180.0f;
            _frameConstants.Projection = Matrix4x4.CreatePerspectiveFieldOfView(fovAngleY, aspectRatio, 0.001f, 100f);
        }

        public void CreatePipelines()
        {
            var rootParams = new[]
            {
                RootParameter.CreateDescriptor(RootParameterType.ConstantBufferView, 0, 0),
                RootParameter.CreateDescriptor(RootParameterType.ConstantBufferView, 1, 0),
                RootParameter.CreateDescriptor(RootParameterType.ConstantBufferView, 2, 0),
                RootParameter.CreateDescriptorTable(DescriptorRangeType.ShaderResourceView, 0, 2, 0)
            };

            var samplers = new[]
            {
                new StaticSampler(
                    TextureAddressMode.Clamp,
                    SamplerFilterType.Anistropic,
                    //SamplerFilterType.MagLinear | SamplerFilterType.MinLinear | SamplerFilterType.MipLinear,
                    shaderRegister: 0,
                    registerSpace: 0,
                    ShaderVisibility.All,
                    StaticSampler.OpaqueWhite
                )
            };

            _rootSig = RootSignature.Create(_device, rootParams, samplers);

            var compilationFlags = new[]
            {
                DxcCompileFlags.PackMatricesInRowMajorOrder,
                //DxcCompileFlags.DefineMacro("NORMALS")
            };

            var vertexShader = ShaderManager.CompileShader("Shaders/SimpleTexture/TextureVertexShader.hlsl", DxcCompileTarget.Vs_6_0, compilationFlags);
            var pixelShader = ShaderManager.CompileShader("Shaders/SimpleTexture/TexturePixelShader.hlsl", DxcCompileTarget.Ps_6_0, compilationFlags);

            var psoDesc = new GraphicsPipelineDesc(_rootSig, _renderTarget.Format, _depthStencil.Format, vertexShader, pixelShader);

            PipelineManager.CreatePso<TexturedVertex>(_device, "Texture", psoDesc);

            psoDesc.Msaa = _msaaDesc;
            PipelineManager.CreatePso<TexturedVertex>(_device, "TextureMSAA", psoDesc);
        }

        private ObjectConstants[] _objectConstants = null!;
        private FrameConstants _frameConstants;
        private LightConstants _sceneLight;

        private Buffer _obj;
        private Buffer _frame;
        private Buffer _light;

        public void InitializeConstants()
        {
            for (var i = 0; i < _texturedObjects.Length; i++)
            {
                var geometry = _texturedObjects[i];
                _objectConstants[i] = new ObjectConstants
                {
                    World = geometry.World,
                    Tex = Matrix4x4.Identity,
                    Material = geometry.Material,
                    //Material = new Material
                    //{
                    //    DiffuseAlbedo = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
                    //    ReflectionFactor = new(0.05f),
                    //    Shininess = 0.8f
                    //}
                };
            }

            _frameConstants = new FrameConstants
            {
                View = Matrix4x4.CreateLookAt(
                    new Vector3(0.0f, 0.7f, 1f),
                    new Vector3(0.0f, 0.0f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f)
                ),
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

        private RootSignature _rootSig = null!;
        //private PipelineStateObject _texPso = null!;
        //private PipelineStateObject _msaaTexPso = null!;
        //private PipelineStateObject _colorPso = null!;

        private Matrix4x4 _perFrameRotation = Matrix4x4.CreateRotationY(10f)/* * Matrix4x4.CreateRotationX(0.001f)*/;
        //private int _totalCount = 0;

        public override void Update(ApplicationTimer timer)
        {
            // scale between 0 and 5 seconds
            //var scale = Matrix4x4.CreateScale((float)(Math.Abs((total % 10) - 5)) / 5);u
            float scale = _zoomDelta;

            if (scale < 0)
            {
                scale = 1 / Math.Abs(scale);
            }
            else if (scale == 0)
            {
                scale = 1;
            }

            //_objectConstants.World *= Matrix4x4.CreateScale(scale);

            // rotate a small amount each frame
            //

            for (var i = 0u; i < _objectConstants.Length; i++)
            {
                _objectConstants[i].World *= Matrix4x4.CreateRotationY(0.5f * (float)timer.ElapsedSeconds);
                _obj.WriteConstantBufferData(ref _objectConstants[i], i);
            }

            _frame.WriteConstantBufferData(ref _frameConstants, 0);
            _light.WriteConstantBufferData(ref _sceneLight, 0);

            _zoomDelta = 0;
        }

        public override void ToggleMsaa()
        {
            _msaa = !_msaa;
            _msaaDesc = _msaa ? MsaaDesc.X8 : MsaaDesc.None;
        }

        public override PipelineStateObject GetInitialPso()
        {
            return _msaa ? PipelineManager.RetrievePso("TextureMSAA")  : PipelineManager.RetrievePso("Texture");
        }

        private Texture _renderTarget;
        private DescriptorHandle _renderTargetView;


        private Texture _depthStencil;
        private DescriptorHandle _depthStencilView;

        private bool _msaa = false;

        public override void Render(ref GraphicsContext recorder)
        {
            recorder.SetViewportAndScissor(_device.OutputRectangle);
            recorder.ResourceTransition(_renderTarget, ResourceState.RenderTarget);

            recorder.SetRenderTargets(_renderTargetView, 1, _depthStencilView);
            recorder.ClearRenderTargetAndDepthStencil(_renderTargetView, _depthStencilView, RgbaColor.CornflowerBlue);

            recorder.SetConstantBuffer(1, _frame);
            recorder.SetConstantBuffer(2, _light);
            recorder.SetRootDescriptorTable(3, _texHandle);

            recorder.SetTopology(Topology.TriangeList);

            for (var i = 0u; i < _texturedObjects.Length; i++)
            {
                recorder.SetConstantBuffer<ObjectConstants>(0, _obj, i);
                recorder.SetVertexBuffers<TexturedVertex>(_vertexBuffer[i]);
                recorder.SetIndexBuffer<ushort>(_indexBuffer[i]);

                recorder.DrawIndexed(_texturedObjects[i].Indices.Length);
            }

            if (_msaa)
            {
                recorder.ResolveSubresource(_renderTarget, _device.BackBuffer);
            }
            else
            {
                recorder.CopyResource(_renderTarget, _device.BackBuffer);
            }

            recorder.ResourceTransition(_device.BackBuffer, ResourceState.Present);
        }

        public override void Dispose()
        {
            _rootSig.Dispose();
            _device.Dispose();
        }

        public override void OnMouseScroll(int scroll)
        {
            _zoomDelta = scroll;
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
    }
}
