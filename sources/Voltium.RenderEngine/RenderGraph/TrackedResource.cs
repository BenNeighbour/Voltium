using System;
using System.Collections.Generic;
using Microsoft.Toolkit.HighPerformance.Extensions;
using Voltium.Common;
using Voltium.Core;
using Voltium.Core.Contexts;
using Voltium.Core.Memory;

namespace Voltium.RenderEngine
{
    internal struct TrackedResource
    {
        public const int NoWritePass = -1;

        public ResourceDesc Desc;

        public bool HasWritePass => LastWritePassIndex != NoWritePass;
        public bool HasReadPass => (LastReadPassIndices?.Count ?? 0) != 0;

        public int LastWritePassIndex;
        public List<int> LastReadPassIndices;

        public ResourceState CurrentTrackedState;

        public ResourceBarrier CreateTransition(ResourceState state, ResourceBarrierOptions options)
        {
            ResourceBarrier barrier;
            if (Desc.Type == ResourceType.Buffer)
            {
                barrier = ResourceBarrier.Transition(Desc.Buffer, CurrentTrackedState, state, options);
            }
            else
            {
                barrier = ResourceBarrier.Transition(Desc.Texture, CurrentTrackedState, state, uint.MaxValue, options);
            }

            CurrentTrackedState = state;
            return barrier;
        }

        public ResourceBarrier CreateUav(ResourceBarrierOptions options)
        {
            if (Desc.Type == ResourceType.Buffer)
            {
                return ResourceBarrier.UnorderedAccess(Desc.Buffer, options);
            }
            else
            {
                return ResourceBarrier.UnorderedAccess(Desc.Texture, options);
            }
        }


        public void AllocateFrom(GraphicsAllocator allocator)
        {
            if (Desc.Type == ResourceType.Buffer)
            {
                Desc.Buffer = allocator.AllocateBuffer(Desc.BufferDesc, Desc.MemoryAccess);
            }
            else
            {
                Desc.Texture = allocator.AllocateTexture(Desc.TextureDesc, Desc.InitialState);
            }
        }

        public void Dispose(in GpuTask free = default)
        {
            if (Desc.Type == ResourceType.Buffer)
            {
                Desc.Buffer.Dispose(free);
            }
            else
            {
                Desc.Texture.Dispose(free);
            }
        }

        internal struct ResourceDependency
        {
            public ResourceState RequiredState;
            public uint ResourceHandle;
        }

        internal void SetName()
        {
            if (Desc.DebugName is null)
            {
                return;
            }

            if (Desc.Type == ResourceType.Buffer)
            {
                Desc.Buffer.SetName(Desc.DebugName);
            }
            else
            {
                Desc.Texture.SetName(Desc.DebugName);
            }
        }
    }
}
