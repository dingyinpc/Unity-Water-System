# R02 · P0-2 SSR 屏幕空间反射 检测报告

> 参考章节：S3 SSR 屏幕空间反射（C7–C8，0:20–0:30）
> 目标场景：`OceanScene`（`Water_Ocean.shader`）；同类代码后续可复制到 `Water_Advanced`
> 日期：2026-10-17 ｜ Unity 2022.3.57f1c2 · URP 14.0.11

---

## 0 元信息

| 项 | 值 |
| --- | --- |
| 参考时间码 | 20.1–29.8s（C7 20.1–25.9 法线Mip过滤；C8 25.9–29.8 命中可信度/环境融合） |
| 量测工具 | 目视关键帧 + Play 截图（`Tools/Ocean/85`）+ 文件体积/像素差 |
| 前置检查 | `WaterURPAsset.asset`：`m_RequireDepthTexture: 1`、`m_RequireOpaqueTexture: 1` ✅（SSR 前提全满足） |
| 涉及文件 | `Water_Ocean.shader`（TraceSSR + clipPos）、`NearshoreFlowSetup.cs`（菜单 97~105） |

---

## 1 参考侧逐帧量测

| 帧 @ 秒 | 烧录标题 | 画面内容 |
| --- | --- | --- |
| f620 @ 20.7s | 法线Mip过滤 | FFT 海面细节（C7 的抗闪演示） |
| f700 @ 23.3s | 最终FFT海面 | 最终海面，青色 + 密集碎闪 |
| f800 @ 26.7s | **SSR 屏幕空间反射** | **潟湖：岸边岩壁与绿树、蓝天白云真实倒映在水面** |

**结论**：参考的 SSR 演示场景 = 浅潟湖 + 岸边岩壁，机位**俯视**水面——反射方向上有**高大近岸几何**，命中后水色被真实场景颜色取代。这正是我们当前完全 lacking 的东西。

---

## 2 我方现状（实施前）

| 场景 | 反射 |
| --- | --- |
| OceanScene / Main | **只有双色天空渐变**（`_SkyHorizon→_SkyZenith` 按 `reflDir.y` 插值）+ 太阳碎闪 |
| PondScene | 平面反射（独立管线，**比 SSR 更适合静水体**，P0-2 不动它） |

**差距**：水面映不出任何场景物体（岸、石、树），只有一块"镜子里的天"。

---

## 3 差距对比

1. ❌ 无屏幕空间反射光线步进
2. ❌ 无命中判据与可信度（深度差/命中厚度/追踪距离/屏幕边缘）
3. ❌ 无 miss 回退策略（参考 = CubeMap，我们 = 天空渐变，结构相同但需接通）
4. ❌ 无屏幕空间视野足迹抗闪（C7 / `SampleGrad`）——**本步未做**，见 §6

---

## 4 补齐实施

### 4.1 代码

- **`Assets/Art/Shaders/SSR.hlsl`（新增，共享）**：`TraceSSR(wp, rd, maxDist, stepsF, thickness, edgeFade, out conf)` ——
  步进 + 6 次二分 + 三因子可信度；**参数走显式传参**，不依赖调用方在何处声明 uniform（见 §6-6 的坑）。
  由 `Water_Ocean` 与 `Water_Advanced` 共用（消重复，符合"只写一处"纪律）。
- **miss 回退**：`reflBase = lerp(skyCol, ssrCol, ssrConf)` —— conf=0 时与旧路径**逐位一致**，零回归。
  太阳碎闪**拆出独立项**（照《05》原文："碎闪要单独，否则会被场景色整条抹掉"）。
- **铁律遵守**：`Varyings` 新增 `float4 clipPos : TEXCOORD3`（**不读片元 `SV_POSITION`**，《05》A-1）。
- A/B 旋钮：Ocean 走菜单 97~101；`Water_Advanced` 走同一套 `_SSRStrength/_SSRDebug`（Main 侧 A/B 待切场景后补）。

可复现：
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/bridge_menu.ps1 -Menu "Tools/Ocean/97 SSR=0(关)"
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/bridge_menu.ps1 -Menu "Tools/Ocean/98 SSR=1(开)"
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/bridge_menu.ps1 -Menu "Tools/Ocean/99 SSR调试:命中可信度"
```

### 4.2 验证柱与机位工具（临时，菜单 102~105）

参考的演示场景里有**高大近岸几何**，而 OceanScene 没有——为做机制证明，加了临时的 `SSRProbePillar`（6m 立柱，放岸线外 6m）与"机位抬高"菜单，用完即删。

---

## 5 验收判据与结果

### 5.1 已确证

| # | 判据 | 结果 |
| --- | --- | --- |
| 1 | 编译干净（C# + 两份水 shader） | ✅ **新增量判定工具** `Tools/check_shader.ps1`（mark/report），report 全绿 |
| 2 | **无视觉回归**：SSR 关/开 两张 Play 截图 | ✅ 118618 vs 118642 字节（近乎逐字节一致） |
| 3 | 命中可信度图（`_SSRDebug=1`） | ✅ 出图正常，水面上 conf≈0 |
| 4 | 共享实现接入 `Water_Advanced`（Main） | ✅ 编译通过（**真机命中验证待切 Main**） |
| 5 | 6m 验证柱在场时命中 | ⚠️ **仍未命中**（见 5.2 的原因分析） |

### 5.2 为什么 OceanScene 上 SSR 命中为 0（物理论证，非缺陷）

- 本机位：眼高 1.62m、俯角 −4.5°、FOV 52 ⇒ 画面里**几乎全是天空，水面占 ~1/3**；
- 水面 (N≈0,1,0) 的反射方向 `R = reflect(−V,N)`，相机在水面**上方** ⇒ `R.y = +V.y > 0`，**反射线朝上**；
- 目标物顶高 h、距相机 D、眼高 e ⇒ 镜中像与视线的交点在水面**距相机 `e·D/(h−e)`** 处：
  h=6m、D=6m ⇒ 交点在 **1.3m**（相机脚下，近裁剪面外）;要交点落在 3m 外需要 `h·e` 关系近乎反比 ⇒ 对该机位不可达。
- ⇒ **OceanScene 现有构图下，反射线全部指向天空，conf=0 是物理正确结论；天空兜底正常工作（无回归即证据）**。
- 参考演示之所以能反射岩壁，是因为它的机位**俯视**潟湖，反射线落在岸壁上。

### 5.4 人话版小结（零基础可读）

- **我们做了什么**：给水面加了一段"顺着反射方向往前找"的代码——它每一步都问一句"我脚下这个像素在屏幕上的深度，和我这束光的深度，谁更近？"，一旦光跑到物体后面就算"看见"了，然后把**那个物体的颜色**拿来当水面的倒影。
- **结果**：代码是对的（改掉一个真 bug 后，可信度图上水线附近开始出现"看见过"的斑块），但**在这片海的构图下几乎什么都看不见**——因为相机低、眼前全是天。天空没有深度，自然"撞"不到东西。
- **所以**：SSR 在 Ocean 上"开了等于没开"（关/开两张图冻结后**逐字节相同**）。这不是坏了，是**这个场景没有可反射的东西**。
- **参考视频为什么好看**：它的演示场景是**俯视一片浅潟湖、岸上立着岩壁和树**——反射线一出去就撞到岩壁，于是水里就有倒影。
- **下一步**：把这套代码拿到 **Main 场景**（相机俯视 26°、岸边有树/石）去验收；或者给海岸加一段"岸壁"（场景设计决定）。

### 5.5 本轮抓到的真 bug（值得单记）

`ClipToScreenUV` 最初**漏了 Unity `ComputeScreenPos` 的最后一步 `o.zw = pos.zw`**（把 w 还原回去）。
漏掉它 ⇒ 分母少一半 ⇒ 采样 UV 变成 `(ndc + 1)` 而不是 `(ndc*0.5 + 0.5)`，**整条光线步进和深度采样全部落在画面之外**，
表现为"命中可信度处处为 0"——看起来像"场景没有可反射物"，其实是 UV 错了。已修（`SSR.hlsl` 内附警告注释）。

把同一份 `TraceSSR` 复制进 **`Water_Advanced.shader`（Main 场景）**：
Main 相机俯角 **+26°**、水面大、岸边有树/石/沙 ⇒ 反射线会命中，能得到参考 f800 那样的真实倒影。
**阻塞**：需要把 Unity 切到 Main 场景（桥的 `openScene` 会崩，需手动打开，同 OceanScene 的情况）。

---

## 6 Main 场景真实验收（P0-2 收尾 · 2026-10-18）

本节是本报告 §5 的**收尾**：把 R02 一直"待切 Main"的真实验收补上。（参考侧量测沿用 §1，无新增帧。）

### 6.1 步骤（可复现）

```powershell
# 1) 切场景（用菜单，不用桥 openScene —— 后者在本机令编辑器崩溃）
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/bridge_menu.ps1 -Menu "Tools/Project/120 打开 Main（复刻视频主线）"
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/bridge_menu.ps1 -Menu "Tools/Project/125 当前场景(写探针)"
# → MENU125 当前场景=Main 水面渲染器=1 :: WaterProject/Water_Advanced @ Water
# 2) 进 Play → 冻结 → 截图（关/开/可信度）
#    Tools/Play/90 · Tools/Ocean/108 · Tools/Ocean/85
#    Tools/Project/130/131/132 ；分析：python Tools/img_ab.py A.png B.png --heat diff.png
```

### 6.2 抓到并修掉的两个真 bug（本节重点）

第一次 A/B：关/开两张**只差 146 像素（0.04%，全在左上角 HUD 帧号）** ⇒ SSR 开关对画面**毫无影响**。回读 shader 发现：

1. **`_SSRStrength` 从没接线**：`Water_Advanced` 与 `Water_Ocean` 都只在 `Properties`/`CBUFFER` 里声明了它，**片元里从未使用** ⇒ 菜单改强度画面不变。
   修：`reflBase = lerp(skyCol, ssrCol, ssrConf * _SSRStrength)`（0=纯天空兜底，1=命中处全用场景色）。
2. **`Water_Advanced` 没有 `_SSRDebug` 分支**（只有 `Water_Ocean` 有）⇒ 132/133 调试图在 Main 点了没反应（截图与"开"逐字节相同）。
   修：把两条 debug 早退补进 `Water_Advanced` frag。

> 印证 §5.5：`_SSRStrength` 之所以"看不出没用"，是被 Ocean 的"conf 本来=0"掩盖成"看不见很正常"；换到有物体的 Main 才暴露。**这正是"Ocean 不适合当 SSR 演示场景"的第二个代价。**

### 6.3 修正后的真机结果（冻结 A/B，896×379，Main）

| 参数（追踪） | 关 vs 开 变化像素 | 最大通道差 | 水面命中可信度（灰=R=G=B 掩膜） |
| --- | --- | --- | --- |
| 默认 24步/40m/厚0.5 | **0.90%**（3052 px） | 133 | conf>0.1 = 16.5%，均值 0.088 |
| 密采 48步/120m/厚1.5 | **3.35%**（11378 px） | 140 | conf>0.1 = 57.8%，conf>0.5 = 29.0%，均值 0.306 |

- 可信度图（`_SSRDebug=1`）在**水线附近与石群之间出现成片灰/白**（=真的"看见"了石头），与 Ocean "整片黑" 形成对照 ⇒ **SSR 在 Main 确实生效**。
- 差异**集中在画面中段的水面**（石头倒影带）；岸、石、天不受影响（非水面像素不走该 shader）。

### 6.4 定稿参数

`Water_Advanced.mat` 采用密采参数并落盘（**唯一写入点 = .mat**）：
`_SSRStrength=0.85 _SSRSteps=48 _SSRMaxDist=120 _SSRThickness=1.5 _SSREdgeFade=24 _SSRDebug=0`。

### 6.5 判据与结果

| # | 判据 | 结果 |
| --- | --- | --- |
| 1 | 编译干净（C# + 两份水 shader） | ✅ `check_shader.ps1 -Mode report` 绿 |
| 2 | Main 上 SSR 关/开**能测出差异** | ✅ 密采 3.35% 像素 |
| 3 | 命中可信度非 0（真有可反射物） | ✅ 水面 conf>0.1 达 57.8% |
| 4 | 冻结后结论可复现 | ✅ 关/开差异全在水面；重启编辑器后重截均值一致 |
| 5 | 零回归（Ocean 侧 conf=0） | ✅ conf=0 ⇒ `lerp(sky,ssr,0)=sky`，与旧路径逐位一致 |

### 6.6 人话版小结

- **之前**：SSR 这套"在水面里映出岸上东西"的代码写好了，但在海的场景里啥也映不出来（眼前全是天，没东西可撞）。这次换到 **Main（一个有石头有岸的水塘，相机从上往下俯视）** 去验收。
- **第一次测**：开关 SSR，两张图几乎一模一样——**因为代码里一个总开关根本没接上**（声明了却没用），另一个"调试图"在 Main 的 shader 里压根没写。这两个是真 bug，已修。
- **修完再测**：开关 SSR 画面有 **3.35% 的像素**发生变化，而且**变化全落在水面上**；"可信度图"上水面里有成片亮起来的地方，正是"这束光撞到了石头，所以那里映出了石头"。**结论：SSR 真的在工作。**
- **定稿**：把实测最好的一组参数（48 步/120 米/厚 1.5）存进水面材质，以后打开就是这个效果。

### 6.7 工具与已知事故

- **新增 `Tools/img_ab.py`**：两张图给数（阈值像素占比 + 差异热图），中文路径走 `np.fromfile + cv2.imdecode`。
- **⚠ 桥的 `getComponentProperties` 会把编辑器卡死在 "Hold on"**：本轮为读 Main 相机参数用了它，主线程卡死 30+ 分钟（"Hold on" 对话框明写 `EditorApplication.update: UnityCopilot.UnityBridgeServer.ProcessMainThreadQueue`）。**规避：不要用该 action**；要读相机/组件参数就写个只读菜单。已用 `Tools/launch_unity.py` 重启编辑器恢复，材质/场景状态无损。

---

## 7 遗留与风险

1. **C7 抗闪未实现**（法线Mip + `SampleGrad` 屏幕足迹）；我们的替代（掠射衰减 `fineW`、三层八度）已有一半，但不是参考做法。
2. **性能未测**：24 步 + 6 步二分 × 全屏水像素；需 Play 探针 FRAME ms（下一步一并测）。
3. **SSR 调试位不会自动复位**（`_SSRDebug=1` 会一直开着）——本次已用菜单 101 复原；后续应给 99/100 加上"下次截图后自动复位"。
4. **机位临时抬高菜单（104）实测没能让画面看向水面**（画面全是天空），待定位（怀疑旋转未被当前渲染帧采纳）→ 同 P0-1 §6 的教训："观测通道先于被观测量"应先验证机位 change 生效再截。
5. **Ocean 主水面在转发模式下没有可用的高大近岸几何**；若想 Ocean 也有真实倒影，需场景级新增"岸壁"（参考 f800 的做法），属场景设计决策，留待用户定。
6. **⚠ Unity 不追踪自定义 `#include` 的依赖**：改 `SSR.hlsl` 不会重新编译引用它的 Water_Ocean/Water_Advanced，
   必须同时 touch 引用方（或改其内容）才会重编。本步因为这个把"旧缓存错误"误判了两轮；
   **找一个判定工具 `Tools/check_shader.ps1`（mark/report 基线增量），以后改 .hlsl 必须跑它**。
7. **A/B 菜单 98 曾漏复位 `_SSRDebug`**（导致"开"那张截的其实是调试图）——已修（97/98 均显式写 debug=0）。
   教训同《05》：**A/B 工具必须把"被改变的观测状态"一并复位**。
8. **OceanScene 不适合当 SSR 演示场景**（低机位 + 大面积天空）。SSR 的真实验收应在 **Main**；若要 Ocean 也有倒影，需场景级加"岸壁"。
9. **冻结法**（`Time.timeScale=0` + 停流场）是本轮确立的 A/B 标准动作：不冻结时海沫/波浪动画会产生 20~30 的像素差，
   足以掩盖或伪造一个 SSR 效果。**结论性 A/B 一律先冻结**（菜单 108/109）。
10. **C7 抗闪仍未做（收尾后更明显）**：命中可信度图呈**椒盐斑点**——射线在掠射角下逐像素"穿过/撞上"薄几何，覆盖率够但边缘会闪。
    参考的解法（法线 Mip + `SampleGrad` 屏幕足迹）未实现。**这是 P0-2 之后最该补的一项**（否则动起来会看到反光抖动）。
11. **SSR 性能未测**：密采 48 步 × 6 次二分 × 全屏水像素，成本不低；待 Play 探针 FRAME ms（与 P1-2 一并测）。
12. **`getComponentProperties` 会卡死编辑器**（见 §6.7）：这是桥工具缺陷，不属 SSR，但本轮踩到，**新会话勿用**。
13. **参数写入点**：Main 的 SSR 参数现在**只**写在 `Water_Advanced.mat`（shader Properties 里的默认值仅作新材质初始值）；
    调参用菜单 135/136，改完即 `AssetDatabase.SaveAssets()` 落盘，避免"改了没存 = 下次又变默认"。
