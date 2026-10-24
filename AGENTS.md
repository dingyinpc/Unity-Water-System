# AGENTS.md — 接手须知

> 本文件给**任何新接入的 agent / 新会话**看。读完这一页就能直接干活，不必翻聊天记录。
> 维护方式：每个大步骤完成时更新「当前进度」和「下一步」。

## 1 项目是什么

复刻抖音博主「雨鱼鱼雨雨雨」《unity 水体 shader》的完整效果。
Unity **2022.3.57f1c2** + URP **14.0.11**，工程根目录就是本仓库根目录。

作者**完全不懂技术**：一切技术决策由 agent 自主负责；交付物 = 可运行的效果 + 零基础能读懂的文档。
所以：术语要用大白话解释一遍，报告里必须有「人话版小结」。

## 2 铁律（违反会浪费大量时间，都是踩过的坑）

1. **PowerShell 执行策略是 Restricted**：跑 `.ps1` 必须
   `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/xxx.ps1 ...`
   直接用 `opencode` 命令同理：用 `opencode.cmd`，不要用 `opencode.ps1`。
2. **Python 读中文路径**：用 `cv2.imdecode(np.fromfile(path, np.uint8), ...)`，或先复制到 ASCII 临时路径。
3. **不要用桥的 `openScene`**：本机两次让编辑器崩溃/退出（手动打开正常）。
   切场景用菜单 `Tools/Project/120~124`（`EditorSceneManager.OpenScene`）。
4. **改 `.hlsl` 后必须 touch 引用它的 shader**：Unity 不追踪自定义 include 依赖，否则用旧缓存。
   每次改完跑 `Tools/check_shader.ps1 -Mode mark` → 改 → `-Mode report` 做增量判定。
5. **A/B 结论必须先冻结**：`Tools/Ocean/108 冻结`（`Time.timeScale=0` + 停流场）。
   不冻结时海沫/波浪动画本身就有 20~30 像素差，会伪造出「效果差异」。
6. **参数只有一个写入点**：同一参数不要在材质、脚本、菜单里各写一份（《05》C-4 的教训）。
   实例：天空 shader 用 `GetMainLight()` 取太阳方向，材质里不再存太阳角。
7. **改完必须回读探针**：`Logs/water_probe.txt`。只截一张图就下结论 → 会误判（已有 3 次教训）。

## 3 目录地图

| 路径 | 内容 |
| --- | --- |
| `Docs/学习路径与知识库/08_参考视频知识点全解.md` | 参考视频 15 章 + 38 卡逐卡转录（**事实源**） |
| `Docs/学习路径与知识库/09_对标差距分析与补齐路线.md` | 差距表 + P0~P2 路线 + 进度表 + 零基础导读 |
| `Docs/学习路径与知识库/检测报告/` | R01(浅水流场) / R02(SSR) / R03(昼夜) 每步验收报告 |
| `Docs/参考视频/`、`Docs/视频逐帧/` | 原片与 5568 帧（**gitignored，不入库**） |
| `Assets/Art/Shaders/` | `Water_Ocean`(海岸) / `Water_Advanced`(主线) / `SSR.hlsl` / `ShallowWaterSim` / `Sky_Coast` |
| `Assets/Scripts/Logic/` | `NearshoreFlowSim`(浅水模拟) / `DayNightCycle`(昼夜) |
| `Assets/Scripts/MonoBehaviour/` | 水体表现脚本等 |
| `Assets/Editor/` | 全部编辑器菜单（唯一操作入口） |
| `Tools/` | Unity 桥、截图、逐帧量测、shader 增量判定 |
| `Logs/water_probe.txt` | 所有菜单的探针输出（**排障第一现场**） |

场景：`Main`（主线水塘）、`OceanScene`（海岸主力）、`PondScene`、`PoolScene`（泳池交互）、`IsletScene`（环礁）、`FFTScene`（R2 新增 FFT 海）。

## 4 常用命令

```powershell
# 拉起 Unity（编辑器已关闭时）
python Tools/launch_unity.py

# 执行编辑器菜单（桥，端口 6400；需 Temp/UnityLockfile 存在）
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/bridge_menu.ps1 -Menu "Tools/Ocean/85 Play 中截图 Game 视图"

# 强制刷新资源 + 编译（改完代码）
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/UnityBridgeClient.ps1 -Action "refreshAssets" -TimeoutSec 30

# shader 增量判定
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/check_shader.ps1 -Mode mark
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/check_shader.ps1 -Mode report

# 参考视频分段逐帧量测
python Tools/Video/segment_report.py --start 3000 --end 3450 --region sky
```

## 5 当前进度（2026-10-24 深夜 R23 四轮收官同步）

> ⚠ 旧警告存档：曾出现过头部写「模块完成」但仓库无对应代码的情况——**一切以下表和 R 系报告为准**。
> 阶段6（R1~R6/R14）收官后，用户追加四轮：R20 感官复刻 / R21 涟漪手感 / R22 SSR时域+性能 / R23 矩阵刷新收官。
> 旧 SSR_AntiFlicker.hlsl 草稿已被 **R22a 正式实现取代**（SSRTemporalFeature，见 §7 第 13 条更新）。

| 编号 | 内容 | 状态 |
| --- | --- | --- |
| P0-1 | 近岸浅水流场（干湿网格→水位梯度→涌浪→接水材质） | ✅ 完成，波速实测 10.8 vs 理论 10.75 m/s（0.5% 误差） |
| P0-2 | 屏幕空间反射 SSR | ✅ Main 真实验收通过（关/开差异 3.35% 像素、水面 conf>0.1=57.8%）；遗留 C7 抗闪（命中呈椒盐斑点） |
| P1-1 | 昼夜系统 + 天空 | ✅ 主体 + 月面/月相/星空/银河（午夜实测月盘 403px、最亮 205；白天零回归）；缺双套渐变/LensFlare |
| P1-2 | 泡沫历史场（半拉格朗日平流） | ✅ Ocean 完成（关/开差异 8.00% 像素）；Main 无流场未接 |
| P2-1 | 水底散射 RayMarch（逐点主光阴影+逐级透射+有界混合） | ✅ Main+Pond（A/B 21.5%/28.7% 像素）；遗留：阴影定量/性能未测 |
| P2-2 | 多尺度焦散 + RGB 色散 | ✅ Pool（三层+去平铺+色散+乘性打底；关/开 18.6% 像素） |
| P2-3 | 体积雾 + 云影→水面高光 | ✅ 雾统一（水面/地形同式同密度）+ 云影可见性修复（正午 1.75% 像素）；遗留：掠射机位云影偏弱、雾无 3D 雾团、性能未测 |
| **风格-1** | **后处理链路（R08 G1）** | ✅ 修复 `WaterURPRenderer.postProcessData={fileID:0}`（全项目后处理一直是死的）；菜单 194 |
| **风格-2** | **参考调色档（R08 G2）** | ✅ ACES/曝光−0.18/对比+12/饱和−22/暖冷分离/Bloom/暗角；Islet 色 (112,192,211)→(93,137,147)，参考 (96,127,137)；菜单 190/191/192/193 |
| **风格-3** | **光比实验（R08 S1）** | ❌ 实测无效：同会话 A/B 对比度 44.6→44.2。直方图证明参考 0–40 近黑占 36%，我们仅 4–7% ⇒ **差距在构图不在光照** |
| **风格-4** | **构图（S2）** | ✅ 逆光 + FOV 50→26 + 俯角 −6° + 天光 0.11：近黑 4.6%→**20.2%**、对比度 45.8→**73.1**、饱和 131→**87**；菜单 198/199；暴露资产精度不足 |
| **风格-5** | **G6 免费资产方案（叶脉贴图 + 高频颗粒）** | ✅ 查实棕榈 `_BaseMap`=纯白（根本没贴图）；程序化生成 512² 叶脉图（8 条宽脉/14% 对比/alpha 恒 1 **不改轮廓**，可逆）；菜单 205/206；沙滩补 1.8cm 沙粒(`_GrainAmount` 207/208)、岩石补 11cm 麻点(`_MicroAmount` 209/210)，默认 0 零回归 |
| 阶段5-pool | 泳池交互线 7 件（波动方程水面/人物/球/跳水/水花/推挤/入水涟漪环） | ✅ 菜单 `Tools/Pool/90~100`；涟漪环必须画队列 3100（水面 3000 会盖掉它） |
| 阶段6-R1 | 3 秒逐帧对比量具（连帧采集/参考基准/seq_compare 四判据，自证 ρ≈1） | ✅ R09；参考云速实为**原地翻腾**（全局位移 0），热点法对沸腾云不稳定 |
| 阶段6-R2 | FFTScene 三级 FFT 海（δ谱自检色散 3.9%；choppy A/B 47.75%；vs S1 水面 4/4；调色后静帧三指标全带内） | ✅ R10/R14；菜单 `Tools/FFT/90~97`；模拟 3.3ms |
| 阶段6-R3 | 体积云+四层双色天空+双型 LensFlare+云影同源（云影 A/B 23.56%；性能 93%） | ✅ R11；菜单 `Tools/Sky/90~92`；**fp32 球壳求交会精度爆炸→平流层近似** |
| 阶段6-R4 | 3D 雾团+雾遮太阳（A/B 19.13%；S15 速度双过） | ✅ R12；菜单 `Tools/Ocean/180~182` |
| 阶段6-R5 | 小项清账（销账 2 / 结构性降级 4 带归因：1.33Hz=波动方程步进、URP14 属性名 renderPostProcessing） | ✅ R13；菜单 `Tools/Fix/591/592` |
| 阶段6-R6 | 总验收（15 段矩阵：FFT 系全过、老场景 60~75%；差距 G1~G6 全归因**待用户拍板**）+README 重写 | ✅ R14；**已推送 GitHub 定版（2026-10-24，8f074a3，经本机代理 127.0.0.1:7897）** |
| **R20** | **感官复刻修复**（用户"精细程度/噪点"批词） | ✅ ①588 扫出 Pool/Pond 两台相机 `renderPostProcessing=False`（后处理从未生效，R14~R19 的 Pool/Pond 后处理类数字全是关闭口径）②观看尺度颗粒 589 系（256² 周期值噪声 lookup 2~4px blob，六场景 intensity 0.5，S3 6×，盲评"像手机实拍"）③Pool 底黑网亮 584~587（水区 p50/p90 149/182 对齐参考 135/185）④SEQ95 阻塞静帧 |
| **R21** | **泳池涟漪手感 v2**（用户"涟漪手感不满意"） | ✅ 波速 3.98→实测 0.95~1.0 m/s（101 自测场回读）+双场 RG 色散(0.45 尾波)+环形冲量+反棋盘；环/水花/尾迹三增强（盲评环 8/10、水花 20~30 颗、动态 3→6/10）；**后台泵 93/94 落地**（失焦采集基础设施）；d77b7fa |
| **R22** | **SSR C7 抗闪完整版 + 性能体检** | ✅ SSRTemporalFeature 时域累积（_SSR_WRITE 预通道+EMA+查表，时序方差 -11%，盲评"噪点明显更少无鬼影"；583 装/583b 卸=回旧路径）；582 六场景帧耗上界 Main6.4/Ocean5.7/Islet13.9(最重=体积云)/Pool5.8/Pond6.5ms；fce0493 |
| **R23** | **四轮收官矩阵刷新 + 口径修正** | ✅ Main D 判据全保持（water E ρ0.763，SSR 时域无回归）；Pool D4 0.83Hz ρ1.0 **PASS**（1.33Hz 旧病治愈）；两条新口径：**D 判据测时颗粒必关**、**Pool/Pond 跨 R20 数字不与 R14 直接比**；R23 报告立账 |
| **R24** | **构图轮**（R23 头号遗留） | ✅ composition_diag 五场景诊断(中频超额9~13×/近黑缺口)；暗角档 581 系补近黑(Islet三项全中: 近黑47/均值62/S2-2.30 vs 参40.4/60/-2.23；Pool 38.4%、Main 40.3%)；S2 斜率对"游戏机位vs参考微距"的结构性边界记档；Islet D 回归 water E ρ0.861 PASS；126 FFTScene 菜单+4.4ms 补测；尾迹加强档 |
| **R25** | **云性能定档 + S2 边界终验** | ✅ 云步数 20 保档(8步省2.3ms但盲评'不可用':条带+锯齿;0.19ms/步,云占帧耗~29%,'最重=体积云'修正);Pool 微距机位实验(+0.06)+颗粒0.7(+0.03)——**取景假说证伪,S2 差距=内容频谱形状**(跨尺度纹理缩放不变),本线关闭;菜单 Sky/95、Pool/580(可逆) |## 6 交付纪律

每一步（P0-x / P1-x）都要产出：**参考侧逐帧量测 → 我方实测 → 对比表 → 判据通过/不通过 → 遗留风险**，
落到 `Docs/学习路径与知识库/检测报告/R0x_*.md`，并更新 `09` 的进度表。
小改动只做本地 commit；**大版本才推 GitHub**。

## 7 已知未解问题

1. 桥 `openScene` 崩溃（改用菜单 120~124 规避）。
2. `DayNightCycle` 的颜色曲线**不能用 `Gradient`**：本机实测被还原成默认白渐变（`colorKeys.Length` 读回 2、
   `Evaluate` 恒白）→ 已改朴素 `ColorKey[]` + 手写插值。新写曲线时别用 `Gradient`。
3. `ComputeScreenPos` 必须写 `o.zw = pos.zw`，漏掉会让整个屏幕空间采样错位（SSR 曾因此 conf 恒 0）。
4. 本机相机 `Euler x` 取**正**才是俯视（负值是抬头）。
5. **桥的 `getComponentProperties` 会让编辑器死锁**：主线程卡在 `EditorApplication.update → UnityBridgeServer.ProcessMainThreadQueue`，
   弹 "Hold on (busy for …)" 且不再处理任何命令。**不要用该 action**；要读相机/组件参数就写一个只读菜单（写探针）。
   恢复办法：`Stop-Process` 掉该 Unity PID → `python Tools/launch_unity.py`（会起新的编辑器实例，材质/场景无损）。
6. **`AssetDatabase.SaveAssets()` 会刷掉"全部脏资产"**（不只是你要存的那个）：A/B 菜单存水材质时，会把上轮 Play 遗留的
   昼夜色一起写回别的 `.mat`，于是 git 工作区反复出现"莫名其妙的颜色改动"。**只存一个用 `AssetDatabase.SaveAssetIfDirty(obj)`**。
7. **给暗场景做"发光类"效果（散射/泛光）别用 `col +=`**：加法无上界 → 暗场景里整片水变发光板（P2-1 的 Pond 实测）。
   用**有界混合** `col = lerp(col, target, w)`，w∈[0,1]。
8. **动画类效果（焦散/泡沫）的 A/B 必须在"同一个冻结会话"内连续截**：`Time.timeScale=0` 冻的是当时的相位，
   两次会话冻结在不同 `_Time.y` → 会凭空多出 60%+ 的差异（P2-2 实测）。同理，A/B 工具要把"模式"等观测状态**一起写**，
   只改参数不改模式会出现"两张图逐字节相同"的假象（P2-2 的 163 漏写 `_CausticMode`）。
   **天空/云也吃 `_Time.y`**：跨会话对比会差 0.06%~5%（R08 实测），凡是要下结论的 A/B 一律同会话。
9. **后处理链路必须先自证**：`WaterURPRenderer.asset` 的 `postProcessData` 曾经是 `{fileID:0}` ⇒ URP **静默跳过**
   整个后处理栈，Volume 里怎么调都没反应、截图逐位相同。已由菜单 `Tools/Style/194` 修好。
   新场景/新管线先跑 `193 诊断`，确认 `postProcessing=True` **且** Renderer 挂上了 PostProcessData。
10. **`RenderSettings.ambientIntensity` 只在 `AmbientMode.Skybox` 下有效**：改成 Trilight 后这个系数被忽略，
    "降天光"会变成"变亮"（R08 实测 153→164）。要么保持 Skybox 调系数，要么直接给三色定值。
11. **场景是二进制序列化**（本工程），**不能**靠 grep 场景 YAML 读相机/光照/Volume 参数（读到的是乱码）。
    要读就写一个**只读菜单**把值打进 `Logs/water_probe.txt`（样板：`StyleSetup.193/197`、`DayNightSetup.116`）。
12. **风格对标的结论（别走回头路）**：对比度差距**不是**光照/曲线问题。参考画面 0–40 近黑占 **36%**，
    我们只有 **4–7%** ⇒ 缺的是**画幅里的大面积暗色前景**。调色只能对齐色相与亮度，别再拧曝光/对比/天光。
    详见 `Docs/学习路径与知识库/检测报告/R08_风格对标差距.md`。
13. **`Assets/Art/Shaders/SSR_AntiFlicker.hlsl` 是未验证草稿**：没有任何 shader include 它（所以从未参与编译，
    `check_shader` 报 clean 并不能说明它是对的）。里面 `extern float4 _GradTexMip0;`、`extern float4 _GradTexMip1[8];`
    这种写法不是 URP 的纹理声明方式（应为 `TEXTURE2D(_GradTexMip0); SAMPLER(sampler_GradTexMip0);`），
    `uniform` 修饰符在 ShaderLab 里也不规范。~~要做 C7 抗闪，重写而不是直接接线~~
    **→ R22a 已重写完毕**：`Assets/Scripts/Render/SSRTemporalFeature.cs` + `SSR_TemporalEMA.shader` + `Water_Advanced` 的
    `_SSR_WRITE` 变体（菜单 583 装/583b 卸）。草稿文件仍未删，勿接线。
14. **编辑器失焦后 player loop 停摆**：桥进 Play 后 frame 卡在 2（runInBackground=True 也救不了）。
    R1~R19 的采集能跑是因为当时 Unity 窗口恰好在前台。**无人值守采集必须用后台泵**：
    `Tools/Seq/93`（每命令步进一拍）/`Tools/Seq/94`（阻塞抽干 SeqCapture）。⚠ 泵≈50fps 与固定率
    累加器类模拟（如 PoolRipples 的 60Hz）有**拍频伪影**（7.5Hz 假闪烁），严格 D4 要前台 60fps 采集。
15. **本项目 Unity 实例的日志在 `E:/Blender/tools/unity_launch.log`**（launch_unity.py 带 `-logFile` 启动）。
    `%LOCALAPPDATA%/Unity/Editor/Editor.log` 属于**用户同时开着的另一个 Unity 实例**（别的项目）——
    排障读错日志会得出"编译全绿"的假象（R22a Play 秒退查了三轮才抓到真编译错误）。
16. **验收口径（R23 起）**：①D 判据（运动能量/闪烁）测量时**颗粒必须关**（590）——颗粒逐帧随机偏移
    会把 D1 抬 10~30×；②R20 修复相机后处理前后的 Pool/Pond 数字**不可直接比**（那两台相机当时是死的）。
17. **菜单里不能阻塞等 Play 转换**（`isPlaying=true` 是异步的，主线程 Sleep 等它=自死锁，R582 首版全灭）。
    编排模式：bash 逐场景 [开场景 → Play/90 → sleep → 目标菜单 → Play/91]。C# 里阻塞 Step 循环是安全的（93/94 已验证）。
