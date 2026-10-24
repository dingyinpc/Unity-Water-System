# R22 SSR C7 抗闪完整版（时域累积）+ 六场景性能体检

> 2026-10-24 ｜ 提交 fce0493（R22a）/ R22b 见 R5Fixup 582 ｜ C7 = 《09》遗留清单"SSR 命中呈椒盐斑点"

## 人话版小结

水面反射以前每一帧都重新"打一枪"找反射物，命中点忽明忽暗——像椒盐一样闪。这次把反射结果做成"记性"：每一帧的新结果和前几帧的记忆加权平均，闪点被时间抹平。盲评结果："两边反射都在，左边（新版）噪点明显更少、形态更连贯、无鬼影发黑"。性能体检顺带把六个场景的帧耗时都量了一遍：最重的 Islet 72fps 上界，其余 150~176fps。

## 1 架构（避免重写水面法线链）

难点：反射起点是水面**自身动画法线**，通用全屏后处理拿不到。解法=**全局关键字双画水面**：

1. `Water_Advanced` 加 `_SSR_WRITE` multi_compile 变体：同一份片元代码，算完 N/反射向即**早退**只输出 `half4(SSR色, conf)`——法线链零重复；
2. `SSRTemporalFeature`（RendererFeature，BeforeRenderingTransparents）三步：开关键字把透明队列重画进 `_SSRTrace` 半幅 RT（RenderStateBlock 强制不混合不测深：水材质的 SrcAlpha 混合会把色乘 conf；追踪还要采样相机深度，不能绑深度目标）→ `_SSRTrace`+历史 → EMA → `_SSRAccum` → 主 pass `_SSRTemporalOn>0.5` 时查表替代逐像素追踪；
3. 卸载（583b）即回旧路径零回归；EMA conf 门控（新帧 miss 不清零历史、按 0.88/帧遗忘；高 conf 快跟 0.45/低 conf 慢跟 0.15）。
4. ⚠ 静态相机假设（屏幕 UV 复用历史），Main 验收机位满足；动态镜头请 583b 卸载。

## 2 验收

同场景 96 帧 ×2 组（开/关）：

| 指标 | 关（旧） | 开（时域） |
| --- | --- | --- |
| 逐像素时序方差 mean | 382.8 | **340.4（-11%）** |
| 逐像素时序方差 median | 301.7 | 276.6（-8%） |
| 时序差分高频占比 | 1.280 | 1.239 |

视觉盲评：反射两边都在；**左边噪点明显更少、形态更连贯；无拖影/鬼影/发黑/缺失；综合左边更好**。剩余方差主体是真实波浪动画（非椒盐）。

## 3 R22b 性能体检（582：单场景阻塞采样，Step 泵墙钟，150 帧前 30 热身）

| 场景 | avg | p50 | p95 | fps 上界 |
| --- | --- | --- | --- | --- |
| Main | 6.4ms | 6.0 | 8.0 | 156 |
| OceanScene | 5.7 | 6.0 | 7.0 | 176 |
| IsletScene | **13.9** | 14.0 | 15.0 | **72**（最重=体积云） |
| PoolScene | 5.8 | 6.0 | 7.0 | 171 |
| PondScene | 6.5 | 6.0 | 8.0 | 154 |

结论：R20/R21/R22 新增（颗粒后处理、双场波方程、SSR 时域）**全未破预算**；Islet 体积云是唯一 >10ms 项（与 R11 性能 93% 记录一致）。口径注意：编辑器失焦 Step 计时含编辑器开销=上界，场景间相对比较有效。FFTScene 无开场景菜单未入表（历史：模拟 3.3ms@R10）。

## 4 事故记录（都进了代码注释与 AGENTS）

- **URP14 API 实测**：`RenderingUtils.ReAllocateIfNeeded`（非 ReAllocateHandleIfNeeded）、`RenderStateMask.Depth`（非 ZTest）、`DepthState(false, func)`（非 zTest 字段）、`BlendState()` 默认即不混合。
- **Play 秒退元凶**=本特性首批编译错误；而排障时读错了日志——`%LOCALAPPDATA%/Unity/Editor/Editor.log` 属于**用户另一个 Unity 实例（MCP测试项目）**，本项目实例日志在 `E:/Blender/tools/unity_launch.log`（-logFile 启动参数）。排障第一现场认准后者。
- **菜单内阻塞等 Play 转换=自死锁**（首版性能菜单全灭 n=0），改 bash 编排。
