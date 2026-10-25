// ============================================================================
// Sky_Tropical.shader — 热带海岛天空：饱和湛蓝渐变 + **光线步进体积积云**
//
// 为什么要新写一个、而不是把 Sky_Coast 的云调一调：
//   参考1~6（Docs/参考视频帧/参考*.png，作者标签里就写着 #体积云）的云有两种
//   形态是 2D 平面投影**结构上给不出**来的：
//     ① **平底** —— 参考4 最清楚，每团云的下缘是一条近乎水平的软边，
//        高度基本一致。平面投影的云没有"高度"这个维度，底边形状完全由噪声决定，
//        永远是乱的。
//     ② **花椰菜顶 + 侧影** —— 云顶向上被啃成一团团，且**侧面轮廓**会随视角变。
//        投影法里"往上看"和"平看"采到的是同一张噪声图，云会像贴在穹顶上的纸片。
//   这两条都要求密度场是三维的、并且有一条真实的世界高度基准 ⇒ 只能 raymarch。
//
// 平底是怎么来的（这一句是整份 shader 的核心）：
//   h = (p.y - BASE) / (TOP - BASE)  ∈ 0..1
//   density = saturate((coverage - h) / max(coverage, 0.2))
//   coverage 是"这一列有多少云"。coverage=0.8 的列，云从 h=0 一直满到 h=0.8。
//   ⇒ **底边恒在 h=0 那一层平面上**（所以是平的），**顶边随 coverage 起伏**（所以是花椰菜）。
//   再往上叠一个随 h 增强的细节侵蚀，顶部就更碎、底部保持干净。
//
// 太阳方位不在这里存第二份：方向取 GetMainLight().direction，
//   躲开潭线"太阳仰角两个写入点"那个坑（《05》C-4）。
//
// 性能：用户明确说"不用考虑性能、越精细越好"。这里 32 步主步进 + 每步 4 步朝太阳
//   的阴影采样 + 3D 值噪声，单像素约 300 次噪声。896×508 下编辑器会掉到个位数帧率，
//   这是**有意的取舍**，不是 bug —— 截图验收走 GameView RT，不要求实时帧率。
// ============================================================================
Shader "WaterProject/Sky_Tropical"
{
    Properties
    {
        // 显示目标（Docs/参考视频帧/海岛目标色板.json）：天顶 (3,118,200)、地平 (168,206,231)。
        // 这里给的是**线性值**，中间隔着 ACES + postExposure 0.85，所以不能照抄显示值。
        _ZenithColor  ("天顶颜色（要饱和湛蓝，别发白）", Color) = (0.006, 0.280, 0.720, 1)
        _HorizonColor ("地平颜色", Color) = (0.380, 0.600, 0.800, 1)
        // 平视机位画幅上缘只有约 22° 仰角，`up` 最大才 0.38 —— 也就是说
        // 可见的那一段几乎全是 _HorizonColor，天顶色根本进不来。
        // 所以"天空够不够饱和"这个旋钮**主要是 _GradPow 和地平色**，不是天顶色。
        // ⚠ 方向：_GradPow 越**小**，up^e 越接近 1 ⇒ 越偏向天顶（这里天顶更饱和）。
        _GradPow      ("渐变指数（小=更快进天顶色）", Range(0.4, 3)) = 1.4
        // ★ 把"多少仰角算走到天顶"归一化到**画幅真的能看到的那一段**。
        //   不按这个归一时，`up` 在平视画幅里最大只有 ~0.38（仰角 22°），
        //   混合系数全程只在 0~0.26 里挪 —— 实测上带 (50,169,223)、
        //   中带 (51,171,223) **几乎同值**，整片天是一条平色，没有竖向渐变。
        //   0.42 ≈ 仰角 25°，等于把"天顶色"落到画幅上缘附近。
        _GradNorm     ("渐变归一仰角(sin值)", Range(0.15, 1.0)) = 0.42
        _GroundColor  ("地面半色", Color) = (0.180, 0.230, 0.260, 1)

        _CloudLit     ("云向阳色", Color) = (1.050, 1.070, 1.100, 1)
        _CloudShade   ("云背光色", Color) = (0.260, 0.460, 0.660, 1)

        _CloudBase    ("云层底高 (m)", Float) = 1400
        _CloudTop     ("云层顶高 (m)", Float) = 4200
        _CloudFreq    ("云列频率 (1/m)", Float) = 0.00042
        _CloudCover   ("云量", Range(0, 1)) = 0.55
        _CloudDetail  ("顶部侵蚀强度", Range(0, 2)) = 1.0
        _CloudDrift   ("漂移速度 (m/s)", Vector) = (7.0, 0.0, 3.0, 0.0)
        _Extinct      ("消光系数", Float) = 0.055
        _SunPow       ("云受光对比", Range(0.5, 6)) = 2.6
        _SilverLining ("逆光镶边", Range(0, 1.5)) = 0.55
        _SunGlow      ("日晕强度", Range(0, 2)) = 0.45
        _Steps        ("主步进数", Range(8, 64)) = 32
        _ShadeSteps   ("阴影步数", Range(0, 8)) = 4
        // 云积分的**平均相位数**。逐像素抖动是必需的（它把等距切片的同心环带
        // 打散成噪点），但代价就是天空上一层细颗粒 —— 实测高频能量 3.83，
        // 参考帧只有 1.26，是 3 倍。多相位平均是唯一能**同时**保住
        // "没有环带"和"没有噪点"的做法：同一个像素用几个不同相位各 march
        // 一遍再平均，噪点互相抵消，环带也不会回来。
        // ⚠ 默认 **1 = 与加这个属性之前完全一致**，由调用方显式抬，
        //   免得共享这份 shader 的别处被静默改掉。
        _MarchPasses  ("云积分平均相位", Range(1, 4)) = 1
        // 体积云不能靠"看图猜"：密度场空不空、受光对不对、渐变本身合不合格，
        // 三件事混在一张图里永远分不开。先证明通道通，再解释数值（《06》附 B）。
        _SkyDebug     ("调试(0正常/1云遮罩/2云受光/3仅渐变)", Range(0, 3)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox"
               "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off Lighting Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _HorizonColor, _GroundColor, _CloudLit, _CloudShade;
                float  _CloudBase, _CloudTop, _CloudFreq, _CloudCover, _CloudDetail;
                float4 _CloudDrift;
                float  _Extinct, _SunPow, _SilverLining, _SunGlow;
                float  _Steps, _ShadeSteps, _MarchPasses;
                float  _GradPow;
                float  _GradNorm;
                float  _SkyDebug;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // ⚠ 视线方向只能直接取物体空间顶点（《05》A-9）：
                //   URP 的 DrawSkyboxPass 走 CreateSkyboxRendererList，那颗立方体的
                //   物体矩阵不是"平移到相机"的可依赖形式，写成
                //   TransformObjectToWorld(v) - _WorldSpaceCameraPos 会得到一片纯黑。
                //   但**射线起点**用 _WorldSpaceCameraPos 是对的 —— 方向与起点来源不同，
                //   别把这条误读成"不能用相机位置"。
                o.dir = v.positionOS.xyz;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            // ───────── 3D 值噪声 ─────────
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float Hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float Noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i), n100 = Hash31(i + float3(1,0,0));
                float n010 = Hash31(i + float3(0,1,0)), n110 = Hash31(i + float3(1,1,0));
                float n001 = Hash31(i + float3(0,0,1)), n101 = Hash31(i + float3(1,0,1));
                float n011 = Hash31(i + float3(0,1,1)), n111 = Hash31(i + float3(1,1,1));
                return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                            lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
            }
            float Fbm3(float3 p, int oct)
            {
                float s = 0.0, amp = 0.5, norm = 0.0;
                for (int k = 0; k < oct; k++)
                {
                    s += amp * Noise3(p);
                    norm += amp;
                    p = p * 2.13 + float3(9.7, 3.1, 5.3);
                    amp *= 0.5;
                }
                return s / max(norm, 1e-4);
            }

            // 这一列有多少云。fbm 的取值天然挤在中间，必须用窄 smoothstep 才出**一团一团**
            // 而不是满屏糊盖 —— 参考3/4 里云之间是大片纯蓝的。
            float ColumnCoverage(float3 p, float3 wind)
            {
                float c = Fbm3(p * _CloudFreq + wind, 4);
                float t = lerp(0.74, 0.40, _CloudCover);       // 阈值落在 fbm 的取值带里
                return smoothstep(t, t + 0.16, c);
            }

            float SampleDensity(float3 p, float3 wind, float dist)
            {
                float h = (p.y - _CloudBase) / max(_CloudTop - _CloudBase, 1.0);
                if (h < 0.0 || h > 1.0) return 0.0;

                float cover = ColumnCoverage(p, wind);
                if (cover <= 0.001) return 0.0;

                // ★ 平底 + 花椰菜顶就来自这一行：云从层底一直满到 h = cover
                float d = saturate((cover - h) / max(cover, 0.20));

                // 顶部侵蚀：同一个噪声在越高处搅得越狠，底部几乎不动 ⇒ 下缘保持平。
                // ⚠ 细节倍频尺度 ≈ 1/(_CloudFreq*5.7) ≈ 420 m，两万公里外在屏幕上连
                //   一个像素都不到 —— 不减幅就是往远处纯灌锯齿，云边糊成一把盐粒。
                //   按距离平方压掉细节，远处只留大尺度轮廓，读成"软的一团团"。
                float det = Fbm3(p * (_CloudFreq * 5.7) + wind * 0.6, 3);   // R40: 1.7→0.6 细节翻腾降档(E 超带根因,与 R36b 同族)
                float lod = 1.0 / (1.0 + dist * dist * 1.6e-9);
                d += (det - 0.5) * _CloudDetail * (0.16 + 1.15 * h * h) * lod;

                // 层底再给一点软着陆，否则底边是一条硬直线、像贴了胶带
                d *= smoothstep(0.0, 0.045, h);
                return saturate(d);
            }

            // 朝太阳走几步估透射率。步长用"斜距"而不是世界距离，
            // 否则近地平（d.y 很小）时垂直推进极慢，云底会假亮。
            float Shadow(float3 p, float3 sunDir, float3 wind, float dist)
            {
                float t = 0.0, tr = 1.0;
                int n = (int)max(_ShadeSteps, 0.0);
                for (int i = 0; i < n; i++)
                {
                    t += 90.0;
                    float den = SampleDensity(p + sunDir * t, wind, dist + t);
                    tr *= exp(-den * _Extinct * 90.0);
                    if (tr < 0.02) break;
                }
                // saturate 是给编译器的**范围声明**，不是数值需要：
                // tr 数学上恒在 (0,1]，但编译器证明不了，下游 pow(sun,…) 会报
                // "pow(f,e) will not work for negative f"。
                return saturate(tr);
            }

            // ── 单相位云积分 ──────────────────────────────────────────────
            // 从 frag 里搬出来的，逻辑一字未改；搬的原因只有一个：
            // 要能对**同一个像素用不同抖动相位 march 几遍再平均**（见 _MarchPasses）。
            // 相位 jit 现在是**入参**而不是内部算，其余（能量守恒的混合权重、
            // 提前退出、步长随距离放大）全部保持原样。
            void MarchClouds(float3 d, float3 sunDir, float3 wind,
                             float t0, float t1, float dt, int N, float jit,
                             out float3 acc, out float trans, out float tEnd, out float maxLit)
            {
                acc = 0.0; trans = 1.0; maxLit = 0.0;
                float t = t0 + dt * jit;

                for (int i = 0; i < N; i++)
                {
                    float3 p = _WorldSpaceCameraPos + d * t;
                    float den = SampleDensity(p, wind, t);
                    if (den > 0.001)
                    {
                        // 能量守恒：先算这一小段的消光，再按入射光加权累加
                        float sigma = den * _Extinct;
                        float stepT = exp(-sigma * dt);

                        float sun = Shadow(p, sunDir, wind, t);
                        // 各向异性：朝太阳那一侧更亮（前向散射）
                        float fwd = 0.5 + 0.5 * dot(d, sunDir);
                        float lit = pow(sun, _SunPow) * lerp(0.55, 1.75, pow(fwd, 3.0));
                        lit += pow(sun, 1.6) * 0.45;             // 天光填充，别让背光面死黑
                        maxLit = max(maxLit, lit);
                        float3 cc = lerp(_CloudShade.rgb, _CloudLit.rgb, saturate(lit));
                        // 逆光银边：刚进入云体的边缘最亮
                        cc += _CloudLit.rgb * _SilverLining * pow(sun, 3.0)
                            * (1.0 - trans) * saturate(den);

                        // ⚠ 混合权重必须**无量纲**。前一版写的是 `(1-stepT)/sigma`，
                        //   注释还自称"发射-吸收解析积分"——听着很对，实际单位是**米**，
                        //   等于把云的辐射乘了 1/0.055 ≈ 18 倍，整片天直接烧成纯白。
                        //   正确的前向合成就是 `acc += cc * trans * (1 - stepT)`。
                        //   （`/sigma` 那种形式只出现在"光学厚度"或"平均反照路径"里，
                        //     不在逐段辐射累加里。）
                        acc += cc * trans * (1.0 - stepT);
                        trans *= stepT;
                        if (trans < 0.012) { trans = 0.0; break; }
                    }
                    // 步长随距离**温和**放大：远处同样一步跨过的立体角更小。
                    // 系数从 1.6 降到 0.6 —— 1.6 时掠射方向一步就跨过几十公里，
                    // 云内部根本没被采到几个点，同心环带就是这么来的。
                    t += dt * (1.0 + 0.6 * (t - t0) / max(t1 - t0, 1.0));
                }
                tEnd = t;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                if (d.y < 0.0) return half4(_GroundColor.rgb, 1.0);

                float3 sunDir = GetMainLight().direction;
                float up = saturate(d.y);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb,
                                  pow(saturate(up / max(_GradNorm, 0.05)), _GradPow));

                // 日晕：锐项给"太阳那枚硬币"，宽项给"那一片天整体发白"
                float sd = saturate(dot(d, sunDir));
                sky += _CloudLit.rgb * (pow(sd, 700.0) * 0.55 + pow(sd, 14.0) * 0.16) * _SunGlow;

                // 射线与云层两个平面的交点
                float camY = _WorldSpaceCameraPos.y;
                if (d.y < 0.0015) return half4(sky, 1.0);        // 近平视：穿层路径过长，直接当无云
                float t0 = max((_CloudBase - camY) / d.y, 0.0);
                float t1 = (_CloudTop - camY) / d.y;
                if (t1 <= t0) return half4(sky, 1.0);
                // ⚠ 步进距离截在 45km，不是 260km。掠射方向上射线在云层里要穿
                //   上百公里，而 coverage 噪声最低一倍的特征尺度就有 ~2.4km ——
                //   48 步根本采不动它，远处直接糊成一把盐粒（实测就是这样）。
                //   超过这个距离的云在屏幕上已经不到一个采样点，截掉不可见，
                //   剩下的透射率交给下面的地平雾接管。
                t1 = min(t1, 45000.0);

                float3 wind = _CloudDrift.xyz * _Time.y;
                float dt = (t1 - t0) / max(_Steps, 4.0);

                // 3 = 只留渐变：把云整个关掉，这样"天空蓝到没到目标值"
                // 是一个可以单独量的数，不会被云的平均效应掩盖
                if (_SkyDebug > 2.5) return half4(sky, 1.0);

                int N = (int)max(_Steps, 4.0);
                // ⚠ 逐像素抖动起点。不抖的话等距切片会在云上留下一圈圈**同心环带**
                //   （第一版实测就是"千层饼"），因为云边界恰好落在某一片切片上时，
                //   整片区域的采样相位是一致的。
                //   这里原注释说"抖动把相位打散，环带变成细噪点，再被后面的雾与
                //   压缩吃掉" —— **前半句对，后半句被实测否掉了**：噪点根本没被吃掉，
                //   天空带高频能量 3.83，参考帧只有 1.26（3 倍）。
                //   所以改成多相位平均：同一个像素用几个错开的相位各 march 一遍再平均。
                //   噪点互相抵消，而环带**不会**回来 —— 因为相位依旧是逐像素打散的，
                //   只是每个像素从"取 1 个相位"变成"取 P 个相位求均值"。
                //   _MarchPasses 默认 1 ⇒ 不显式抬就等于改之前的单遍行为。
                float base = Hash21(IN.positionCS.xy * 0.6135);
                int P = (int)max(_MarchPasses, 1.0);
                float3 acc = 0.0;
                float trans = 0.0;
                float maxLit = 0.0;
                float t = 0.0;                       // 下面算地平雾淡入要用，保持这个名字
                for (int k = 0; k < P; k++)
                {
                    float3 ac2; float tr2, te2, ml2;
                    MarchClouds(d, sunDir, wind, t0, t1, dt, N,
                                frac(base + (float)k * 0.37), ac2, tr2, te2, ml2);
                    acc   += ac2;
                    trans += tr2;
                    t     += te2;
                    maxLit = max(maxLit, ml2);
                }
                float invP = 1.0 / (float)P;
                acc *= invP;  trans *= invP;  t *= invP;

                // 1 = 云遮罩：纯看"这一条射线穿没穿过云"。
                //   如果整屏是黑的，问题在密度场（高度区间/频率/阈值），
                //   不在光照 —— 这一眼就能把两大类原因分开。
                if (_SkyDebug > 0.5 && _SkyDebug < 1.5)
                {
                    float mask = 1.0 - trans;
                    return half4(mask, mask, mask, 1.0);
                }
                // 2 = 只看受光量：能看出云为什么上白下灰，以及阴影步进是不是恒为 1
                if (_SkyDebug > 1.5)
                    return half4(saturate(maxLit / 2.0), 0.0, 1.0 - trans, 1.0);

                float3 col = sky * trans + acc;

                // 45km 处硬截会留一圈"云突然没了"的边。把**剩下的透射率**按
                // 射线走过的距离淡进地平雾色 —— 参考3/4 的地平本来就是一条
                // 密云带 + 一条白雾，正好用这个淡入当那条带，截断就看不出来了。
                // ⚠ 这里原来只按 `far`（本条射线走完自己区间的比例）算霾，是**反的**。
                //   仰着走的射线：云层只有 (4200-1400)/d.y ≈ 三千米，几步就走完
                //   ⇒ far 顶到 1、又没云所以 trans≈1 ⇒ toHaze=1 ⇒
                //   **天顶那片纯净蓝天被混进 85% 的淡地平色**。
                //   而真正该有霾的掠射地平带反而 far 很小、吃不到霾 —— 完全倒置。
                //   实测对得上：抬亮 SkyHorizon 会让 sky_upper 偏差 44→64
                //   （当时误判成"渐变爬上顶缘"，GradNorm 试验已证明不是它）。
                //   _SkyDebug=3 看着纯蓝是因为它在**这行之前**就 return，
                //   没经过这个 lerp —— 那个对照当时把结论带偏了一次。
                //
                // 修法不是"把天顶的霾关掉"，而是**按仰角给**，因为两端都是量出来的：
                //   toHaze=1（旧行为，天顶吃满）→ sky_upper R = **129**
                //   toHaze=0（绕过这行）        → sky_upper R = **3**
                //   目标                        → sky_upper R = **65**
                //   ⇒ 天顶要的不是 0 也不是 1，是 (65-3)/(129-3) ≈ **0.50**。
                //   直接二值化（只有撞 45km 截断才给霾）会把误差翻到另一边
                //   （R 掉到 3，偏差 62），所以用仰角斜坡：地平 1.0 → 天顶 0.50。
                //   trans 保留：云厚的地方不该再叠霾。
                float toHaze = saturate(lerp(1.0, 0.50, saturate(d.y * 2.2)) * trans);
                col = lerp(col, _HorizonColor.rgb, toHaze * 0.85);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "WaterProject/Sky_Coast"
}
