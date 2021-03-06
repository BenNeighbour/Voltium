struct PixelFrag
{
    float4 Position : SV_POSITION;
    float4 WorldPosition : POSITION; // for lighting
    float3 Normal : NORMAL;
    float4 Color : COLOR;
};
