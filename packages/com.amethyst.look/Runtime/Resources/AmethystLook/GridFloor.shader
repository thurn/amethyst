// The void floor (GridFloor.cs): Playlist Survivors' grid floor without its beat wave and event rings. Black,
// with a quiet emissive grid in the zone key's line colour (the palette's colours are as drawn after the
// grade, which this undoes: Ungraded), every fourth line the major one, the minor lines a share of it.
// Lines thinner than a pixel dim rather than widen, fade with distance from the camera and fade out where a
// cell is under a few pixels, so the far floor is quiet and has no moire. No fill, no fog: light comes from
// things, never from the world.
Shader "Amethyst/GridFloor"
{
    Properties
    {
        _Line ("Zone line colour (linear, as drawn; set by GridFloor)", Vector) = (0, 0.014, 0.13, 1)
        _Gain ("Line brightness", Float) = 1
        _Cell ("Cell (m)", Float) = 1.5
        _Major ("Cells per major line", Float) = 4
        _MinorHalfM ("Minor line half-width (m)", Float) = 0.01
        _MajorHalfM ("Major line half-width (m)", Float) = 0.02
        _MinorShare ("Minor lines' share of the major's light", Float) = 0.35
        _FadeNear ("Lines fade from (m from the camera)", Float) = 30
        _FadeFar ("Lines faded by (m)", Float) = 90
        _FadeTo ("Faded lines' share", Float) = 0.35
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
                float4 _Line;
                float _Gain, _Cell, _Major, _MinorHalfM, _MajorHalfM, _MinorShare, _FadeNear, _FadeFar, _FadeTo;
            CBUFFER_END

            // The grade's exposure, toe knee and power (Grade.cs), to undo for colours given as drawn.
            float4 _AmethystGrade;

            float3 Ungraded(float3 c)
            {
                float m = max(c.r, max(c.g, c.b));
                if (_AmethystGrade.y > 0 && m > 1e-6 && m < _AmethystGrade.y) c *= pow(m / _AmethystGrade.y, 1 / _AmethystGrade.z - 1);
                return _AmethystGrade.x > 0 ? c / _AmethystGrade.x : c;
            }

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // How much of a line of half-width halfW (m) covers this pixel, on a lattice of the given cell.
            float GridLine(float2 p, float cell, float halfW)
            {
                float2 d = abs(frac(p / cell + 0.5) - 0.5) * cell;   // m from the nearest line, per axis
                float2 px = max(fwidth(p), 1e-5);                    // m per pixel
                float2 w = max(halfW, 0.5 * px);                     // at least half a pixel wide
                float2 c = saturate(1 - (d - w) / px);
                c *= saturate(halfW / w);                            // thinner than a pixel: dimmer, not wider
                c *= saturate(2 - 8 * px / cell);                    // a cell under ~4 px fades: no moire
                return max(c.x, c.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.positionWS.xz;
                float minor = GridLine(p, _Cell, _MinorHalfM);
                float major = GridLine(p, _Cell * _Major, _MajorHalfM);
                float far = smoothstep(_FadeNear, _FadeFar, distance(i.positionWS, _WorldSpaceCameraPos));
                float lines = max(_MinorShare * minor, major) * lerp(1, _FadeTo, far);
                return half4(Ungraded(lines * _Gain * _Line.rgb), 1);
            }
            ENDHLSL
        }
    }
}
