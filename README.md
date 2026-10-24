# Unity3D 风景项目 —— 水体 Shader 完全复刻

> 目标：复刻抖音作者「雨鱼鱼雨雨雨」《unity 水体 shader》视频的**全部**效果，并产出
> 零基础能读懂的文档与可复现流程。参考视频 15 章 5568 帧已逐帧量测（见 `Docs/视频逐帧/`，
> 不入库），每一轮开发都有"参考侧量测 → 我方实测 → 对比表 → 判据通过/不通过"的验收报告。

## 一、项目信息

| 项目 | 内容 |
| ---- | ---- |
| Unity 版本 | 2022.3.57f1c2（LTS） |
| 渲染管线 | URP 14.0.11 |
| 桥 | `Assets/Editor/UnityBridge/`（WebSocket `ws://127.0.0.1:6400`，编辑器打开即启动） |
| 版本管理 | git（大版本推 GitHub，小版本本地提交） |

## 二、六个场景，各看什么

| 场景 | 内容 | 对应参考视频 |
| ---- | ---- | ---- |
| `FFTScene` | **FFT 数学海**：三级频谱海（250/60/15m）、choppy 开关、浪尖泡沫、单层焦散演示 | S1/S2/S5/S7 |
| `OceanScene` | 海岸主场景：近岸浅水流场、泡沫历史场、SSR、**体积云+四层双色天空+双型耀斑**、昼夜、3D 雾团+雾遮太阳、云影 | S3/S9~S15 |
| `Main` | 主线水塘：Gerstner 六波、屏幕折射、水底散射、交互涟漪 | S3/S4 |
| `PondScene` | 池塘：水底 RayMarch 散射 | S6 |
| `PoolScene` | 泳池：波动方程水面+多尺度焦散+**第三人称人物/沙滩球/跳水涟漪**（交互演示线） | S8 + 交互 |
| `IsletScene` | 环礁岛：潟湖水色、地形、棕榈/巨岩、构图档 | S0/S12 |

## 三、零基础快速开始（五步看到效果）

1. 用 Unity 2022.3.57 打开本工程（首次导入需等待编译）；
2. 顶部菜单 `Tools/Project/121` 打开 OceanScene（其余场景 120~124 同理）；
3. `Tools/Play/90` 进入播放——即可看到海面、体积云、雾团与昼夜（`Tools/Ocean/111~114` 切时段）；
4. 想看 FFT 数学海：`Tools/Project/125`→FFTScene 不存在则 `Tools/FFT/90` 建场景 → 播放；
5. 想玩泳池交互：`Tools/Project/123` 打开 PoolScene → `Tools/Pool/92/93/94` 建水面/人物/球 → 播放后 WASD 移动、空格跳水、B 扔球、鼠标点水。

**验收/取证菜单**：`Tools/Seq/91`（3 秒连帧采集）→ `python Tools/Video/seq_compare.py`
（对参考视频逐帧四判据，用法见 `--help` 与 R09 报告）。

## 四、复现手册（每场景的最小菜单路径）

> 全部操作入口都在 `Tools/` 菜单（唯一写入点纪律：参数只由菜单/脚本写，不手改 Inspector）。

| 场景 | 建场景 | 关键演示开关 | 取证 |
| ---- | ---- | ---- | ---- |
| FFTScene | `FFT/90` | choppy `FFT/91`、焦酸地板 `FFT/94`、自检 `FFT/92` | `FFT/95` |
| OceanScene | `Ocean/80`（骨架）+`Ocean/110`（昼夜）+`Ocean/170`（云影）+`Ocean/180`（雾团） | 时段 111~114、云影 171/172、雾 173/174、雾遮太阳 181/182 | `Ocean/85` |
| Main | `Tools/WaterProject/` 系列（见菜单） | — | 菜单 65 |
| PondScene | `WaterPond/60~67` | — | `WaterPond/6x` 截图 |
| PoolScene | `Pool/90~94`（骨架/池体/水面/人物/球） | 巡游 97、抛球 98、跳水 99、涟漪环 100 | `Pool/95` |
| IsletScene | `Islet/90~98` | 构图 198/199（Tools/Style） | `Islet/95b` |

风格调色：`Tools/Style/194`（修复后处理链）→ `190`（参考调色档）→ `193`（诊断）。
参数包：`Tools/Fix/591`（四水面节奏）、`592`（FFT 调色微调）、`Tools/Sky/90`（体积云参数）。

## 五、文档地图

| 文档 | 内容 |
| ---- | ---- |
| `AGENTS.md` | **接手须知**（铁律/目录/命令/进度）——新会话先读这个 |
| `Docs/学习路径与知识库/08_参考视频知识点全解.md` | 参考视频 15 章 38 卡逐卡转录（事实源） |
| `Docs/学习路径与知识库/10_完全复刻策划案与验收标准.md` | 阶段6 总案（六大步骤 + 3 秒逐帧对比协议 + 小项登记表） |
| `Docs/学习路径与知识库/检测报告/R01~R14` | 每轮验收报告（判据数字，不是目测） |
| `Docs/版本更新计划.md` | 全部轮次的提交流水（一行一轮） |
| `Docs/外接组件记录.md` | 外部素材来源与许可（含 CC0 人物/植被） |

## 六、目录规范

```
Assets/
  Art/Shaders/    全部水体/天空/后处理 shader（Water_*/Sky_Coast/FFTOcean.compute…）
  Art/Materials/  材质（按场景分文件夹）
  Scripts/Logic/  模拟与管理（FFTOceanSim/PoolRipples/DayNightCycle…）
  Scripts/MonoBehaviour/ 表现层脚本
  Editor/         全部编辑器菜单 + UnityBridge
Scenes/           Main/OceanScene/PondScene/PoolScene/IsletScene/FFTScene
Tools/            桥客户端/连拍/逐帧量测/合成工具（python）
Docs/             文档与验收报告
```
