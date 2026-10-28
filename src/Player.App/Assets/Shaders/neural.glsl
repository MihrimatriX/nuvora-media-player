// Nuvora "Neural Ultra": a DLSS 5-style structure & tone reconstruction pass. NOT NVIDIA DLSS and not affiliated with
// NVIDIA. DLSS 5 relights game frames with engine data (geometry, motion vectors); a video player has none of that,
// so this pass borrows only its two artist controls, Structure and Tone intensity, and its idea of treating skin and
// foliage differently, with plain colour masks instead of a neural network.
//
// Runs at OUTPUT (display resolution, after the upscaler: RTX VSR on RTX cards, ArtCNN / FSRCNNX elsewhere).
// 13 texture reads per pixel; the parameters are uniforms, so the sliders never recompile anything.
// The sharpening limiter follows the RCAS idea from AMD FidelityFX FSR 1 (MIT): the negative lobe is capped so the
// result can never leave the local min/max, which is what keeps it free of halos.

//!PARAM split
//!TYPE float
//!MINIMUM 0.0
//!MAXIMUM 1.0
0.0

//!PARAM nu_structure
//!TYPE float
//!MINIMUM 0.0
//!MAXIMUM 1.0
0.6

//!PARAM nu_tone
//!TYPE float
//!MINIMUM 0.0
//!MAXIMUM 1.0
0.5

//!HOOK OUTPUT
//!BIND HOOKED
//!DESC Nuvora Neural Ultra (DLSS 5-style structure & tone)

const vec3 LUMA_W = vec3(0.2126, 0.7152, 0.0722);

float luma(vec3 c) {
    return dot(c, LUMA_W);
}

vec4 hook() {
    vec4 color = HOOKED_tex(HOOKED_pos);

    // Before/after comparison: the left side stays untouched.
    if (HOOKED_pos.x < split)
        return color;

    vec3 c = color.rgb;
    vec3 n = HOOKED_texOff(vec2( 0.0, -1.0)).rgb;
    vec3 s = HOOKED_texOff(vec2( 0.0,  1.0)).rgb;
    vec3 w = HOOKED_texOff(vec2(-1.0,  0.0)).rgb;
    vec3 e = HOOKED_texOff(vec2( 1.0,  0.0)).rgb;

    // A wider ring: the local "base" the detail sits on, for clarity and for the skin mask's softening.
    vec3 ring = (HOOKED_texOff(vec2(-3.0,  0.0)).rgb + HOOKED_texOff(vec2( 3.0,  0.0)).rgb
               + HOOKED_texOff(vec2( 0.0, -3.0)).rgb + HOOKED_texOff(vec2( 0.0,  3.0)).rgb
               + HOOKED_texOff(vec2(-2.0, -2.0)).rgb + HOOKED_texOff(vec2( 2.0, -2.0)).rgb
               + HOOKED_texOff(vec2(-2.0,  2.0)).rgb + HOOKED_texOff(vec2( 2.0,  2.0)).rgb) * 0.125;

    // Simple semantic masks (YCbCr skin range, green-dominant foliage), standing in for DLSS 5's AI masks.
    float l0 = luma(c);
    float cb = (c.b - l0) / 1.8556;
    float cr = (c.r - l0) / 1.5748;
    float skin = smoothstep(0.02, 0.06, cr) * (1.0 - smoothstep(0.16, 0.22, cr))
               * smoothstep(-0.16, -0.10, cb) * (1.0 - smoothstep(-0.01, 0.03, cb))
               * smoothstep(0.12, 0.25, l0);
    float foliage = smoothstep(0.02, 0.10, c.g - max(c.r, c.b));

    // ---- Structure ----
    // 1. Contrast-adaptive sharpening, capped so it cannot overshoot the 3x3 cross (no halos).
    vec3 mn4 = min(min(n, s), min(w, e));
    vec3 mx4 = max(max(n, s), max(w, e));
    vec3 hitMin = min(mn4, c) / (4.0 * mx4 + 1e-5);
    vec3 hitMax = (1.0 - max(mx4, c)) / (4.0 * mn4 - 4.0 - 1e-5);
    vec3 lobe3 = max(-hitMin, hitMax);
    float lobe = max(-0.1875, min(max(lobe3.r, max(lobe3.g, lobe3.b)), 0.0));
    // A pixel that stands out from flat neighbours is noise or grain, not detail: sharpen it less.
    float ln = luma(n), ls = luma(s), lw = luma(w), le = luma(e);
    float range = max(max(max(ln, ls), max(lw, le)), l0) - min(min(min(ln, ls), min(lw, le)), l0);
    float noise = clamp(abs(0.25 * (ln + ls + lw + le) - l0) / max(range, 1e-4), 0.0, 1.0);
    lobe *= (1.0 - 0.5 * noise) * nu_structure * (1.0 - 0.7 * skin);
    c = (lobe * (n + s + w + e) + c) / (4.0 * lobe + 1.0);

    // 2. Clarity: micro-contrast against the ring, only where there is texture. Darkens crevices (an AO-like depth)
    //    and lifts ridges; foliage gets a little more, skin almost none.
    float textured = smoothstep(0.015, 0.10, range);
    float detail = clamp(luma(c) - luma(ring), -0.08, 0.08);
    c += detail * nu_structure * textured * (0.9 + 0.5 * foliage) * (1.0 - 0.8 * skin);

    // 3. Skin: a touch of the softness and warmth light picks up under skin (subsurface scattering).
    c = mix(c, mix(c, ring, 0.35) + vec3(0.012, 0.004, -0.008), skin * nu_tone * 0.8);

    // ---- Tone ----
    // 4. Relight: filmic S-curve on luma with slightly opened shadows, carried to RGB additively (keeps hue).
    vec3 cc = clamp(c, 0.0, 1.0);
    float l = luma(cc);
    float lt = mix(l, l * l * (3.0 - 2.0 * l), 0.3 * nu_tone) + 0.035 * nu_tone * pow(1.0 - l, 4.0);
    c = cc + (lt - l);

    // 5. Vibrance: dull colours gain more than vivid ones; skin is protected, foliage favoured.
    float sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
    float vib = 0.35 * nu_tone * (1.0 - clamp(sat, 0.0, 1.0)) * (1.0 - 0.7 * skin) * (1.0 + 0.4 * foliage);
    c = mix(vec3(luma(c)), c, 1.0 + vib);

    return vec4(clamp(c, 0.0, 1.0), color.a);
}
