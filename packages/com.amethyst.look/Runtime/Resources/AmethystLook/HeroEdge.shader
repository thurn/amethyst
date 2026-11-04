// The edge hero (HeroLook.cs): Playlist Survivors' HeroEdge as its neon edge trailer drew the knight, without
// the beat and the crystal blade. The crowd's grammar on the hero's light rung: smoked glass a little lit in
// white (where a monster's is black), a strong fresnel rim and lines on the knight's feature edges, thicker
// than a monster's, and a core of light in its chest seen through the glass (_CorePos, which HeroLook keeps on the chest bone), so the small figure at the
// trailer's framing is a bright cut-crystal knight among the swarm's coloured outlines. Drawn on the style
// meshes HeroLook.Dress gives each renderer (barycentrics plus a random value per triangle in TEXCOORD3, the
// feature edges in TEXCOORD5), skinned by Unity.
Shader "Amethyst/HeroEdge"
{
    Properties
    {
        [HDR] _Body ("The hero's white (linear)", Color) = (0.9, 0.94, 1, 1)
        [HDR] _EmissionColor ("Added light (a hurt flash)", Color) = (0, 0, 0, 1)
        _EdgePx ("Line width (px at 2160 rows)", Float) = 1.8
        _Glass ("Glass light", Float) = 0.03
        _Rim ("Rim gain", Float) = 0.6
        _RimPower ("Rim falloff", Float) = 3.0
        _Edge ("Line gain", Float) = 1.0
        _CoreGlow ("Chest core gain", Float) = 0.35
        _CoreSize ("Chest core radius (m)", Float) = 0.22
        _Level ("Light level: the trailer's hero took out the scene exposure for its tuned 0.8 stops", Float) = 1.87
        _CorePos ("Chest core position (world, set each frame by HeroLook)", Vector) = (0, 1.15, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Body; half4 _EmissionColor;
                half _EdgePx; half _Glass; half _Rim; half _RimPower; half _Edge;
                half _CoreGlow; half _CoreSize; half _Level; float4 _CorePos;
            CBUFFER_END
            #include "Packages/com.amethyst.look/Runtime/Shaders/EdgeLines.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 bary : TEXCOORD3;
                float3 edges : TEXCOORD5;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 bary : TEXCOORD2;
                nointerpolation float3 edges : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.bary = v.bary;
                o.edges = v.edges;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 facet = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(facet, V) < 0) facet = -facet;
                float3 N = normalize(lerp(normalize(i.normalWS), facet, 0.6));
                float nv = saturate(dot(N, V));
                float3 body = _Body.rgb;
                // The glass: each facet a shade apart (the triangle's random), lit a little, never black.
                float3 color = body * _Glass * (0.6 + 0.8 * i.bary.w);
                color += body * _Rim * pow(1 - nv, _RimPower);
                float lit = EdgeLine(i.bary.xyz, i.edges, _EdgePx);
                color += body * _Edge * lit * (0.75 + 0.25 * (1 - nv));
                // The chest's core: where the view ray passes near the chest.
                float d = length(cross(_CorePos.xyz - _WorldSpaceCameraPos, -V));
                float r = max(_CoreSize, 1e-3);
                color += body * exp(-(d * d) / (r * r)) * _CoreGlow;
                return half4(color * _Level + _EmissionColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
