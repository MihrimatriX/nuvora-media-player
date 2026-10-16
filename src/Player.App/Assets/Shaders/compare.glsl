//!PARAM split
//!TYPE float
//!MINIMUM 0.0
//!MAXIMUM 1.0
0.5

//!PARAM sharp
//!TYPE float
//!MINIMUM -2.0
//!MAXIMUM 4.0
0.45

//!PARAM cont
//!TYPE float
//!MINIMUM -100.0
//!MAXIMUM 100.0
0.0

//!PARAM sat
//!TYPE float
//!MINIMUM -100.0
//!MAXIMUM 100.0
0.0

//!PARAM bright
//!TYPE float
//!MINIMUM -100.0
//!MAXIMUM 100.0
0.0

//!PARAM gam
//!TYPE float
//!MINIMUM -100.0
//!MAXIMUM 100.0
0.0

//!PARAM hue_deg
//!TYPE float
//!MINIMUM -100.0
//!MAXIMUM 100.0
0.0

//!PARAM preset_boost
//!TYPE float
//!MINIMUM 0.0
//!MAXIMUM 1.0
0.35

//!HOOK OUTPUT
//!BIND HOOKED
//!DESC Nuvora Split-Screen Before/After Comparison

vec4 hook() {
    vec2 pos = HOOKED_pos;
    vec4 center = HOOKED_tex(pos);

    // Left of the split bar: untouched original ("ÖNCE · Orijinal")
    if (pos.x < split) {
        if (preset_boost > 0.01) {
            vec3 b = (HOOKED_texOff(vec2(-0.6, 0.0)).rgb +
                      HOOKED_texOff(vec2( 0.6, 0.0)).rgb +
                      HOOKED_texOff(vec2( 0.0,-0.6)).rgb +
                      HOOKED_texOff(vec2( 0.0, 0.6)).rgb) * 0.25;
            return vec4(mix(center.rgb, b, 0.25 * preset_boost), center.a);
        }
        return center;
    }

    // Right of the split bar: enhanced image ("SONRA · İyileştirilmiş")
    vec3 rgb = center.rgb;

    // 1. Contrast Adaptive Sharpening (CAS) + Detail Recovery
    if (abs(sharp) > 0.001) {
        vec3 n = HOOKED_texOff(vec2( 0.0, -1.0)).rgb;
        vec3 s = HOOKED_texOff(vec2( 0.0,  1.0)).rgb;
        vec3 w = HOOKED_texOff(vec2(-1.0,  0.0)).rgb;
        vec3 e = HOOKED_texOff(vec2( 1.0,  0.0)).rgb;
        vec3 nw = HOOKED_texOff(vec2(-1.0, -1.0)).rgb;
        vec3 ne = HOOKED_texOff(vec2( 1.0, -1.0)).rgb;
        vec3 sw = HOOKED_texOff(vec2(-1.0,  1.0)).rgb;
        vec3 se = HOOKED_texOff(vec2( 1.0,  1.0)).rgb;

        vec3 minRgb = min(min(min(n, s), min(w, e)), rgb);
        vec3 maxRgb = max(max(max(n, s), max(w, e)), rgb);
        vec3 amp = clamp(min(minRgb, 1.0 - maxRgb) / max(maxRgb, vec3(0.0001)), 0.0, 1.0);
        amp = sqrt(amp);

        vec3 blur = (n + s + w + e) * 0.15 + (nw + ne + sw + se) * 0.10;
        vec3 diff = rgb - blur;
        rgb = clamp(rgb + diff * sharp * (0.55 + 0.45 * amp), 0.0, 1.0);
    }

    // 2. Subtle micro-contrast & clarity from active preset
    if (preset_boost > 0.01) {
        rgb = clamp((rgb - 0.5) * (1.0 + 0.04 * preset_boost) + 0.5, 0.0, 1.0);
    }

    // 3. Brightness (-50..+50)
    if (abs(bright) > 0.01) {
        rgb += bright * 0.005;
    }

    // 4. Contrast (-50..+50)
    if (abs(cont) > 0.01) {
        float cFactor = pow(2.0, cont * 0.02);
        rgb = (rgb - 0.5) * cFactor + 0.5;
    }

    // 5. Gamma (-50..+50)
    if (abs(gam) > 0.01) {
        float gFactor = pow(2.0, -gam * 0.018);
        rgb = pow(max(rgb, vec3(0.0)), vec3(gFactor));
    }

    // 6. Saturation (-50..+50) & subtle preset vibrance
    float effSat = sat + preset_boost * 4.0;
    if (abs(effSat) > 0.01) {
        float luma = dot(rgb, vec3(0.2126, 0.7152, 0.0722));
        float sFactor = max(0.0, 1.0 + effSat * 0.02);
        rgb = mix(vec3(luma), rgb, sFactor);
    }

    // 7. Hue rotation (-50..+50)
    if (abs(hue_deg) > 0.01) {
        float angle = hue_deg * 0.0314159265;
        float cosA = cos(angle);
        float sinA = sin(angle);
        vec3 k = vec3(0.577350269);
        rgb = rgb * cosA + cross(k, rgb) * sinA + k * dot(k, rgb) * (1.0 - cosA);
    }

    return vec4(clamp(rgb, 0.0, 1.0), center.a);
}

