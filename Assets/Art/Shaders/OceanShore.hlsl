// ============================================================================
// OceanShore.hlsl — 海岸参数化的唯一 HLSL 来源
//
// 为什么单独一个文件：海面要按"离岸多远"算水深，沙滩要按"离岸多远"算湿不干，
// 浪沫要按"离岸多远"决定冲到哪儿。三处用的是**同一条岸线 + 同一个剖面**。
// 一旦这三份式子分开抄写，水陆交界就会错位 —— 潭线那边是靠"水盘和沙岸共用
// PondR(θ)"解决的，这里同理，只是沙滩还要单独画，所以抽成 include。
//
// 还有一条本文件独有的约束：**贴沙的那层水膜用的是解析剖面**，所以沙面网格
// 的任何起伏都必须走同一个 SandY()。起伏不能"只加在沙滩网格上"，否则水膜会
// 浮在沙上或陷进沙里。这也是为什么 SandRipple 在这里而不是在 C# 的建网格代码里。
//
// 仍然存在的第二抄写点：C# 侧 Assets/Editor/WaterOceanSetup.cs 里的
// CShoreZ / CSandY（生成网格必须能在 C# 里算高度）。
// 两份式子由菜单80 的 SELF-CHECK 行对账：它把 C# 版在固定采样点的值写进探针，
// 改任何一个系数都要回来核这几行。标记：同式 v2。
// ============================================================================
#ifndef WATERPROJECT_OCEANSHORE_INCLUDED
#define WATERPROJECT_OCEANSHORE_INCLUDED

// ── 本文件一个 uniform 都不声明。这是契约，不是偷懒 ──
// 宿主 shader 必须在自己**唯一**的 UnityPerMaterial 块里，先声明下面 10 个名字，
// 再 #include 本文件。声明在本文件之后 = 编译报 "undeclared identifier '_Shore'"，
// 因为 HLSL 自上而下解析，本文件的函数体看不到后面的声明。
//
//   float4 _Shore;      // x = 世界 X 起点（岸线谐波以此为零点）, y = 岸线 Z 基准
//   float4 _ShoreA;     // 三个谐波的幅度（米）
//   float4 _ShoreB;     // 三个谐波的频率（1/米）
//   float4 _WindDir;    // 浪**传播**的方向（朝岸 = -z），已归一化
//   float  _ShoreSlope; // 海床：每离岸 1m 加深多少
//   float  _BeachRise;  // 滩面：每往岸上 1m 抬升多少（线性项）
//   float  _BeachCurve; // 滩面二次项（越往里越陡，读成沙丘脚）
//   float  _SwashReach; // 水膜上冲距离
//   float  _SwashSpeed; // 上冲周期频率
//   float  _SwashFade;  // 边界退透距离
//
// 三种"看起来更省事"的放法都试过了，全部编译失败，别再试第四种：
//   ① 本文件自己开一个 CBUFFER_START(UnityPerMaterial)
//      → "Duplicate constant buffer declaration: UnityPerMaterial"。
//        同名 cbuffer **不会**被合并；URP 的 CommonMaterial.hlsl 用的其实是
//        另一个名字（UnityBuiltinPerMaterial），不是同名第二块。
//   ② 本文件开一个自创名的块（OceanShore）→ 能跑，但 SRP Batcher 要求
//      一个材质的属性全在 UnityPerMaterial 里，于是两个材质都判成不兼容。
//   ③ 本文件只 #define 一个声明宏，宿主在块里展开 → 回到"声明在 include 之后"，
//      本文件自己的函数先用到 _Shore，直接 undeclared identifier。
//
// 另：这 10 个名字还必须在 shader 的 Properties{} 里列出来，否则**不落盘** ——
// 序列化看的是 Properties{}，与 cbuffer 无关；只在 cbuffer 里声明的 uniform，
// SetFloat 写了也只活在本轮会话，重开工程归零。

// 岸线：世界 X → 岸线所在的 Z。单值函数，所以不能有把海遮在后面的岬角。
float ShoreZ(float worldX)
{
    float u = worldX - _Shore.x;
    return _Shore.y
         + _ShoreA.x * sin(u * _ShoreB.x)
         + _ShoreA.y * sin(u * _ShoreB.y + 1.7)
         + _ShoreA.z * sin(u * _ShoreB.z + 0.4);
}

// 世界坐标点"有符号离岸距离"：>0 在海侧，<0 在岸侧。
// 注意这是沿 z 轴量的，不是垂直距离 —— 岸线起伏的斜率被压在 |dZ/dx| < 0.25 内
// （实测 0.147），所以误差不超过 1%，而换来的是网格可以保持轴对齐。
float OffDist(float3 wp)
{
    return wp.z - ShoreZ(wp.x);
}

// 沿岸起伏。湿区只给极小量（k=0.06 ⇒ 最大 5cm），越往里越大，读成沙丘脚。
float SandRipple(float worldX, float offDist)
{
    float up = max(-offDist, 0.0);
    float k = 0.06 + 0.94 * saturate((up - 12.0) / 18.0);
    return k * (0.55 * sin(worldX * 0.013 + 2.1)
              + 0.16 * sin(worldX * 0.061 - 0.7) * sin(up * 0.19)
              + 0.10 * sin(up * 0.42 + worldX * 0.03));
}

// 沙面（也是海床）高度。主剖面两侧在 offDist = 0 都取 0，所以剖面连续。
float SandY(float worldX, float offDist)
{
    float base = (offDist >= 0.0)
               ? -(offDist * _ShoreSlope)
               : (-offDist) * _BeachRise + (-offDist) * (-offDist) * _BeachCurve;
    return base + SandRipple(worldX, offDist);
}

float WaterDepth(float worldX, float offDist)
{
    return max(-SandY(worldX, offDist), 0.0);
}

// 这一瞬间水膜能冲到多远。海面与沙滩共用它，湿沙边界才会和沫带同步呼吸。
float SwashReach(float t)
{
    float tide = 0.5 + 0.5 * sin(t * 6.2831853 * _SwashSpeed);
    return _SwashReach * (0.55 + 0.45 * tide);
}

#endif
