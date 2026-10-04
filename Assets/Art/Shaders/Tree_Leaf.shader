// Tree_Leaf.shader — 叶片卡片：背光透射 + 风摆动
//
// 为什么不用 URP 内置 Lit：Lit 没有 transmission 通道，逆光的叶子只会黑成剪影，
// 而"光穿过树叶"正是这个场景要的感觉。这里只写叶片需要的那几项，不做完整 PBR：
//   Lambert 漫反射 + 球谐环境光 + 背光透射 + alpha 裁剪 + 雾。
//
// 顶点色是交付包烘好的（见 E:\Blender\交付_树资产\Unity导入说明.md 第 5 节）：
//   R = 柔韧度，树根 0 → 叶梢 1  ← 同时当"叶片薄厚"的代理：越靠梢的叶越薄越透
//   G = 每根枝条的随机相位
//   B = 叶片抖动开关（树皮 0，叶片 1）
//
// 已知取舍：不采样阴影贴图（树冠本来就该在光里，且叶片早关了投影）、不做高光。
// 树皮继续用 URP/Lit，不吃这个 shader。
Shader "WaterProject/TreeLeaf"
{
    Properties
    {
        _BaseMap   ("叶簇图集", 2D) = "white" {}
        _BaseColor ("底色", Color) = (1, 1, 1, 1)
        _Cutoff    ("Alpha 裁剪", Range(0, 1)) = 0.5

        _TransColor("透光色", Color) = (0.55, 0.85, 0.35, 1)
        _TransStrength ("透光强度", Range(0, 4)) = 1.6
        _TransPower    ("透光锐度", Range(1, 64)) = 10

        // 下面各项**默认值必须是不改变画面的**（白 / 0）：这个 shader 潭线那批树也在用，
        // 加了属性而不给无操作默认值，下一次打开 PondScene 就发现森林的叶子变样了。
        //
        // 为什么明暗要**四个**通道叠出来：实测参考1 的树冠亮度 p90-p15 = 136~142，
        // 而我第一版只有 33 —— 差 4 倍，光靠一个视角相关的项补不上。
        // 视角相关的项（_UnderColor）还有个毛病：相机一转分层的分布就跟着变，
        // 量不稳。所以真正扛住对比度的是下面两个**与视角无关**的梯度。
        _UnderColor ("叶背色(乘在漫反射上)", Color) = (1, 1, 1, 1)
        _SheetLit   ("薄片双面受光(0=按视角翻正,潭线旧行为)", Range(0, 1)) = 0.0
        _Dbg        ("调试输出(0关/1法线/2受光/3高度/4反照率)", Range(0, 4)) = 0.0
        _TipLift    ("叶梢提亮", Range(0, 1)) = 0.0
        _HeightShade("冠内下部压暗", Range(0, 1)) = 0.0
        _CrownY     ("树高(定标用米)", Float) = 9.0
        _FrondVar   ("每片羽叶色调抖动", Range(0, 1)) = 0.0

        _WindStrength ("风幅度(米)", Range(0, 0.6)) = 0.12
        _WindFreq     ("风频率", Range(0, 5)) = 1.1
        _WindDir      ("风向(归一化)", Vector) = (0.92, 0, 0.32, 0)
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest"
               "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }

        // 卡片是单片四边形，必须双面 —— 从树下往上看才不会整块消失
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "TreeLeafForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // 交付包的风权重，缺了它树永远不动
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 vcol       : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
                // **物体空间**位置：高度梯度必须与实例缩放无关。
                // 用 positionWS 的话，同一棵树摆成 0.82 倍和 1.12 倍会得出不同的 h01，
                // 冠子的明暗就会随摆放而跳。
                float3 posOS      : TEXCOORD5;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // SRP Batcher 要求所有材质常量进同一个 CBUFFER
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Cutoff;
                float4 _TransColor;
                float  _TransStrength;
                float  _TransPower;
                float4 _UnderColor;
                float  _SheetLit;
                float  _Dbg;
                float  _TipLift;
                float  _HeightShade;
                float  _CrownY;
                float  _FrondVar;
                float  _WindStrength;
                float  _WindFreq;
                float4 _WindDir;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;

                // ★ 位移必须在【变换到世界空间之后】做，否则物体自转会带着风向一起转
                float3 wpos = TransformObjectToWorld(v.positionOS.xyz);
                float3 nrm  = TransformObjectToWorldNormal(v.normalOS);

                float flex    = v.color.r;                       // 根 0 → 梢 1
                float phase   = v.color.g;
                float flutter = v.color.b;                       // 树皮 0，叶片 1

                float sway = 0.5 + 0.5 * sin(_Time.y * _WindFreq + phase * 6.28318);
                float3 off = _WindDir.xyz * (_WindStrength * flex * sway);

                // 叶片抖动：交付说明要 noise(worldPos*8 + _Time.y*3)*0.06，
                // 这里用三轴错相的正弦当廉价噪声 —— 视觉等价，省一次噪声实现
                float3 n = wpos * 8.0 + _Time.y * 3.0;
                off += flutter * 0.06 * float3(sin(n.x + n.z), sin(n.y + n.x), sin(n.z + n.y));

                wpos += off;

                o.positionWS = wpos;
                o.posOS      = v.positionOS.xyz;
                o.normalWS   = nrm;
                o.uv         = TRANSFORM_TEX(v.uv, _BaseMap);
                o.vcol       = v.color;
                o.positionCS = TransformWorldToHClip(wpos);
                o.fogCoord   = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(tex.a - _Cutoff);

                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 Nraw = normalize(IN.normalWS);
                float facing = dot(Nraw, V);

                // ── 光照法线**不能朝相机翻**（但这是**行为改动**，必须开关化）──
                // 原来"背面那侧翻过来"看着合理，实际把树冠打成了近黑剪影：
                // 相机基本是平视 ⇒ 翻正后的法线接近水平 ⇒ 太阳在高处 ⇒ ndl≈0，
                // 漫反射只剩 SampleSH；而小叶法线**普查结果 100% 朝下**（palm_report.json
                // nrm_down=100%），朝下那面取的是**地面**那半个球，本来就是最暗的。
                // 两件事叠起来：整团冠子被同一个"地面环境光 × 同一个反照率"涂成一片，
                // 实测 luma_spread=14，而参考1 是 145 —— 差的是**受光**，不是分层。
                //
                // 叶片是张薄片，正确的模型是"朝上的那面"：**与视角无关**地把法线翻进上半球，
                // 环境光按朝上的面取（拿到天空），太阳按**哪一面朝向它**取（背面打个折）。
                // ⚠ 这个 shader 潭线的森林也在用，那批材质是**按旧模型调过色**的，
                //   所以旧路径必须一字不差地留在 _SheetLit=0 上（默认值就是 0）。
                float3 Nview = Nraw * (facing < 0.0 ? -1.0 : 1.0);   // 旧：朝相机翻正
                float3 Nup   = Nraw * (Nraw.y   < 0.0 ? -1.0 : 1.0); // 新：朝上半球翻正
                float3 Nlit  = lerp(Nview, Nup, _SheetLit);

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(Nlit, mainLight.direction))
                          + _SheetLit * 0.35 * saturate(dot(-Nlit, mainLight.direction));

                // 背光透射：视线与"光的来向"越接近重合，光越是穿过叶片射进眼睛
                float backlit = pow(saturate(dot(-mainLight.direction, V)), _TransPower);
                float thickness = saturate(IN.vcol.r);            // 叶梢薄 → 更透

                // ── 冠子的明暗分层 ──
                // 参考1 的树冠能数清叶片，靠的不是细节而是**明暗**：受光的上表面偏黄绿，
                // 下垂的老叶和翻出来的叶背近黑。判"看到的是叶背"取两条线索的较大者。
                // ⚠ 但 _UnderColor 在椰树上给的是**白**：小叶法线同向时这一项是常数，
                //   常数乘数只会把整团一起压暗，反而缩小 p90-p15 的绝对差。
                //   扛对比度的是下面三个与视角无关的梯度。
                float underside = saturate(max(facing < 0.0 ? 1.0 : 0.0, -Nlit.y));
                // flex 从叶轴根的 0.42/0.55 涨到梢的 1.0，正好当"离根多远"
                float tipMask = saturate((IN.vcol.r - 0.55) / 0.45) * _TipLift;

                float3 albedo = tex.rgb * _BaseColor.rgb;
                // ⚠ 第 11 轮（潟湖岛灌木"绿团太粗糙"）：顶点色 A = 明暗乘子。
                //   建模侧在穹顶内核烘"深处 0.18 → 顶部 ~1.0"、叶基→梢 0.55→1.0；
                //   旧资产（PondScene 树 / 椰树）A 全为 1 ⇒ lerp(0.55,1,1)=1，
                //   完全向后兼容，潭线那批树一个像素都不会变。
                albedo *= lerp(0.55, 1.0, IN.vcol.a);
                albedo = lerp(albedo, albedo * float3(1.95, 1.60, 0.48), tipMask);

                // ── 两层**与视角无关**的梯度，这才是把 luma_spread 从 33 抬向 136 的主力 ──
                // 冠子只占树的上半段（h01 大约 0.5~1.0），线性梯度在这段里几乎拉不开，
                // 所以用三次方；1.75 是把顶端补回原亮度。
                float h01 = saturate(IN.posOS.y / max(_CrownY, 0.001));
                albedo *= lerp(1.0, pow(max(h01, 0.0001), 3.0) * 1.75, _HeightShade);
                // 相邻两片羽叶给不同明暗 —— "数得清叶片"靠的是这个，不是靠细节。
                // G 通道本来就是"每片叶一个随机相位"（风用的），这里白嫖它当色调种子：
                // 同一片叶内部**恒定**，所以它不会变成噪点，只会变成一片一片的色块。
                albedo *= lerp(1.0, 0.62 + 0.76 * frac(IN.vcol.g * 7.13), _FrondVar);

                float3 ambient = SampleSH(Nlit);

                // 叶背只压漫反射。透射项不压 —— 逆光的叶背恰恰是**最亮**的那批，
                // 一起压掉就把这个 shader 存在的理由抹了。
                float3 diffuse = albedo * (ambient + mainLight.color * ndl)
                               * lerp(1.0, _UnderColor.rgb, underside);
                float3 col = diffuse
                           + albedo * _TransColor.rgb * (backlit * _TransStrength * thickness)
                             * mainLight.color;

                // ── 调试通道（默认 0 = 不改变画面；潭线共用材质因此不受影响）──
                // 为什么要有：亮端实测只有 106 而参考是 204，我连着三次把原因猜在
                // 反照率/梯度强度上，每次都被证伪 —— 这些项**在编辑器里问不到**，
                // 只能让它自己把每一项画出来看（《验证要分级》：量不到就造量具）。
                //   1 = 光照法线（Nlit 翻对没有）
                //   2 = 受光总量 (ambient + sun*ndl)/4  ← 判"太阳到底到没到叶片上"
                //   3 = 高度梯度 h01
                //   4 = 三个梯度叠完的 albedo 本身（把光照摘掉）
                //   5 = **覆盖率标记**：常量不透明色，用来量"叶片真的遮了多少像素"
                // ⚠ 为什么需要 5：上一版想用一个不依赖颜色的判据，把每株的投影框
                //   栅格化求并集，结果报"占 vegbox 92%"——可同一个框的中位色是
                //   (151,193,237) 天空蓝。原因是灌木是 41 片下垂薄叶、**AABB 里几乎全是空气**，
                //   包围盒把覆盖率高估到完全失去判别力。真正要量的是渲染出来的剪影。
                // ⚠ 数像素时**不要**直接数这个颜色：取帧还要过 ACES + saturation 22 + contrast 8，
                //   常量会被搬走。正确做法是"开标记取一帧 / 关标记取一帧"，
                //   在植被框内做差分 —— 植物从绿变品红（差值巨大），
                //   而天空和沙根本不经过本 shader（差值为 0），水只在植被框最下缘一点，量时排掉。
                if (_Dbg > 0.5 && _Dbg < 1.5) return half4(Nlit * 0.5 + 0.5, 1.0);
                if (_Dbg > 1.5 && _Dbg < 2.5) return half4(saturate((ambient + mainLight.color * ndl) * 0.25), 1.0);
                if (_Dbg > 2.5 && _Dbg < 3.5) return half4(h01.xxx, 1.0);
                if (_Dbg > 3.5 && _Dbg < 4.5) return half4(albedo, 1.0);
                if (_Dbg > 4.5)               return half4(1.0, 0.0, 1.0, 1.0);

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
