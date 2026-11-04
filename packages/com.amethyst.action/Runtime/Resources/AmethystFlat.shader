// Flat unlit colour for the fight's ground shapes and bars (Flat.cs): the swing crescents, spawn markers,
// telegraphs, armor bars. Alpha blended, no depth writes, so a later queue draws over an earlier one where they
// overlap: enemy telegraphs (a later queue) draw above the hero's effects, and with _ZTest Always above the
// crowd's figures too. No keywords.
Shader "Amethyst/Flat"
{
    Properties
    {
        [HDR] _Color ("Colour (linear, alpha)", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Flat"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            float4 vert(float3 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            half4 frag() : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
