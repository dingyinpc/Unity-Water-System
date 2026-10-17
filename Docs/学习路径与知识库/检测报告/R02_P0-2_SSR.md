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

### 5.3 P0-2 还差的最后一步（明确目标）

把同一份 `TraceSSR` 复制进 **`Water_Advanced.shader`（Main 场景）**：
Main 相机俯角 **+26°**、水面大、岸边有树/石/沙 ⇒ 反射线会命中，能得到参考 f800 那样的真实倒影。
**阻塞**：需要把 Unity 切到 Main 场景（桥的 `openScene` 会崩，需手动打开，同 OceanScene 的情况）。

---

## 6 遗留与风险

1. **C7 抗闪未实现**（法线Mip + `SampleGrad` 屏幕足迹）；我们的替代（掠射衰减 `fineW`、三层八度）已有一半，但不是参考做法。
2. **性能未测**：24 步 + 6 步二分 × 全屏水像素；需 Play 探针 FRAME ms（下一步一并测）。
3. **SSR 调试位不会自动复位**（`_SSRDebug=1` 会一直开着）——本次已用菜单 101 复原；后续应给 99/100 加上"下次截图后自动复位"。
4. **机位临时抬高菜单（104）实测没能让画面看向水面**（画面全是天空），待定位（怀疑旋转未被当前渲染帧采纳）→ 同 P0-1 §6 的教训："观测通道先于被观测量"应先验证机位 change 生效再截。
5. **Ocean 主水面在转发模式下没有可用的高大近岸几何**；若想 Ocean 也有真实倒影，需场景级新增"岸壁"（参考 f800 的做法），属场景设计决策，留待用户定。
6. **⚠ Unity 不追踪自定义 `#include` 的依赖**：改 `SSR.hlsl` 不会重新编译引用它的 Water_Ocean/Water_Advanced，
   必须同时 touch 引用方（或改其内容）才会重编。本步因为这个把"旧缓存错误"误判了两轮；
   **找一个判定工具 `Tools/check_shader.ps1`（mark/report 基线增量），以后改 .hlsl 必须跑它**。
