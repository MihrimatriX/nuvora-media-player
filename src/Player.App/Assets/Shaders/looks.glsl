// Nuvora colour & cinema filters. Values come from ColorLooks.ShaderOptions through glsl-shader-opts; changing them
// updates uniforms only, nothing is recompiled. Runs at MAIN (RGB, before linearization and scaling), so it costs one
// cheap pass at the video's own resolution.

//!PARAM split
//!TYPE float
//!MINIMUM 0.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_curve
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_m0
//!TYPE float
//!MINIMUM -4.0
//!MAXIMUM 4.0
1.0

//!PARAM lk_m1
//!TYPE float
//!MINIMUM -4.0
//!MAXIMUM 4.0
0.0

//!PARAM lk_m2
//!TYPE float
//!MINIMUM -4.0
//!MAXIMUM 4.0
0.0

//!PARAM lk_m3
//!TYPE float
//!MINIMUM -4.0
//!MAXIMUM 4.0
0.0

//!PARAM lk_m4
//!TYPE float
//!MINIMUM -4.0
//!MAXIMUM 4.0
1.0

//!PARAM lk_m5
//!TYPE float
//!MINIMUM -4.0
//!MAXIMUM 4.0
0.0

//!PARAM lk_m6
//!TYPE float
//!MINIMUM -4.0
//!MAXIMUM 4.0
0.0

//!PARAM lk_m7
//!TYPE float
//!MINIMUM -4.0
//!MAXIMUM 4.0
0.0

//!PARAM lk_m8
//!TYPE float
//!MINIMUM -4.0
//!MAXIMUM 4.0
1.0

//!PARAM lk_o0
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_o1
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_o2
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_s0
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_s1
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_s2
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_h0
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_h1
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_h2
//!TYPE float
//!MINIMUM -1.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_grain
//!TYPE float
//!MINIMUM 0.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_vig
//!TYPE float
//!MINIMUM 0.0
//!MAXIMUM 1.0
0.0

//!PARAM lk_bars
//!TYPE float
//!MINIMUM 0.0
//!MAXIMUM 0.45
0.0

//!HOOK MAIN
//!BIND HOOKED
//!DESC Nuvora color & cinema filters

const vec3 LUMA_W = vec3(0.2126, 0.7152, 0.0722);

// Dave Hoskins' hash: stable on every GPU, unlike sin()-based ones at large coordinates.
float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

vec4 hook() {
    vec4 color = HOOKED_tex(HOOKED_pos);
    vec2 pos = HOOKED_pos;

    // Before/after comparison: the left side stays untouched.
    if (pos.x < split)
        return color;

    // Cinemascope bars.
    if (pos.y < lk_bars || pos.y > 1.0 - lk_bars)
        return vec4(0.0, 0.0, 0.0, color.a);

    vec3 c = clamp(color.rgb, 0.0, 1.0);

    // 1. S-curve contrast (negative values flatten).
    c = mix(c, c * c * (3.0 - 2.0 * c), lk_curve);

    // 2. Channel matrix + offset: saturation, tint, sepia, black & white, faded blacks.
    mat3 m = mat3(lk_m0, lk_m3, lk_m6,
                  lk_m1, lk_m4, lk_m7,
                  lk_m2, lk_m5, lk_m8);
    c = m * c + vec3(lk_o0, lk_o1, lk_o2);

    // 3. Split toning, luma-neutral so it shifts colour without changing brightness.
    float l = dot(clamp(c, 0.0, 1.0), LUMA_W);
    vec3 t = vec3(lk_s0, lk_s1, lk_s2) * (1.0 - l) * (1.0 - l) + vec3(lk_h0, lk_h1, lk_h2) * l * l;
    c += t - dot(t, LUMA_W);

    // 4. Vignette: an ellipse that follows the frame, 1.0 in the corners.
    if (lk_vig > 0.0) {
        float r = length((pos - 0.5) * 2.0) * 0.70710678;
        c *= 1.0 - lk_vig * 0.85 * smoothstep(0.35, 1.0, r);
    }

    // 5. Film grain: new every frame, strongest in the midtones like real stock.
    if (lk_grain > 0.0) {
        vec2 px = pos * HOOKED_size;
        vec2 seed = vec2(random * 4096.0, fract(random * 7.31) * 4096.0);
        float n = hash12(px + seed) + hash12(px.yx + seed.yx + 17.0) - 1.0;
        float lum = dot(clamp(c, 0.0, 1.0), LUMA_W);
        c += n * lk_grain * 0.14 * (0.35 + 2.6 * lum * (1.0 - lum));
    }

    return vec4(clamp(c, 0.0, 1.0), color.a);
}
