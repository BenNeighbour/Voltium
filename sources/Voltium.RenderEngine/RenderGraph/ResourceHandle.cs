namespace Voltium.RenderEngine
{
    internal readonly struct ResourceHandle
    {
        // index into the graph's list of resources
        internal readonly uint Index;

        internal ResourceHandle(uint index)
        {
            Index = index;
        }

        internal bool IsInvalid => Index == 0;
        internal BufferHandle AsBufferHandle() => new BufferHandle(Index);
        internal TextureHandle AsTextureHandle() => new TextureHandle(Index);
    }
}
