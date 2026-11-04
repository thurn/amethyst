// The kill shatter's chunks (MonsterShatter.cs): Playlist Survivors' MonsterEdgeShard, which drew the neon edge
// trailer's shatter, without the beat. A type's chunks, one instance per death, skinned from the palette frozen on
// the break and moved rigidly with each chunk, in the bodies' glass (MonsterEdge.shader). Every chunk is outlined
// where it broke (the shard baker's crack edges in TEXCOORD5) as well as on the model's feature edges, so a body
// shatters into edge-lit glass in its hue, its weapon with it. The outlines are hottest on the break's frame, fading
// over FRESH_SECONDS, and the set flashes white for FLASH_SECONDS. Opaque, in the geometry queue.
Shader "Amethyst/MonsterEdgeShard"
{
    Properties
    {
        [HDR] _Tint ("Hue (linear, levelled)", Color) = (0.1, 0.8, 1.0, 1)
        _Core ("Glass (linear)", Color) = (0.002, 0.002, 0.004, 1)
        [HDR] _Flash ("Flash (linear)", Color) = (1, 1, 1, 1)
        _Rim ("Rim gain", Float) = 2.0
        _RimPower ("Rim falloff", Float) = 4.0
        _Edge ("Edge gain", Float) = 1.4
        _EdgePx ("Edge width (px at 2160 rows)", Float) = 1.25
        _Fresh ("Edge lift when fresh", Float) = 0.6
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
                half4 _Tint; half4 _Core; half4 _Flash;
                half _Rim; half _RimPower; half _Edge; half _EdgePx; half _Fresh;
            CBUFFER_END
            #include "Packages/com.amethyst.look/Runtime/Shaders/CrowdCommon.hlsl"
            #include "Packages/com.amethyst.look/Runtime/Shaders/EdgeLines.hlsl"
            #include "Packages/com.amethyst.look/Runtime/Shaders/MonsterShards.hlsl"

            StructuredBuffer<Death> _Deaths;
            StructuredBuffer<Shard> _Shards;
            uint _ShardCount, _RingStart, _RingCapacity;
            float _Now;

            #define FLASH_SECONDS 0.12
            #define FRESH_SECONDS 0.6
            #define FLASH_GAIN 0.5
            #define SHRINK_SECONDS 0.1

            // The joined chunks (MonsterShardBaker): the style mesh's attributes, the chunk and the edges.
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 boneIndex : TEXCOORD1;
                float4 boneWeight : TEXCOORD2;
                float4 bary : TEXCOORD3;
                float2 chunk : TEXCOORD4; // x 1 on an inside face, y the chunk's index
                float3 edges : TEXCOORD5;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2; // x flash, y inside, z fresh
                nointerpolation float3 tint : TEXCOORD3;
                float4 bary : TEXCOORD4;
                nointerpolation float3 edges : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                InitIndirectDrawArgs(0);
                uint slot = (_RingStart + GetIndirectInstanceID_Base(v.instanceID)) % _RingCapacity;
                Death d = _Deaths[slot];
                Shard s = _Shards[slot * _ShardCount + (uint)v.chunk.y];
                MonsterInstance m = (MonsterInstance)0; // not alive: no motion layers
                m.position = d.position;
                m.yaw = d.yaw;
                m.scale = d.scale;
                float3 p, n;
                SkinVertex(m, slot, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, p, n);
                Varyings o;
                // A chunk contracts to its pivot over its last SHRINK_SECONDS, gone before it lands.
                float shrink = saturate((s.vanishAt - _Now) / SHRINK_SECONDS);
                o.positionWS = s.position + Rotate(s.rotation, p - s.origin) * shrink;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = Rotate(s.rotation, n);
                float age = _Now - d.time;
                float fresh = saturate(1 - age / FRESH_SECONDS);
                o.fx = float4(saturate(1 - age / FLASH_SECONDS), v.chunk.x, fresh * fresh, 0);
                o.tint = d.tint != 0 ? TintOf(d.tint) : _Tint.rgb;
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
                // An inside face looks into the break: no rim.
                float fres = pow(1 - nv, _RimPower) * (1 - i.fx.y);
                float3 color = _Core.rgb * (0.5 + i.bary.w);
                color += i.tint * _Rim * fres;
                float lit = EdgeLine(i.bary.xyz, i.edges, _EdgePx);
                color += i.tint * _Edge * lit * (0.65 + 0.35 * (1 - nv)) * (1 + _Fresh * 2.0 * i.fx.z);
                color += _Flash.rgb * i.fx.x * FLASH_GAIN * (2.0 * lit + fres + 0.35);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
