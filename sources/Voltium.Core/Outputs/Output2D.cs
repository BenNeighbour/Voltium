using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TerraFX.Interop;
using Voltium.Common;
using Voltium.Core.Infrastructure;
using Voltium.Core.Devices;
using Voltium.Extensions;
using Voltium.Core.Memory;
using static TerraFX.Interop.Windows;
using Rectangle = System.Drawing.Rectangle;
using System.Buffers;

namespace Voltium.Core.Devices
{
    /// <summary>
    /// An output that displays graphics to the user
    /// </summary>
    public unsafe class Output2D
    {
        private struct BackBufferBuffer8
        {
            public static readonly uint MaxBufferCount = 8;

#pragma warning disable CS0649
            public Texture E0;
            public Texture E1;
            public Texture E2;
            public Texture E3;
            public Texture E4;
            public Texture E5;
            public Texture E6;
            public Texture E7;
#pragma warning restore CS0649

            public ref Texture this[uint index]
                => ref Unsafe.Add(ref GetPinnableReference(), (int)index);

            public ref Texture GetPinnableReference()
                => ref MemoryMarshal.GetReference(MemoryMarshal.CreateSpan(ref E0, 0));
        }

        private struct DescriptorHandleBuffer8
        {
            public static readonly uint MaxBufferCount = 8;

#pragma warning disable CS0649
            public DescriptorHandle E0;
            public DescriptorHandle E1;
            public DescriptorHandle E2;
            public DescriptorHandle E3;
            public DescriptorHandle E4;
            public DescriptorHandle E5;
            public DescriptorHandle E6;
            public DescriptorHandle E7;
#pragma warning restore CS0649

            public ref DescriptorHandle this[uint index]
                => ref Unsafe.Add(ref GetPinnableReference(), (int)index);

            public ref DescriptorHandle GetPinnableReference()
                => ref MemoryMarshal.GetReference(MemoryMarshal.CreateSpan(ref E0, 0));
        }

        private OutputConfiguration _desc;
        private GraphicsDevice _device;

        //private IBufferWriter<byte>? _bufferWriter;

        private DescriptorHeap _viewHeap;

        private ComPtr<IDXGISwapChain3> _swapChain;
        private BackBufferBuffer8 _backBuffers;
        private DescriptorHandleBuffer8 _views;
        private uint _backBufferIndex;

        /// <summary>
        /// The <see cref="OutputConfiguration"/> used 
        /// </summary>
        public OutputConfiguration Configuration => _desc;

        /// <summary>
        /// The current back buffer index
        /// </summary>
        public uint CurrentOutputBufferIndex => _backBufferIndex;

        /// <summary>
        /// Returns the backbuffer for a given index
        /// </summary>
        public Texture GetOutputBuffer(uint index)
        {
            if (index > OutputBufferCount)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(index));
            }

            return _backBuffers[index];
        }

        /// <summary>
        /// Resize the render resources
        /// </summary>
        /// <param name="newSize">The <see cref="Size"/> indicating the size to resize to</param>
        public void Resize(Size newSize)
        {
            if (newSize == Dimensions)
            {
                return;
            }

            _device.Idle();
            Dimensions = newSize;
            AspectRatio = Dimensions.AspectRatio();

            ResizeBuffers(newSize);
            CreateTexturesFromBuffers();
            CreateViews();
        }

        /// <summary>
        /// The <see cref="Size"/> of the output
        /// </summary>
        public Size Dimensions { get; private set; }

        /// <summary>
        /// The aspect ratio of the output
        /// </summary>
        public float AspectRatio { get; private set; }

        /// <summary>
        /// The number of output buffers
        /// </summary>
        public uint OutputBufferCount => _desc.BackBufferCount;

        /// <summary>
        /// The current output buffer texture
        /// </summary>
        public Texture OutputBuffer => _backBuffers[_backBufferIndex];


        /// <summary>
        /// The current output buffer texture view
        /// </summary>
        public DescriptorHandle OutputBufferView => _views[_backBufferIndex];

        private Output2D(GraphicsDevice device, OutputConfiguration desc)
        {
            _device = device;
            _desc = desc;

            CreateTexturesFromBuffers();

            // need to create views etc
            throw new NotImplementedException();
        }

        private Output2D(GraphicsDevice device, ComPtr<IDXGISwapChain1> swapChain, OutputConfiguration desc)
        {
            _device = device;

            if (!swapChain.TryQueryInterface(out ComPtr<IDXGISwapChain3> swapChain3))
            {
                ThrowHelper.ThrowPlatformNotSupportedException("Couldn't create IDXGISwapChain3, which is required");
            }

            DXGI_SWAP_CHAIN_DESC1 swapChainDesc;
            Guard.ThrowIfFailed(swapChain3.Get()->GetDesc1(&swapChainDesc));
            Dimensions = new Size((int)swapChainDesc.Width, (int)swapChainDesc.Height);
            AspectRatio = Dimensions.AspectRatio();

            _desc = desc;
            _swapChain = swapChain3.Move();

            CreateTexturesFromBuffers();
            CreateViews();
        }

        private void CreateViews()
        {
            // Create or reset the heap
            if (!_viewHeap.Exists)
            {
                _viewHeap = DescriptorHeap.Create(_device, DescriptorHeapType.RenderTargetView, Configuration.BackBufferCount);
            }
            else
            {
                _viewHeap.ResetHeap();
            }


            for (var i = 0u; i < Configuration.BackBufferCount; i++)
            {
                _views[i] = _device.CreateRenderTargetView(_backBuffers[i], _viewHeap.GetNextHandle());
            }
        }

        private void CreateTexturesFromBuffers()
        {
            for (var i = 0U; i < _desc.BackBufferCount; i++)
            {
                using ComPtr<ID3D12Resource> buffer = default;
                Guard.ThrowIfFailed(_swapChain.Get()->GetBuffer(i, buffer.Iid, ComPtr.GetVoidAddressOf(&buffer)));
                DebugHelpers.SetName(buffer.Get(), $"BackBuffer #{i}");

                _backBuffers[i] = Texture.FromResource(_device, buffer.Move());
            }

            _backBufferIndex = _swapChain.Get()->GetCurrentBackBufferIndex();
        }

        /// <summary>
        /// Creates a new <see cref="Output2D"/> to a <see cref="IOutputOwner"/>
        /// </summary>
        /// <param name="desc">The <see cref="OutputConfiguration"/> for this output</param>
        /// <param name="device">The <see cref="GraphicsDevice"/> that will output to this buffer</param>
        /// <param name="window">The <see cref="IOutputOwner"/> that owns the window</param>
        /// <param name="outputArea">Optionally, the <see cref="Size"/> of the rendered output. By default, this will be the entire window</param>
        /// <returns>A new <see cref="Output2D"/></returns>
        public static Output2D Create(OutputConfiguration desc, GraphicsDevice device, IOutputOwner window, Size outputArea = default)
        {
            return window.Type switch
            {
                OutputType.Hwnd => CreateForWin32(device, desc, window.GetOutput(), outputArea),
                OutputType.ICoreWindow => CreateForWinRT(device, desc, (void*)window.GetOutput(), outputArea),
                _ => throw new ArgumentOutOfRangeException(nameof(window))
            };
        }

        /// <summary>
        /// Creates a new <see cref="Output2D"/> to a Win32 Window backed by a HWND
        /// </summary>u
        /// <param name="device">The <see cref="GraphicsDevice"/> that will output to this buffer</param>
        /// <param name="desc">The <see cref="OutputConfiguration"/> for this output</param>
        /// <param name="window">The <see cref="IHwndOwner"/> that owns the window</param>
        /// <param name="outputArea">Optionally, the <see cref="Size"/> of the rendered output. By default, this will be the entire window</param>
        /// <returns>A new <see cref="Output2D"/></returns>
        public static Output2D CreateForWin32(GraphicsDevice device, OutputConfiguration desc, IHwndOwner window, Size outputArea = default)
            => CreateForWin32(device, desc, window.GetHwnd(), outputArea);

        /// <summary>
        /// Creates a new <see cref="Output2D"/> to a Win32 Window backed by a HWND
        /// </summary>
        /// <param name="device">The <see cref="GraphicsDevice"/> that will output to this buffer</param>
        /// <param name="desc">The <see cref="OutputConfiguration"/> for this output</param>
        /// <param name="window">The HWND for the window to bind to</param>
        /// <param name="outputArea">Optionally, the <see cref="Size"/> of the rendered output. By default, this will be the entire window</param>
        /// <returns>A new <see cref="Output2D"/></returns>
        public static Output2D CreateForWin32(GraphicsDevice device, OutputConfiguration desc, IntPtr window, Size outputArea = default)
        {
            var swapChainDesc = CreateDesc(desc, outputArea);

            using ComPtr<IDXGIFactory2> factory = CreateFactory(device);

            using ComPtr<IDXGISwapChain1> swapChain = default;

            Guard.ThrowIfFailed(factory.Get()->CreateSwapChainForHwnd(
                device.GetGraphicsQueue(),
                window,
                &swapChainDesc,
                null, //&fullscreenDesc,
                null, // TODO maybe implement
                ComPtr.GetAddressOf(&swapChain)
            ));

            var output = new Output2D(device, swapChain.Move(), desc);

            return output;
        }


        /// <summary>
        /// Creates a new <see cref="Output2D"/> to a WinRT ICoreWindow
        /// </summary>
        /// <param name="device">The <see cref="GraphicsDevice"/> that will output to this buffer</param>
        /// <param name="desc">The <see cref="OutputConfiguration"/> for this output</param>
        /// <param name="window">The <see cref="ICoreWindowsOwner"/> that owns the window</param>
        /// <param name="outputArea">Optionally, the <see cref="Size"/> of the rendered output. By default, this will be the entire window</param>
        /// <returns>A new <see cref="Output2D"/></returns>
        public static Output2D CreateForWinRT(GraphicsDevice device, OutputConfiguration desc, ICoreWindowsOwner window, Size outputArea = default)
            => CreateForWinRT(device, desc, window.GetIUnknownForWindow(), outputArea);

        /// <summary>
        /// Creates a new <see cref="Output2D"/> to a WinRT ICoreWindow
        /// </summary>
        /// <param name="device">The <see cref="GraphicsDevice"/> that will output to this buffer</param>
        /// <param name="desc">The <see cref="OutputConfiguration"/> for this output</param>
        /// <param name="window">The IUnknown* for the window to bind to</param>
        /// <param name="outputArea">Optionally, the <see cref="Size"/> of the rendered output. By default, this will be the entire window</param>
        /// <returns>A new <see cref="Output2D"/></returns>
        public static Output2D CreateForWinRT(GraphicsDevice device, OutputConfiguration desc, void* window, Size outputArea = default)
        {
            var swapChainDesc = CreateDesc(desc, outputArea);

            using ComPtr<IDXGIFactory2> factory = CreateFactory(device);

            using ComPtr<IDXGISwapChain1> swapChain = default;

            Guard.ThrowIfFailed(factory.Get()->CreateSwapChainForCoreWindow(
                device.GetGraphicsQueue(),
                (IUnknown*)window,
                &swapChainDesc,
                null, // TODO maybe implement
                ComPtr.GetAddressOf(&swapChain)
            ));


            var output = new Output2D(device, swapChain.Move(), desc);

            return output;
        }


        /// <summary>
        /// Creates a new <see cref="Output2D"/> to a WinRT ISwapChainPanelNative
        /// </summary>
        /// <param name="device">The <see cref="GraphicsDevice"/> that will output to this buffer</param>
        /// <param name="desc">The <see cref="OutputConfiguration"/> for this output</param>
        /// <param name="swapChainPanelNative">The IUnknown* for the ISwapChainPanelNative to bind to</param>
        /// <param name="outputArea">The <see cref="Size"/> of the rendered output</param>
        /// <returns>A new <see cref="Output2D"/></returns>
        public static Output2D CreateForSwapChainPanel(GraphicsDevice device, OutputConfiguration desc, void* swapChainPanelNative, Size outputArea)
        {
            var swapChainDesc = CreateDesc(desc, outputArea);

            using ComPtr<IDXGIFactory2> factory = CreateFactory(device);

            using ComPtr<IDXGISwapChain1> swapChain = default;

            Guard.ThrowIfFailed(factory.Get()->CreateSwapChainForComposition(
                device.GetGraphicsQueue(),
                &swapChainDesc,
                null, // TODO maybe implement
                ComPtr.GetAddressOf(&swapChain)
            ));

            Guard.ThrowIfFailed(((ISwapChainPanelNative*)swapChainPanelNative)->SetSwapChain((IDXGISwapChain*)swapChain.Get()));

            var output = new Output2D(device, swapChain.Move(), desc);

            return output;
        }

        private static ComPtr<IDXGIFactory2> CreateFactory(GraphicsDevice device)
        {
            using ComPtr<IDXGIFactory2> factory = default;

            // Try get the factory from the device if possible. Won't work if the device is a IDXCoreAdapter tho, then we fallback to manual creation
            int hr;
            if (device.Adapter.GetAdapterPointer() is not null && ComPtr.TryQueryInterface(device.Adapter.GetAdapterPointer(), out IDXGIAdapter* dxgiAdapter))
            {
                hr = dxgiAdapter->GetParent(factory.Iid, ComPtr.GetVoidAddressOf(&factory));
                _ = dxgiAdapter->Release();
            }
            else
            {
                hr = CreateDXGIFactory1(factory.Iid, ComPtr.GetVoidAddressOf(&factory));
            }

            if (hr == E_NOINTERFACE)
            {
                // we don't actually *need* IDXGIFactory2, we just need to do CreateSwapChain (rather than CreateSwapChainForHwnd etc) without it which is currently not implemented
                ThrowHelper.ThrowPlatformNotSupportedException("Platform does not support IDXGIFactory2, which is required");
            }

            Guard.ThrowIfFailed(hr);

            return factory.Move();
        }

        private static DXGI_SWAP_CHAIN_DESC1 CreateDesc(OutputConfiguration desc, Size outputArea)
        {
            if (desc.BackBufferCount > BackBufferBuffer8.MaxBufferCount)
            {
                ThrowHelper.ThrowArgumentException($"Cannot have more than {BackBufferBuffer8.MaxBufferCount} back buffers");
            }

            return new DXGI_SWAP_CHAIN_DESC1
            {
                AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE, // TODO document
                BufferCount = desc.BackBufferCount,
                BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT, // this is the output chain
                Flags = 0,
                Format = (DXGI_FORMAT)desc.BackBufferFormat,
                Height = (uint)outputArea.Height,
                Width = (uint)outputArea.Width,
                SampleDesc = new DXGI_SAMPLE_DESC(count: 1, quality: 0), // backbuffer MSAA is not supported in D3D12
                Scaling = DXGI_SCALING.DXGI_SCALING_NONE,
                Stereo =  FALSE, // stereoscopic rendering, 2 images, e.g VR or 3D holo
                SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD
            };
        }

        /// <summary>
        /// Presents the current back buffer to the output, and advances to the next back buffer
        /// </summary>
        public void Present()
        {
            Guard.ThrowIfFailed(_swapChain.Get()->Present(_desc.SyncInterval, 0));
            _backBufferIndex = (_backBufferIndex + 1) % _desc.BackBufferCount;
        }

        internal void ResizeBuffers(Size newSize)
        {
            for (var i = 0U; i < BackBufferBuffer8.MaxBufferCount; i++)
            {
                _backBuffers[i].Dispose();
            }

            Guard.ThrowIfFailed(_swapChain.Get()->ResizeBuffers(
                   0, // preserve existing number
                   (uint)newSize.Width,
                   (uint)newSize.Height,
                   DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, // preserve existing format
                   0
            ));
        }

        /// <inheritdoc cref="IDisposable"/>
        public void Dispose() => _swapChain.Dispose();
    }
}