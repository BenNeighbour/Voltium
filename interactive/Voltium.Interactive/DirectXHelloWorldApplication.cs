using System.Diagnostics;
using TerraFX.Interop;
using Voltium.Core;
using Voltium.Core.Managers;
using static TerraFX.Interop.DXGI_FORMAT;
using static TerraFX.Interop.Windows;
using static TerraFX.Interop.D3D12_PRIMITIVE_TOPOLOGY_TYPE;
using static TerraFX.Interop.D3D12_INPUT_CLASSIFICATION;
using Voltium.Core.Configuration.Graphics;
using System;

namespace Voltium.Interactive
{
    internal class DirectXHelloWorldApplication : Application
    {
        public override string Title => "Hello DirectX!";
        private Renderer _renderer = new DefaultRenderer();

        public override unsafe void Init(ScreenData data)
        {
            GraphicalConfiguration config = new GraphicalConfiguration
            {
                ForceFullscreenAsWindowed = false,
                ScanlineOrdering = DXGI_MODE_SCANLINE_ORDER.DXGI_MODE_SCANLINE_ORDER_UNSPECIFIED,
                VSyncCount = 0,
                BackBufferFormat = DXGI_FORMAT_R8G8B8A8_UNORM,
                DepthStencilFormat = DXGI_FORMAT_D32_FLOAT,
                FullscreenScalingStrategy = DXGI_MODE_SCALING.DXGI_MODE_SCALING_UNSPECIFIED,
                MultiSamplingStrategy = new MsaaDesc(1, 0),
                RequiredDirect3DLevel = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                ScalingStrategy = DXGI_SCALING.DXGI_SCALING_NONE,
                SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD,
                SwapChainBufferCount = 3
            };

            DeviceManager.Initialize(config, data);
            _renderer.Init(config, in data, DeviceManager.Device);

            _renderer.Resize(data);
        }

        public override void Update(ApplicationTimer timer)
        {
            _renderer.Update(timer);
        }

        private Stopwatch watch = Stopwatch.StartNew();
        public override unsafe void Render()
        {
            using var commandList = GpuDispatchManager.Manager.BeginGraphicsContext(_renderer.GetInitialPso());

            commandList.SetViewportAndScissor(DeviceManager.ScreenData);
            _renderer.Render(commandList);

            GpuDispatchManager.Manager.End(commandList.Move());

            DeviceManager.Present();
        }

        public override void Destroy()
        {
            DeviceManager.Dispose();
        }

        public override void OnResize(ScreenData newScreenData)
        {
            DeviceManager.Resize(newScreenData);
            _renderer.Resize(newScreenData);
        }

        public override void OnKeyDown(byte key)
        {

        }

        public override void OnKeyUp(byte key)
        {

        }
    }
}
