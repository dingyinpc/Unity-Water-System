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
