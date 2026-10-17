# Tools/Video —— 视频逐帧分析流水线

> 用于对参考视频（博主授权素材）做【逐帧】分析：**不抽帧、不抽样** ——
> 每一帧都落图（`frames/`），每一帧都出量测（`analysis/metrics.csv`），
> 再生成时间序列图、切镜表与接触表，支撑"对照 → 复刻 → 验收"闭环。
> 依赖：Python 3.12 + numpy + opencv-python + Pillow（本机已装，无需网络）。

## 四步流水线

| 步骤 | 脚本 | 作用 |
| ---- | ---- | ---- |
| 0 | `probe_video.py` | 探元数据（fps / 帧数 / 分辨率 / 时长） |
| 1 | `extract_all_frames.py` | **全帧导出**：每一帧一张图 + `index.csv`（帧号↔时间↔文件） |
| 2 | `analyze_frames.py` | **逐帧量测**：亮度 / 帧间差 / 全局 RGB / 亮部占比 /［分区框 RGB］/［色相分桶］→ `metrics.csv` + `plots.png` + `cuts.json` + `summary.txt` |
| 3 | `contact_sheet.py` | 接触表拼图（每 N 帧取 1 张，快速纵览 / 供 AI 视觉查看） |
| 4 | `extract_subtitles.py` | **烧录字幕卡提取**：白字掩码→分段→去背景→过滤→合并 → `card_*.png` + `strip_XX.jpg` 总览条 + `segments.csv`（帧级时间戳） |

## 字幕卡提取（步骤 4，v3）

```powershell
python Tools/Video/extract_subtitles.py "Docs/参考视频/原片.mp4" --out "Docs/视频逐帧/<视频名>/subtitles"
```

- **原理**（参数都写死在脚本里，按需再调）：
  1. 白字掩码：`min(B,G,R) > 150`（专抓黑描边白字，不挑底色）；
  2. 密度签名：160×12 分段（THR 0.08），逐帧比对检测字幕出现/消失；
  3. 段内时间中值：取 ≤16 帧中值帧，直接抹掉背景（亮背景、水花全消失）；
  4. 连通域过滤：保留 3~700px 笔画，剔除大团块（标题残留）与单像素噪点；
  5. 相邻段合并：掩码 MAE<0.035 且间隔 <1.5s（解决半透明条/伪分裂）。
- **产物**：`card_NN.png`（每段一张干净二值卡）、`strip_XX.jpg`（每 10 段拼一条总览，供快速通读）、`segments.csv`（段号/起止秒/帧号）。
- 用卡转写后请回填知识库（如 `08_参考视频知识点全解.md`），卡与全帧同理**不入库**。

## 约定

- **中文路径完全支持**（内部走 `imdecode/imencode`；非 ASCII 视频源自动拷贝到临时 ASCII 路径再解码）。
- 默认 JPG q95 全帧（体积≈原片 1/3~1/2）；需要像素级无损时用 `--format png`，或只对某区间重抽：
  `--format png --start 10.0 --end 12.0`。
- 建议输出目录：`Docs/视频逐帧/<视频名>/`（内含 `frames/` + `analysis/` + `sheets/`）；
  该目录已在 `.gitignore` 中**整体忽略**（版权：授权仅限学习对照、不二次分发）；
  结论与关键量测请回填到 `Docs/学习路径与知识库/` 文档（入库）。
- 全局指标异常时，先看 `analysis/summary.txt` 的 Top10 差异帧，再去 `frames/` 挑单帧复核；
  单帧静态取色可用 `Tools/sample_png.ps1`（出均值+中位数）。
- 后续深挖（按需再写脚本）：波峰线逐帧追踪、涟漪半径-时间拟合、相机运动估计、我方渲染 vs 参考逐帧并排。

## 完整一轮示例

```powershell
python Tools/Video/probe_video.py        "Docs/视频逐帧/ref/原片.mp4"
python Tools/Video/extract_all_frames.py "Docs/视频逐帧/ref/原片.mp4" --out "Docs/视频逐帧/ref"
python Tools/Video/analyze_frames.py     "Docs/视频逐帧/ref" --boxes "水面:0,40,100,80;天空:0,0,100,30" --hue
python Tools/Video/contact_sheet.py      "Docs/视频逐帧/ref" --step 30
```
