// Pixel-width lines on a style mesh's triangle edges, from its barycentrics (TEXCOORD3) and the feature
// edges it draws (TEXCOORD5): Playlist Survivors' MonsterLines.hlsl as the neon edge trailer drew with it.
#ifndef AMETHYST_EDGE_LINES_INCLUDED
#define AMETHYST_EDGE_LINES_INCLUDED

// How lit a feature edge is at this pixel: 1 on a flagged edge, falling off over a pixel at px from it. A
// barycentric's screen derivative makes the width the same in pixels at any distance, as a neon tube's
// would be. A triangle only a few pixels across fades its lines, so small detail doesn't turn the body
// into a tangle of wire. The trailer rendered 2160 rows and scaled them to 1080, so every measure here is
// in pixels at 2160 rows: a frame drawn at 1080 rows draws the lines as the trailer's frames show them.
float EdgeLine(float3 bary, float3 edges, float px)
{
    float rows = _ScaledScreenParams.y / 2160.0;
    float3 fw = max(fwidth(bary), 1e-5);
    float3 w = bary / fw;
    px *= rows;
    float3 on = (1 - smoothstep(px - 0.75 * rows, px + 0.75 * rows, w)) * edges;
    float size = 1 / (max(fw.x, max(fw.y, fw.z)) * rows);
    return max(on.x, max(on.y, on.z)) * lerp(0.35, 1, saturate((size - 1.5) / 4));
}

#endif
