// The edge look's crowd figures (EdgeLook.cs): Playlist Survivors' MonsterEdge as its neon edge trailer
// drew them, without the beat. The crowd's style mesh skinned from the palette (CrowdCommon.hlsl), drawn as
// near-black glass lit only by the type's hue in a fresnel rim, in lines on the model's feature edges and in a
// small inner core seen through the glass. A figure's flare (a telegraph) lifts its lines and core; a hit
// lights what is lit toward white. Two touching figures show a seam because each one's rim and lines are lit
// and the space between them is its dark core. Status overlays (StatusOverlays.cs) add the hero's white:
// sparks running over the lines, frost facets, a rim. Opaque, in the geometry queue.
Shader "Amethyst/MonsterEdge"
{
    Properties
    {
        [HDR] _Tint ("Hue (linear, levelled)", Color) = (0.1, 0.8, 1.0, 1)
        _Core ("Glass (linear)", Color) = (0.002, 0.002, 0.004, 1)
        [HDR] _Flash ("Hit flash (linear)", Color) = (1, 1, 1, 1)
        [HDR] _Status ("Status overlays: the hero's white (linear)", Color) = (0.9, 0.94, 1, 1)
        _Rim ("Rim gain", Float) = 2.0
        _RimPower ("Rim falloff", Float) = 4.0
        _Edge ("Edge gain", Float) = 2.6
        _EdgePx ("Edge width (px at 2160 rows)", Float) = 1.25
        _CoreGlow ("Core at rest", Float) = 0.1
        _CoreFlare ("Core flared", Float) = 0.35
        _CoreSize ("Core radius (share of the body's)", Float) = 0.18
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
                half4 _Tint; half4 _Core; half4 _Flash; half4 _Status;
                half _Rim; half _RimPower; half _Edge; half _EdgePx;
                half _CoreGlow; half _CoreFlare; half _CoreSize;
            CBUFFER_END
            #include "Packages/com.amethyst.look/Runtime/Shaders/CrowdCommon.hlsl"
            #include "Packages/com.amethyst.look/Runtime/Shaders/EdgeLines.hlsl"

            #define OVERLAY_SPARKS 1u
            #define OVERLAY_FROST 2u
            #define OVERLAY_RING 4u
            #define HIT_GAIN 0.35

            // The crowd's style mesh (StyleMesh.Unweld): every triangle its own vertices, bone data in
            // TEXCOORD1/2, barycentrics plus a random value per triangle in TEXCOORD3, the triangle's feature
            // edges in TEXCOORD5.
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 boneIndex : TEXCOORD1;
                float4 boneWeight : TEXCOORD2;
                float4 bary : TEXCOORD3;
                float3 edges : TEXCOORD5;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2; // x hit, y flare (living only), zw the LOD fade
                nointerpolation float3 tint : TEXCOORD3;
                float4 bary : TEXCOORD4;
                nointerpolation float3 edges : TEXCOORD5;
                nointerpolation float4 core : TEXCOORD6; // the body's bounding sphere: centre (world), radius
                nointerpolation float2 overlay : TEXCOORD7; // x the overlay bits, y their age (s)
            };

            Varyings vert(Attributes v)
            {
                uint slot = CrowdSlot(v.instanceID);
                MonsterInstance m = _Instances[slot];
                Varyings o;
                SkinVertex(m, slot, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, o.positionWS, o.normalWS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                float alive = (m.flags & CROWD_ALIVE) != 0 ? 1 : 0;
                o.fx = float4(m.hitFlash, alive * m.flare, CrowdLodFade(slot));
                o.tint = m.tint != 0 ? TintOf(m.tint) : _Tint.rgb;
                o.bary = v.bary;
                o.edges = v.edges;
                float height;
                o.core = CrowdSphere(m, height);
                o.overlay = float2(m.overlay, m.overlayAge);
                return o;
            }

            // A stable value in [0, 1) per triangle and salt.
            float TriangleHash(float r, float salt) { return frac(sin(r * 917.13 + salt * 78.233) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                float flare = i.fx.y;
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 facet = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(facet, V) < 0) facet = -facet;
                float3 N = normalize(lerp(normalize(i.normalWS), facet, 0.6));
                float nv = saturate(dot(N, V));
                float fres = pow(1 - nv, _RimPower);
                // The glass: each facet a shade apart (the triangle's random), so a flat face isn't one sheet.
                float3 color = _Core.rgb * (0.5 + i.bary.w);
                color += i.tint * _Rim * fres;
                float lit = EdgeLine(i.bary.xyz, i.edges, _EdgePx);
                // Lines toward the silhouette a little brighter, so a body's outline leads its inner facets.
                color += i.tint * _Edge * lit * (0.65 + 0.35 * (1 - nv)) * (1 + 0.3 * flare);
                // The inner core: a glow round the body's centre where the view ray passes near it.
                float3 ray = -V;
                float d = length(cross(i.core.xyz - _WorldSpaceCameraPos, ray));
                float r = max(i.core.w * _CoreSize, 1e-3);
                float3 hot = lerp(i.tint, float3(1, 1, 1), 0.2 + 0.3 * flare);
                float3 core = hot * exp(-(d * d) / (r * r)) * (_CoreGlow + _CoreFlare * flare);
                color += core;
                color += _Flash.rgb * i.fx.x * HIT_GAIN * (2.0 * lit + fres + 0.5 * Luminance(core));

                uint overlay = (uint)i.overlay.x;
                UNITY_BRANCH if (overlay != 0)
                {
                    float fade = saturate(i.overlay.y / 0.12);
                    float3 white = _Status.rgb;
                    if (overlay & OVERLAY_SPARKS)
                    {
                        // A few lines at a time flicker white, a new few each 1/14 s.
                        float spark = step(0.78, TriangleHash(i.bary.w, floor(_CrowdTime * 14)));
                        color += white * 2.4 * lit * spark * fade;
                    }
                    if (overlay & OVERLAY_FROST)
                    {
                        // About half the faces iced over: a pale white fill, its lines whiter.
                        float iced = step(TriangleHash(i.bary.w, 3.1), 0.5);
                        color = lerp(color, white * (0.22 + 0.2 * TriangleHash(i.bary.w, 5.7)) + white * lit, iced * fade * 0.85);
                    }
                    if (overlay & OVERLAY_RING)
                        color += white * 2.5 * pow(1 - nv, 2.5) * fade;
                }
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
