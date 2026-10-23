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

场景：`Main`（主线水塘）、`OceanScene`（海岸，当前主力）、`PondScene`、`PoolScene`、`IsletScene`。

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

## 5 当前进度（2026-10-23 同步核实）

> ⚠ 上一版头部曾写「风格优化大模块融合完成」，但**仓库里没有对应代码/报告/提交**（只有一行标题被改）。
> 实际状态以下表为准：P0–P2 全绿；风格只完成 G1（后处理链路）+ G2（调色档），**G3 构图未做**。
> 另有一个**未接线的草稿** `Assets/Art/Shaders/SSR_AntiFlicker.hlsl`（没有任何 shader include 它，
> 因此从未参与编译；且写法有疑点，勿直接 include，见 §7 第 13 条）。

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

## 6 交付纪律

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
    `uniform` 修饰符在 ShaderLab 里也不规范。**要做 C7 抗闪，重写而不是直接接线**；接线前必须先 `-Mode mark` 再编译验证。
