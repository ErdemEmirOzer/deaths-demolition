// WP-109 (R4, user note 10: "ağaçların da altına girince … karakteri görebileceğimiz şekilde ağaçlar da hafif saydamlaştırılsın
// yapılardaki aynı mantık ile") · the top-down tree-crown reveal. The roofs fade whole (CameraOcclusionFader + OccluderFadeState,
// a material swap per renderer); the crowns cannot: most of them are static-batched library prefabs on one shared cutout material,
// so the decision is made per pixel instead, and it is the roofs' rule — only what stands between the camera and the hero fades.
//
//   cone     from the camera to the focus (the hero, chest height); a crown pixel inside it, in front of the focus and above it
//            (by _DD_TreeRevealShape.w) is screen-door dithered (4 × 4 Bayer): _DD_TreeRevealShape.z of its pixels are dropped at
//            the cone's core, none outside its soft rim (x radius at the focus, y soft edge, both m). A crown that hides nothing
//            is never touched, so leaving the tree brings it back exactly as it was.
//   strength _DD_TreeReveal.w (0 … 1) — TreeCanopyReveal eases it in top-down and out in the other modes, menus and tests
//            (the default 0 leaves every foliage pixel as authored).
//   passes   ForwardLit, DepthOnly and DepthNormals drop the same pixels (a depth prepass must agree with the colour); the
//            ShadowCaster does not: the crown keeps its full shade on the ground under it.
//   cost     a handful of ALU on a cutout crown's pixels; globals, so the SRP Batcher and static batches are unchanged.
#ifndef DD_TREE_REVEAL_INCLUDED
#define DD_TREE_REVEAL_INCLUDED

float4 _DD_TreeReveal;        // xyz focus (world), w strength 0..1
float4 _DD_TreeRevealShape;   // x radius at the focus (m), y soft rim (m), z dropped share at the core, w minimum height over the focus (m)

// 0 … 1: how strongly this world position is revealed (before the dropped-share scale).
float DD_TreeRevealAmount(float3 positionWS)
{
    float strength = _DD_TreeReveal.w;
    if (strength <= 0.0) return 0.0;

    float3 camera = _WorldSpaceCameraPos;
    float3 axis = _DD_TreeReveal.xyz - camera;
    float reach = length(axis);
    if (reach < 1e-3) return 0.0;
    float3 dir = axis / reach;

    float3 v = positionWS - camera;
    float along = dot(v, dir);
    if (along <= 1e-3) return 0.0;

    // In front of the focus (ramping to 0 over its last 0.5 m) and above it — the canopy, not the shrub at the hero's knee.
    float front = 1.0 - smoothstep(reach - 0.5, reach, along);
    float above = smoothstep(_DD_TreeReveal.y + _DD_TreeRevealShape.w, _DD_TreeReveal.y + _DD_TreeRevealShape.w + 0.5, positionWS.y);
    // The cone: the distance off the axis scaled to the focus plane, so the revealed disc keeps one size on screen.
    float off = length(v - dir * along) * (reach / along);
    float radius = max(_DD_TreeRevealShape.x, 1e-3);
    float inside = 1.0 - smoothstep(radius - max(_DD_TreeRevealShape.y, 1e-3), radius, off);
    return strength * front * above * inside;
}

// Drops the revealed share of the pixel (screen-door, 4 × 4 Bayer on the pixel position).
void DD_TreeRevealClip(float3 positionWS, float4 positionCS)
{
    float drop = DD_TreeRevealAmount(positionWS) * saturate(_DD_TreeRevealShape.z);
    if (drop <= 0.0) return;
    uint2 p = (uint2)positionCS.xy & 3u;
    // 4 × 4 Bayer, row-major, (index + 0.5) / 16
    const float bayer[16] = { 0.5, 8.5, 2.5, 10.5, 12.5, 4.5, 14.5, 6.5, 3.5, 11.5, 1.5, 9.5, 15.5, 7.5, 13.5, 5.5 };
    clip(bayer[p.y * 4u + p.x] * (1.0 / 16.0) - drop);
}

#endif
