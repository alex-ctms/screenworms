// GLSL 3.30 for the whole pipeline. World space = physical pixels of the virtual desktop, y down.
namespace ScreenWorm
{
    static class Shaders
    {
        const string V = "#version 330\n";

        const string CLIP = @"
uniform vec4 uView;
vec4 clip(vec2 wp) { vec2 n = (wp - uView.xy) / uView.zw; return vec4(n.x * 2.0 - 1.0, 1.0 - n.y * 2.0, 0.0, 1.0); }
";
        const string HASH = @"
uint uhash(uint x) { x ^= x >> 16u; x *= 0x7feb352du; x ^= x >> 15u; x *= 0x846ca68bu; x ^= x >> 16u; return x; }
float urnd(uint h) { return float(h & 0xffffffu) / 16777216.0; }
float hash21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float vnoise(vec2 p) {
  vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
  float a = hash21(i), b = hash21(i + vec2(1, 0)), c = hash21(i + vec2(0, 1)), d = hash21(i + vec2(1, 1));
  return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
float fbm(vec2 p) {
  float v = 0.0, a = 0.5; mat2 m = mat2(1.6, 1.2, -1.2, 1.6);
  for (int i = 0; i < 4; i++) { v += a * vnoise(p); p = m * p; a *= 0.5; }
  return v;
}
vec2 curl(vec2 p) {
  float e = 0.05;
  float dx = vnoise(p + vec2(e, 0)) - vnoise(p - vec2(e, 0));
  float dy = vnoise(p + vec2(0, e)) - vnoise(p - vec2(0, e));
  return vec2(dy, -dx) / (2.0 * e);
}
";
        public static readonly string QuadVs = V + @"
layout(location = 0) in vec2 aPos; out vec2 vUv;
void main() { vUv = aPos * 0.5 + 0.5; gl_Position = vec4(aPos, 0.0, 1.0); }";

        public static readonly string BgFs = V + HASH + @"
in vec2 vUv; out vec4 o;
uniform vec4 uView, uWorld;
uniform float uTime, uMem, uCpu;
void main() {
  vec2 wp = uView.xy + vUv * uView.zw;
  vec2 wn = (wp - uWorld.xy) / uWorld.zw;
  vec3 top = vec3(0.010, 0.016, 0.042), bot = vec3(0.003, 0.003, 0.010);
  vec3 col = mix(top, bot, smoothstep(-0.2, 1.1, wn.y));
  float t = uTime * 0.018 * (1.0 + 2.5 * uCpu);
  vec2 q = wp / 1700.0;
  float n1 = fbm(q + vec2(t, -t * 0.7));
  float n2 = fbm(q * 1.9 + vec2(-t * 0.6, t * 0.45) + n1 * 0.9);
  float neb = smoothstep(0.30, 0.95, n2) * (0.30 + 0.70 * uMem);
  vec3 nebA = vec3(0.030, 0.075, 0.190);
  vec3 nebB = vec3(0.170, 0.030, 0.210);
  col += mix(nebA, nebB, clamp(uMem * 1.4 - 0.15, 0.0, 1.0)) * neb * 0.6;
  vec2 g = (fract(wp / 120.0) - 0.5) * 120.0;
  float d = smoothstep(2.0, 0.2, length(g)) * 0.045;
  col += vec3(0.45, 0.65, 1.0) * d;
  o = vec4(col, 1.0);
}";

        public static readonly string StarVs = V + CLIP + @"
layout(location = 0) in vec2 aPos; layout(location = 1) in vec2 aAttr;
uniform float uTime, uDim, uScale;
out float vBright; out vec3 vCol;
void main() {
  gl_Position = clip(aPos);
  float tw = 0.55 + 0.45 * sin(uTime * (0.8 + aAttr.y * 2.5) + aAttr.y * 40.0);
  gl_PointSize = (aAttr.x * 2.2 + 1.5) * uScale;
  vBright = tw * uDim * (0.5 + 0.5 * aAttr.x);
  vCol = mix(vec3(0.65, 0.78, 1.0), vec3(1.0, 0.92, 0.80), fract(aAttr.y * 7.0));
}";
        public static readonly string StarFs = V + @"
in float vBright; in vec3 vCol; out vec4 o;
void main() {
  vec2 d = gl_PointCoord - 0.5; float r = length(d) * 2.0;
  float a = exp(-r * r * 6.0) * (1.0 - smoothstep(0.6, 1.0, r));
  o = vec4(vCol * vBright * a * 0.8, 1.0);
}";

        public static readonly string WormVs = V + CLIP + @"
layout(location = 0) in vec2 aPos; layout(location = 1) in vec2 aNrm; layout(location = 2) in vec2 aSU;
uniform float uLen, uWidth, uTime, uUtil, uPhase;
out vec2 vSU;
float widthAt(float u) {
  float s = clamp(u / uLen, 0.0, 1.0);
  float head = 1.6 * uWidth + 0.04 * uLen;
  float cap = u < head ? sqrt(max(0.0, 1.0 - pow((head - u) / head, 2.0))) : 1.0;
  float taper = 1.0 - smoothstep(0.62, 1.0, s);
  float pulse = 1.0 + 0.10 * sin(u * 0.05 - uTime * (4.0 + 26.0 * uUtil) + uPhase) * (0.25 + uUtil);
  return uWidth * cap * pow(taper, 0.8) * pulse;
}
void main() { vSU = aSU; gl_Position = clip(aPos + aNrm * aSU.x * widthAt(aSU.y)); }";
        public static readonly string WormFs = V + @"
in vec2 vSU; out vec4 o;
uniform vec3 uColor; uniform float uLen, uUtil, uTime, uPhase, uIntensity;
void main() {
  float d = abs(vSU.x);
  float s = clamp(vSU.y / uLen, 0.0, 1.0);
  float core = exp(-d * d * 10.0);
  float glow = pow(1.0 - d, 1.6);
  float fade = 1.0 - smoothstep(0.35, 1.0, s) * 0.85;
  float band = 0.22 + 0.78 * pow(0.5 + 0.5 * sin(vSU.y * 0.085 - uTime * (5.0 + 32.0 * uUtil) + uPhase), 2.4);
  float edge = smoothstep(1.0, 0.88, d);
  vec3 tube = uColor * glow * (0.45 + 0.7 * band);
  vec3 hot = mix(uColor, vec3(1.0), 0.45) * core * (0.22 + 0.28 * band);
  o = vec4((tube + hot) * fade * edge * uIntensity, 1.0);
}";

        public static readonly string HeadVs = V + CLIP + @"
layout(location = 0) in vec2 aPos; layout(location = 1) in vec4 aCol; layout(location = 2) in vec2 aInfo;
out vec4 vCol; out float vUtil;
void main() { gl_Position = clip(aPos); gl_PointSize = aInfo.x; vCol = aCol; vUtil = aInfo.y; }";
        public static readonly string HeadFs = V + @"
in vec4 vCol; in float vUtil; out vec4 o;
void main() {
  vec2 d = (gl_PointCoord - 0.5) * 2.0; float r = length(d);
  float core = exp(-r * r * 16.0);
  float halo = exp(-r * r * 3.5) * 0.30;
  float flare = pow(max(0.0, 1.0 - abs(d.x)), 10.0) * exp(-abs(d.y) * 9.0) + pow(max(0.0, 1.0 - abs(d.y)), 10.0) * exp(-abs(d.x) * 9.0);
  vec3 c = vCol.rgb * (halo + flare * 0.5) + mix(vCol.rgb, vec3(1.0), 0.6) * core * 0.6;
  o = vec4(c * (0.25 + 0.6 * vUtil) * vCol.a, 1.0);
}";

        public static readonly string PartUpdateVs = V + HASH + @"
layout(location = 0) in vec4 aA; layout(location = 1) in vec4 aB;
out vec4 vA; out vec4 vB;
uniform float uDt, uTime, uSpawn;
uniform uint uStep;
uniform int uWormCount, uMoteCount;
uniform vec4 uWorld;
uniform sampler2D uHeads;
void main() {
  int id = gl_VertexID;
  uint h = uhash(uint(id) * 747796405u + uStep * 2891336453u + 0x9E3779B9u);
  vec2 p = aA.xy, v = aA.zw;
  float life = aB.x, maxLife = aB.y, worm = aB.z, seed = aB.w;
  if (id < uMoteCount) {
    if (maxLife == 0.0) { p = uWorld.xy + vec2(urnd(h), urnd(uhash(h + 1u))) * uWorld.zw; maxLife = 1.0; seed = urnd(uhash(h + 2u)); }
    v = curl(p * 0.0012 + seed * 17.0 + uTime * 0.02) * 14.0 + vec2(0.0, -4.0);
    p += v * uDt;
    p = uWorld.xy + mod(p - uWorld.xy, uWorld.zw);
    life = 1.0;
  } else {
    life -= uDt;
    if (life <= 0.0) {
      int w = int(urnd(h) * float(uWormCount));
      vec4 hp = texelFetch(uHeads, ivec2(w, 0), 0);
      vec4 hi = texelFetch(uHeads, ivec2(w, 1), 0);
      float util = hi.x;
      float prob = (pow(util, 1.5) * 0.22 + 0.003) * uSpawn;
      if (urnd(uhash(h + 3u)) < prob) {
        float ang = urnd(uhash(h + 4u)) * 6.2831853;
        float rad = urnd(uhash(h + 5u));
        p = hp.xy + vec2(cos(ang), sin(ang)) * rad * (3.0 + 9.0 * util);
        vec2 dir = hp.zw, perp = vec2(-dir.y, dir.x);
        float sp = (50.0 + 420.0 * util) * (0.25 + 0.75 * urnd(uhash(h + 6u)));
        v = -dir * sp + perp * (urnd(uhash(h + 7u)) - 0.5) * sp * 0.8;
        maxLife = 0.45 + 1.9 * urnd(uhash(h + 8u)); life = maxLife;
        worm = float(w); seed = urnd(uhash(h + 9u)) * 100.0;
      } else { life = -1.0; maxLife = 0.0; }
    } else {
      v += curl(p * 0.0035 + seed + uTime * 0.25) * 50.0 * uDt;
      v *= exp(-uDt * 2.2);
      p += v * uDt;
    }
  }
  vA = vec4(p, v); vB = vec4(life, maxLife, worm, seed);
}";
        public static readonly string PartUpdateFs = V + "out vec4 o; void main() { o = vec4(0.0); }";

        public static readonly string PartVs = V + CLIP + @"
layout(location = 0) in vec4 aA; layout(location = 1) in vec4 aB;
uniform sampler2D uHeads; uniform float uTime, uScale, uMoteVis; uniform int uMoteCount, uMoteShown;
out vec3 vCol; out float vSoft;
void main() {
  int id = gl_VertexID;
  if (id < uMoteCount) {
    if (id >= uMoteShown || aB.y == 0.0) { gl_Position = vec4(2.0, 2.0, 2.0, 1.0); gl_PointSize = 1.0; vCol = vec3(0.0); vSoft = 1.0; return; }
    float tw = 0.5 + 0.5 * sin(uTime * 1.7 + aB.w * 90.0);
    vCol = vec3(0.30, 0.45, 0.80) * (0.25 + 0.75 * tw) * uMoteVis * 0.55;
    gl_PointSize = 2.2 * uScale; vSoft = 1.0;
  } else {
    if (aB.x <= 0.0) { gl_Position = vec4(2.0, 2.0, 2.0, 1.0); gl_PointSize = 1.0; vCol = vec3(0.0); vSoft = 1.0; return; }
    float k = clamp(aB.x / aB.y, 0.0, 1.0);
    vec3 wc = texelFetch(uHeads, ivec2(int(aB.z), 1), 0).yzw;
    vCol = wc * pow(k, 1.3) * 1.1 + vec3(1.0) * pow(k, 7.0) * 0.35;
    gl_PointSize = (1.2 + 2.6 * k) * uScale; vSoft = 0.6;
  }
  gl_Position = clip(aA.xy);
}";
        public static readonly string PartFs = V + @"
in vec3 vCol; in float vSoft; out vec4 o;
void main() {
  vec2 d = gl_PointCoord - 0.5; float r = length(d) * 2.0;
  float a = exp(-r * r * (3.0 + 4.0 * vSoft)) * (1.0 - smoothstep(0.7, 1.0, r));
  o = vec4(vCol * a, 1.0);
}";

        public static readonly string CometVs = V + CLIP + @"
layout(location = 0) in vec2 aPos; layout(location = 1) in vec2 aUv; layout(location = 2) in vec4 aCol;
out vec2 vUv; out vec4 vCol;
void main() { gl_Position = clip(aPos); vUv = aUv; vCol = aCol; }";
        public static readonly string CometFs = V + @"
in vec2 vUv; in vec4 vCol; out vec4 o;
void main() {
  float along = clamp(vUv.x, 0.0, 1.0);
  float across = abs(vUv.y * 2.0 - 1.0);
  float width = mix(1.0, 0.15, along);
  float a = exp(-across * across * (5.0 / (width * width))) * pow(1.0 - along, 2.8);
  float head = exp(-along * 10.0) * exp(-across * across * 6.0);
  o = vec4((vCol.rgb * a + mix(vCol.rgb, vec3(1.0), 0.5) * head * 0.3) * vCol.a, 1.0);
}";

        public static readonly string RingVs = V + CLIP + @"
layout(location = 0) in vec2 aPos; layout(location = 1) in vec2 aUv; layout(location = 2) in vec4 aCol; layout(location = 3) in vec2 aRing;
out vec2 vUv; out vec4 vCol; out vec2 vRing; out float vHalf;
void main() { gl_Position = clip(aPos); vUv = aUv; vCol = aCol; vRing = aRing; vHalf = aRing.x + aRing.y * 3.0; }";
        public static readonly string RingFs = V + @"
in vec2 vUv; in vec4 vCol; in vec2 vRing; in float vHalf; out vec4 o;
void main() {
  float r = length(vUv) * vHalf;
  float d = (r - vRing.x) / vRing.y;
  float a = exp(-d * d * 2.0);
  float inner = smoothstep(vRing.x, vRing.x - vRing.y * 6.0, r) * 0.12;
  o = vec4(vCol.rgb * (a + inner) * vCol.a, 1.0);
}";

        public static readonly string BloomDownFs = V + @"
in vec2 vUv; out vec4 o; uniform sampler2D uTex; uniform vec2 uTexel; uniform float uThreshold; uniform int uFirst;
vec3 fetch(vec2 uv) { return texture(uTex, uv).rgb; }
vec3 knee(vec3 c) {
  if (uFirst == 0) return c;
  float br = max(c.r, max(c.g, c.b));
  float soft = clamp(br - uThreshold + 0.5, 0.0, 1.0); soft = soft * soft / 2.0;
  float contrib = max(soft, br - uThreshold) / max(br, 1e-4);
  return c * contrib;
}
void main() {
  vec2 t = uTexel;
  vec3 a = fetch(vUv + t * vec2(-2, -2)), b = fetch(vUv + t * vec2(0, -2)), c = fetch(vUv + t * vec2(2, -2));
  vec3 d = fetch(vUv + t * vec2(-2, 0)),  e = fetch(vUv),                   f = fetch(vUv + t * vec2(2, 0));
  vec3 g = fetch(vUv + t * vec2(-2, 2)),  h = fetch(vUv + t * vec2(0, 2)),  i = fetch(vUv + t * vec2(2, 2));
  vec3 j = fetch(vUv + t * vec2(-1, -1)), k = fetch(vUv + t * vec2(1, -1)), l = fetch(vUv + t * vec2(-1, 1)), m = fetch(vUv + t * vec2(1, 1));
  vec3 s = e * 0.125 + (a + c + g + i) * 0.03125 + (b + d + f + h) * 0.0625 + (j + k + l + m) * 0.125;
  o = vec4(knee(s), 1.0);
}";
        public static readonly string BloomUpFs = V + @"
in vec2 vUv; out vec4 o; uniform sampler2D uTex, uPrev; uniform vec2 uTexel; uniform float uRadius;
void main() {
  vec2 t = uTexel * uRadius;
  vec3 s = texture(uTex, vUv + t * vec2(-1, -1)).rgb + texture(uTex, vUv + t * vec2(1, -1)).rgb
         + texture(uTex, vUv + t * vec2(-1, 1)).rgb  + texture(uTex, vUv + t * vec2(1, 1)).rgb
         + 2.0 * (texture(uTex, vUv + t * vec2(0, -1)).rgb + texture(uTex, vUv + t * vec2(0, 1)).rgb
         + texture(uTex, vUv + t * vec2(-1, 0)).rgb + texture(uTex, vUv + t * vec2(1, 0)).rgb)
         + 4.0 * texture(uTex, vUv).rgb;
  o = vec4(s / 16.0 + texture(uPrev, vUv).rgb, 1.0);
}";

        public static readonly string CompositeFs = V + @"
in vec2 vUv; out vec4 o;
uniform sampler2D uScene, uBloom;
uniform float uBloomAmt, uExposure, uTime, uAberration, uCrt, uGrain;
vec3 aces(vec3 x) { return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0); }
float gnoise(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void main() {
  vec2 uv = vUv;
  vec2 c = uv - 0.5;
  float r2 = dot(c, c);
  vec2 ab = c * r2 * uAberration;
  vec3 scene = vec3(texture(uScene, uv + ab).r, texture(uScene, uv).g, texture(uScene, uv - ab).b);
  vec3 bloom = vec3(texture(uBloom, uv + ab * 1.5).r, texture(uBloom, uv).g, texture(uBloom, uv - ab * 1.5).b);
  vec3 col = scene + bloom * uBloomAmt;
  col = aces(col * uExposure);
  col = pow(col, vec3(1.0 / 2.2));
  float vig = 1.0 - 0.32 * smoothstep(0.25, 1.6, r2 * 4.0);
  col *= vig;
  if (uCrt > 0.0) {
    float line = 0.82 + 0.18 * sin(gl_FragCoord.y * 3.14159265);
    col *= mix(1.0, line, uCrt);
    col.g *= 1.0 + 0.04 * uCrt;
  }
  col += (gnoise(gl_FragCoord.xy + fract(uTime) * 1000.0) - 0.5) * uGrain;
  o = vec4(col, 1.0);
}";

        public static readonly string HudVs = V + @"
layout(location = 0) in vec2 aPos; layout(location = 1) in vec2 aUv; out vec2 vUv;
void main() { vUv = aUv; gl_Position = vec4(aPos, 0.0, 1.0); }";
        public static readonly string HudFs = V + @"
in vec2 vUv; out vec4 o; uniform sampler2D uTex; uniform float uAlpha;
void main() { vec4 c = texture(uTex, vUv); o = vec4(c.rgb, c.a * uAlpha); }";
    }
}
