#include "PixelFrag.hlsli"
#include "../Constants.hlsli"

#define SCENE_LIGHTS 2

struct Lights
{
    DirectionalLight Lights[SCENE_LIGHTS];
};

ConstantBuffer<Lights> SceneLight : register(b2);

float4 main(PixelFrag frag) : SV_TARGET
{
    frag.Normal = normalize(frag.Normal);

    float3 eye = normalize(Frame.CameraPosition - (float3) frag.WorldPosition);
    
    float4 ambient = Frame.AmbientLight * Object.Material.DiffuseAlbedo;

    float3 shadowFactor = 1.0f;

    float3 directLight = 0;
    
    for (int i = 0; i < SCENE_LIGHTS; i++)
    {
        directLight += ComputeDirectionalLight(
            SceneLight.Lights[i],
            Object.Material,
            eye,
            frag.Normal,
            shadowFactor
        );
    }

    float4 color = ambient + float4(directLight, 0);

    color.a = Object.Material.DiffuseAlbedo.a;

    return color;
}
