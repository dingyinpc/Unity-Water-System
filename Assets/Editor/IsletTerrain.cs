// ============================================================================
// IsletTerrain.cs — 第四条水线的陆地：全部用 Unity **原生 Terrain** 承载
//
// 为什么是 Terrain 而不是像海岸线那样自己造网格（用户口径："尽量使用 unity 的
// 原生功能，场景物件才用 Blender 建模"）：
//   潟湖的水深判据是"这一格水有多深"，而海岸线那条是把岸线写成 z=f(x) 的解析式
//   再在 shader 里重算一遍。环礁做不到：这里有沙洲、岬角、水下沙架，岸线不是
//   单值函数。一旦解析式在 C# 和 HLSL 各写一份，两份就会漂（《05》里海岸线湿沙
//   边界漂移的根因）。Terrain 的高度场是**唯一一份真实几何**，水体改成从相机深度
//   图取水下表面高度（Water_Pond / Water_Advanced 已验证过这条路），从机制上
//   消灭了"同一个物理量写两遍"。
//   附带好处：分层混合贴图（splat）、按坡度自动铺岩、大尺度 LOD 都是引擎给的。
//
// 对标：Docs/参考视频帧/参考1.png（00:02 的定场镜头，六张里构图最"平视"的一张，
//   所以拿它当机位与布局的基准；参考6 是仰拍，只当云形参考）。
//   画面里从左到右：近景左侧一座两棵椰树的小沙洲 → 中景一条几乎贴水的浅色沙脊
//   → 远处地平线上几座极小的椰林沙洲 → 右侧一座带海蚀巨岩的大岛。
//
// 高度场怎么"造得像"：
//   不用"一堆高斯堆"，而是**先定岸线，再定剖面**。每座岛给一条带扰动的星形
//   （star-shaped）闭合岸线，算出带符号的"离岸距离" s（正=岸内），
//   高度 = ShoreProfile(s)。于是：
//     · 水线**恰好**落在 s=0，不用事后找等高面；
//     · 水下沙架的宽度/坡度是显式参数 —— 参考里近岸那条亮青绿浅带就是它，
//       是画面成败的关键，不能靠噪声碰运气；
//     · 海岸线自然弯曲，且天然支持岬角（半径按角度调制）。
//
// 菜单号 91；90 建骨架、92 水面、93 摆 Blender 资产、94 全链、95 取图、96 天空调试。
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class IsletTerrain
    {
        const string ScenePath   = "Assets/Scenes/IsletScene.unity";
        const string TexFolder   = "Assets/Art/Textures/Islet";
        const string LayerFolder = "Assets/Art/TerrainLayers/Islet";
        const string TerrainName = "IsletTerrain";

        // ───────── Terrain 尺寸 ─────────
        // 512m 见方、高度量程 40m（世界 y ∈ [-12, +28]）。
        // 高度图 1025 ⇒ 纹素 0.5m：近景那座半径约 4m 的英雄沙洲要经得起贴脸看，
        // 1m 采样会在它边缘看出折线。用户明确"不考虑性能"，所以 pixelError 压到 1。
        // splat 用 513（1m/格）：贴图混合不需要高度那么密，而 splat 每格要算 5 次
        // LandHeight 取坡度，1025 会把编辑器冻住一分多钟。
        const int   HRes    = 1025;
        const int   SplatRes = 513;
        const float SizeXZ  = 512f;
        const float SizeY   = 40f;
        const float BaseY   = -12f;          // 世界 y = BaseY + h01 * SizeY
        // Terrain 左下角的世界坐标：让相机 (0,1.72,-34) 落在 x 居中、z 靠前 1/5 处
        static readonly Vector3 Origin = new Vector3(-SizeXZ * 0.5f, BaseY, -120f);

        const float RimFalloff = 46f;        // 距边界这么近一律压到水下

        // 混合层普查（每次 BuildTerrain 重置）：主层占比 + 陆地主层占比
        static readonly int[] s_win = new int[8];
        static readonly int[] s_winLand = new int[8];
        static int layersCount = 4;

        // ═════════════════════════════════════════════════════════ 91
        [MenuItem("Tools/Islet/91 建地形(原生Terrain：环礁+沙洲+右侧海蚀大岛)")]
        public static void BuildTerrain()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU91 REFUSED: 在 Play 模式，先跑 Tools/Play/91 退出播放\n"); return; }
            if (!EnsureScene()) return;

            // 重建前清掉旧的：TerrainData 是 ScriptableObject，光 Destroy GameObject
            // 会留下一个没人引用的 .asset，下次按名撞上就改不动。
            var old = UnityEngine.GameObject.Find(TerrainName);
            if (old != null) Object.DestroyImmediate(old);

            try
            {
                // ⚠ TerrainData **不是** ScriptableObject（2022.3 的 TerrainModule.xml 里
                //   TerrainData 不继承 ScriptableObject），所以 CreateInstance<TerrainData>()
                //   根本编译不过 —— 直接 new。
                // 同理 heightmapPixelError / basemapDistance 挂在 **Terrain 组件**上，
                // 不在 TerrainData 上（这两个是最容易被旧教程带偏的一对）。
                var data = new TerrainData();
                data.heightmapResolution = HRes;
                data.size = new Vector3(SizeXZ, SizeY, SizeXZ);

                // ── 高度 ──
                float step = SizeXZ / (HRes - 1);
                var h = new float[HRes, HRes];     // [zIndex, xIndex]
                float minW = 1e9f, maxW = -1e9f;
                // 深度直方图：只看"多少格在水上"根本诊断不了问题（第一版 57% 落在
                // 潮间带，但不知道是沙架太宽还是潟湖底被抬平，只能靠再猜一轮）。
                // 分桶按米给，单位是占总格数的百分比。
                var hist = new int[8];
                for (int zi = 0; zi < HRes; zi++)
                {
                    EditorUtility.DisplayProgressBar("Islet 地形", "高度场 " + zi + "/" + HRes,
                                                     (float)zi / HRes * 0.55f);
                    float wz = Origin.z + zi * step;
                    for (int xi = 0; xi < HRes; xi++)
                    {
                        float wx = Origin.x + xi * step;
                        float y = LandHeight(wx, wz);
                        if (y < minW) minW = y;
                        if (y > maxW) maxW = y;
                        hist[y < -6f ? 0 : y < -4f ? 1 : y < -2.5f ? 2 : y < -1.4f ? 3
                             : y < 0f ? 4 : y < 1f ? 5 : y < 5f ? 6 : 7]++;
                        h[zi, xi] = Mathf.Clamp01((y - BaseY) / SizeY);
                    }
                }
                data.SetHeights(0, 0, h);

                // ── 分层贴图 ──
                EditorUtility.DisplayProgressBar("Islet 地形", "生成沙/岩/草贴图", 0.62f);
                var layers = BuildLayers();
                data.terrainLayers = layers;
                data.alphamapResolution = SplatRes;
                data.baseMapResolution  = 2049;   // (2049-1) % (513-1) == 0，Terrain 要求的整倍关系
                int ar = data.alphamapResolution;

                EditorUtility.DisplayProgressBar("Islet 地形", "按高度与坡度撒混合权重", 0.70f);
                layersCount = layers.Length;
                System.Array.Clear(s_win, 0, s_win.Length);
                System.Array.Clear(s_winLand, 0, s_winLand.Length);
                float astep = SizeXZ / (ar - 1);
                var w = new float[ar, ar, layers.Length];
                for (int zi = 0; zi < ar; zi++)
                {
                    float wz = Origin.z + zi * astep;
                    for (int xi = 0; xi < ar; xi++)
                    {
                        float wx = Origin.x + xi * astep;
                        SplatAt(wx, wz, astep, w, xi, zi);
                    }
                }
                // SetAlphamaps 只有 (xBase, yBase, float[,,]) 三参重载，宽高从数组拿
                data.SetAlphamaps(0, 0, w);

                // ── 挂到场景 ──
                // CreateTerrainGameObject 返回的是 **GameObject**，不是 Terrain；
                // pixelError / basemapDistance / drawTreesAndFoliage 全在 Terrain 组件上。
                var go = Terrain.CreateTerrainGameObject(data);
                go.name = TerrainName;
                go.transform.position = Origin;
                var terr = go.GetComponent<Terrain>();
                terr.heightmapPixelError = 1f;    // 默认 5；近景沙洲要贴脸看，用户说不考虑性能
                terr.basemapDistance = 260f;      // 超过这个距离切 1/4 分辨率底图
                terr.drawTreesAndFoliage = false; // 没有树/草原型，省得每帧警告
                var col = go.GetComponent<TerrainCollider>();
                if (col != null) Object.DestroyImmediate(col);  // 纯观赏场景，1025² 碰撞体白建

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

                var sb = new System.Text.StringBuilder();
                string[] bins = { "<-6", "-6..-4", "-4..-2.5", "-2.5..-1.4",
                                  "-1.4..0", "0..1", "1..5", ">5" };
                for (int i = 0; i < bins.Length; i++)
                    sb.Append(' ').Append(bins[i]).Append('=')
                      .Append((100f * hist[i] / (HRes * HRes)).ToString("0.0")).Append('%');

                int landPx = 0;
                for (int i = 0; i < layers.Length; i++) landPx += s_winLand[i];
                sb.Append("  |layer:");
                for (int i = 0; i < layers.Length; i++)
                    sb.Append(' ').Append(layers[i].name).Append('=')
                      .Append((100f * s_win[i] / (ar * ar)).ToString("0.0")).Append('%')
                      .Append("/land").Append((100f * s_winLand[i] / Mathf.Max(landPx, 1)).ToString("0.0")).Append('%');

                WriteProbe("MENU91 terrain built  size=" + SizeXZ + "x" + SizeY + "x" + SizeXZ +
                           "  hmRes=" + HRes + "  texel=" + step.ToString("0.000") + "m" +
                           "  y=[" + minW.ToString("0.0") + "," + maxW.ToString("0.0") + "]" +
                           "  depth:" + sb +
                           "  splatRes=" + data.alphamapResolution + "  baseMapRes=" + data.baseMapResolution +
                           "  layers=" + layers.Length + "  -> " + ScenePath + "\n");
                ProbePoints();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ═════════════════════════════════════════════════════════ 高度场

        /// <summary>世界 (x,z) 处的地面高度（米，水面 = 0）。全场唯一一份地形真值。</summary>
        public static float LandHeight(float x, float z)
        {
            float y = LagoonFloor(x, z);

            // 每座岛/沙洲取 max：它们的沙架会互相搭到对方身上，加法会堆出不存在的礁。
            y = Mathf.Max(y, IsleNearLeft(x, z));
            y = Mathf.Max(y, SandbarMid(x, z));
            y = Mathf.Max(y, BigIslandRight(x, z));
            y = Mathf.Max(y, DistantBar(x, z,  22f, 236f, 15f, 1.15f, 26f, 2.2f));
            y = Mathf.Max(y, DistantBar(x, z, -46f, 268f, 19f, 0.95f, 30f, 2.6f));
            y = Mathf.Max(y, DistantBar(x, z,  96f, 300f, 24f, 1.60f, 34f, 3.0f));
            y = Mathf.Max(y, FarLeftHeadland(x, z));

            // 边界收口：整圈压到水面以下，这样 Terrain 的边不会在地平线上切出一道直边。
            float rim = Mathf.Min(Mathf.Min(x - Origin.x, Origin.x + SizeXZ - x),
                                  Mathf.Min(z - Origin.z, Origin.z + SizeXZ - z));
            if (rim < RimFalloff)
                y = Mathf.Min(y, Mathf.Lerp(-7.5f, -3.0f, Mathf.Clamp01(rim / RimFalloff)));

            return y;
        }

        /// <summary>
        /// 潟湖底。**方向和我第一版的直觉相反**：
        ///   我写的是"近处浅、远处深"（-2.5 → -6.8），理由是"前景要能看清水下"。
        ///   结果中远景水全成了暗蓝 (62,95,122)，而参考1 恰恰相反 ——
        ///   近处 (91,164,183) 是**最深**的一段青绿，越往地平线越**淡**
        ///   (145,207,213) → (210,243,217)。因为那是一片**向外延伸的浅礁盘**，
        ///   不是外海：水越远越浅，白沙底把光整片返上来。
        /// 所以这里做成"近深远浅"：相机脚前 -2.6m，远处礁盘抬到 -0.9m。
        /// 沙波噪声幅度也从 0.31 降到 0.22 —— 浅架上一旦起伏超过半米，
        /// 折射搬上来的水底就会把整片淡青打成花。
        /// </summary>
        static float LagoonFloor(float x, float z)
        {
            float t = Mathf.Clamp01((z + 120f) / 470f);
            float y = Mathf.Lerp(-2.6f, -0.9f, Mathf.Sqrt(t));
            y += 0.22f * (Fbm(x * 0.019f, z * 0.019f, 3) - 0.5f);
            y += 0.13f * (Fbm(x * 0.082f + 11f, z * 0.082f - 7f, 2) - 0.5f);
            return y;
        }

        /// <summary>
        /// 近景左侧英雄沙洲：两棵椰树站的那座。
        /// 位置是**按取景反推**的，不是试出来的。相机在 (0,1.72,-34)、竖 fov 50°、
        /// 宽高比 1.53 ⇒ 横半视角 35.5°。参考1 里沙洲中心在 u≈0.18（偏左约 0.32 个半幅），
        /// 占画面宽约 26°。取距离 26m ⇒ 横向偏移 = 26·tan(0.32×35.5°) ≈ 11m，
        /// 半宽 = 26·tan13° ≈ 6m ⇒ r 取 7.0（留一点岸线调制余量）。
        /// 上一版放在 cz=-19，离相机只有 15m，r=9.5 的穹顶直接糊掉左半屏。
        /// </summary>
        static float IsleNearLeft(float x, float z)
        {
            const float cx = -11f, cz = -8f, r = 7.0f;
            float dx = x - cx, dz = z - cz;
            float rr  = Mathf.Sqrt(dx * dx + dz * dz);
            // 岸线不是正圆：用低频 fbm 沿角度调制半径，做出被水掏出来的凹口
            float shape = ShoreShape(dx, dz, rr, r, 0.30f, 1.6f, 3f);
            // 参考1 的沙洲是**又宽又扁的一座沙丘**（约 14m 宽、2m 高，顶上两棵椰树），
            // 不是尖顶火山 —— 所以 peak 压得很低、beachW 相对很宽。
            // ⚠ smoothstep 峰值导数 = 1.5*peak/innerW。r 缩到 7 之后 innerW 也只能给到 7，
            //   此时 1.5*2.0/7 = 0.43，仍在岩石门限 0.55 之下 —— 再高或再窄，
            //   洲顶就会被判成海蚀岩（第三版实测过那个失败态）。
            float s = r * shape - rr;
            float h = ShoreProfile(s, 12f, 4.5f, 2.00f, 7.0f, 2.4f);
            // 洲顶偏一侧鼓一点，免得像个馒头
            h += 0.50f * Mathf.Exp(-((dx + 1.5f) * (dx + 1.5f) + (dz - 1.0f) * (dz - 1.0f)) / 16f);
            return h;
        }

        static float ang(float dx, float dz, float rr)
            => rr > 1e-5f ? Mathf.Atan2(dz, dx) : 0f;

        /// <summary>
        /// 岸线半径的角向调制（让岛不是正圆）。
        /// ⚠ **调制必须随 rr→0 衰减归零**，否则岛心会凭空长出一根尖刺：
        ///   a = atan2(dz,dx) 在原点是不连续的，往外挪 0.5m 角度就能翻 180°，
        ///   于是 `shape` 在岛心附近剧烈抖动，高度跟着抖。
        ///   实测证据：islet crest 用 0.5m 差分算出的坡度 ≥0.90，
        ///   而用 1m 差分只有 0.58 —— 差一个步长就差一倍，说明那是**奇点**不是地形。
        ///   后果是洲顶被 rock 通道整个吃掉（raw rock=0.47、dry/grass 全 0），
        ///   一座该是"宽扁沙丘"的沙洲渲成了尖顶白火山。
        /// fade 用 (rr / 0.55r)² —— 半个半径以内就基本归零，岸线那边仍是满调制。
        /// </summary>
        static float ShoreShape(float dx, float dz, float rr, float r,
                                float amp, float freq, float seed)
        {
            float a = ang(dx, dz, rr);
            float fade = Mathf.Clamp01(rr / (r * 0.55f));
            fade *= fade;
            return 1f + amp * fade * (Fbm(Mathf.Cos(a) * freq + seed,
                                          Mathf.Sin(a) * freq - seed, 3) - 0.5f);
        }

        /// <summary>中景那条几乎贴水的浅色沙脊 —— 参考1 里画面中央的亮带就是它。</summary>
        static float SandbarMid(float x, float z)
        {
            const float cx = -3.5f, cz = 14f;
            float dx = (x - cx) * 0.42f, dz = (z - cz) * 1.55f;   // 沿 x 拉长的椭圆
            float rr = Mathf.Sqrt(dx * dx + dz * dz);
            float shape = ShoreShape(dx, dz, rr, 9.5f, 0.34f, 2.3f, -5f);
            float s = 9.5f * shape - rr;
            // 顶只到 +0.16m：露一寸是"沙洲"，全淹是"水下亮斑"，参考里两者都有
            return ShoreProfile(s, 16f, 4.2f, 0.16f, 3.2f, 1.1f);
        }

        /// <summary>右侧大岛：宽沙滩 + 草坡台地 + 一座陡峭的海蚀巨岩核。</summary>
        static float BigIslandRight(float x, float z)
        {
            const float cx = 52f, cz = 104f, r = 44f;
            float dx = x - cx, dz = z - cz;
            float rr = Mathf.Sqrt(dx * dx + dz * dz);
            float shape = ShoreShape(dx, dz, rr, r, 0.26f, 1.25f, 7f);
            float s = r * shape - rr;

            // 内坡 innerW 6→16：上一版 6m 内就从 0 冲到 6m，平均坡度 1.0，
            // 整座岛的肩膀全都比草的坡度门限陡 ⇒ 全被判成岩石，渲成一坨青灰。
            // 摊到 16m 肩膀只有 ~0.4，草才拿得到该属于它的那片缓坡。
            float h = ShoreProfile(s, 34f, 26f, 5.0f, 16f, 3.4f);
            // ShoreProfile 的 smoothstep 到 innerW 就饱和，r=44 的岛中间几十米全是
            // 一个平面 —— 渲出来是张"桌子"。这里补一段继续往里抬的坡，让轮廓收向岩核。
            h += 4.0f * Mathf.Clamp01((s - 34f) / 22f);

            // 岩核：pow(...,0.40) 让边缘导数发散 ⇒ 近垂直的海蚀壁，而不是一个土包。
            // ⚠ 半径 27→18 且**往远离相机的方向挪**（+x/+z）。上一版核半径 27
            //   占 r=44 的岛的 (27/44)²≈38% 面积，又偏朝相机，于是从相机看过去
            //   整岛就是岩石，参考里明明是"前绿后岩"：岩壁在岛背后露头，
            //   朝水的一面是植被覆盖的缓肩膀 + 一圈白沙滩。
            float rx = x - (cx + 12f), rz = z - (cz + 26f);
            float rd = Mathf.Sqrt(rx * rx + rz * rz) / 18f;
            if (rd < 1f)
            {
                float core = Mathf.Pow(1f - rd * rd, 0.40f);
                // 棱脊噪声：1-|fbm-0.5| 的折叠，出来的是一沟一沟的侵蚀面
                float ridge = 1f - Mathf.Abs(Fbm(x * 0.075f + 2f, z * 0.075f - 3f, 3) - 0.5f) * 2f;
                h += 14.0f * core * (0.72f + 0.52f * ridge);
            }
            return h;
        }

        /// <summary>地平线上的小椰林沙洲：低、薄，只负责"远处有陆"这件事。</summary>
        static float DistantBar(float x, float z, float cx, float cz, float r,
                                float peak, float shelf, float beach)
        {
            float dx = (x - cx) * 0.62f, dz = (z - cz) * 1.4f;
            float rr = Mathf.Sqrt(dx * dx + dz * dz);
            float shape = ShoreShape(dx, dz, rr, r, 0.28f, 1.9f, cx * 0.1f + cz * 0.07f);
            float s = r * shape - rr;
            // innerW 跟着 beach 走，别写死 1.2：写死会让 1~2m 的洲顶导数冲到 2.0，
            // 地平线上那几座椰林沙洲全变成黑岩石。
            return ShoreProfile(s, shelf, beach, peak, beach * 0.8f, 0.9f);
        }

        /// <summary>画面左角外的一道远岸，只用来封住左侧地平线，别抢参考1 的开阔水面。</summary>
        static float FarLeftHeadland(float x, float z)
        {
            float dx = (x + 238f) * 0.5f, dz = (z - 318f) * 1.1f;
            float rr = Mathf.Sqrt(dx * dx + dz * dz);
            float s = 44f - rr;
            return ShoreProfile(s, 26f, 22f, 3.2f, 7f, 1.8f);
        }

        /// <summary>
        /// 岸线剖面：s=0 就是水线（高度恰好 0），所以水陆边界由岸线形状**直接**决定。
        /// 水下是宽而缓的沙架（指数>1 ⇒ 贴着岸边几乎不降，越外越陡），
        /// 水上是先缓（沙滩）后陡（草坡）。
        /// </summary>
        static float ShoreProfile(float s, float shelfW, float beachW,
                                  float peak, float innerW, float shelfDrop)
        {
            if (s < 0f)
            {
                // ⚠ t **不能** Clamp01。夹住的话沙架外就是一整片 -shelfDrop 的水平面，
                //   而各座岛是用 max() 合成的 —— 于是全场被"最浅的那座沙洲"抬到
                //   -0.9m，整个潟湖底凭空变成一张浅滩（第一版实测 57% 的地形落在
                //   潮间带，就是这个原因）。不夹 → 越过架宽继续按 1.35 次幂下切。
                float t = -s / shelfW;
                return -shelfDrop * Mathf.Pow(t, 1.35f);
            }
            float u = Mathf.Clamp01(s / beachW);
            float v = Mathf.Clamp01((s - beachW * 0.55f) / innerW);
            return beachW * 0.055f * u + peak * (v * v * (3f - 2f * v));
        }

        // SplatAt 最近一次的**原始规则值**。归一化之后的权重会骗人：
        // 上一版探针只打归一化值，看到 `RK=1.00` 却推不出是 rock 真的赢，
        // 还是其它几项同时塌到 0 —— 这两者的修法完全相反。
        static float s_lastUnder, s_lastWet, s_lastDry, s_lastRock, s_lastGrass;

        // 定点探针：**别再靠猜屏幕坐标去读截图了。**
        // 前面好几轮都是"在 u=0.185,v=0.545 取到 (116,127,131)，于是断定洲顶是蓝灰"，
        // 结果那个像素根本不是洲顶 —— 低机位 + 50° 竖视角下，一座 26m 外、
        // 2m 高的沙洲在画面上只占十几个像素，UV 差 0.02 就从洲顶掉到天上/水里。
        // 所以直接把世界坐标喂给同一套 SplatAt，量出来的是什么层就是什么层。
        static void ProbePoints()
        {
            // 大岛中心 (52,104)、r=44 ⇒ "离岸距离 s" 的点在 z = 104-(44-s) = 60+s。
            // 上一版这里写的是 z=66/70/60 却标成 "s=14 / s=26 / beach"，
            // 三个点其实全落在水线附近，标签纯属编的 —— 所以量出来的
            // "大岛没有草"是**取样取错了**，不是规则错了。
            var pts = new (string, float, float)[]
            {
                ("islet crest   ", -11f,  -8f),
                ("islet flank N ", -11f,  -3f),
                ("islet flank S ", -11f, -13f),
                ("islet beach E ",  -3f,  -8f),
                ("big s= 4      ",  52f,  64f),
                ("big s=14      ",  52f,  74f),
                ("big s=26      ",  52f,  86f),
                ("big s=40      ",  52f, 100f),
                ("big core      ",  64f, 130f),
                ("lagoon mid    ",   0f,  60f),
                ("lagoon far    ",   0f, 200f),
            };
            var sb = new System.Text.StringBuilder();
            sb.Append("MENU91 points  raw=under/wet/dry/rock/grass  norm=SD SW RK GR\n");
            foreach (var (nm, px, pz) in pts)
            {
                float y = LandHeight(px, pz);
                float st = 1.0f;
                float sx = (LandHeight(px + st, pz) - LandHeight(px - st, pz)) / (2f * st);
                float sz = (LandHeight(px, pz + st) - LandHeight(px, pz - st)) / (2f * st);
                float slope = Mathf.Sqrt(sx * sx + sz * sz);

                var w = new float[1, 1, layersCount];   // SplatAt 只写 [0,0,*]
                SplatAt(px, pz, 0.5f, w, 0, 0);
                sb.Append("  ").Append(nm)
                  .Append(" y=").Append(y.ToString("0.00")).Append(" slope=").Append(slope.ToString("0.00"))
                  .Append("  raw=").Append(s_lastUnder.ToString("0.00"))
                  .Append('/').Append(s_lastWet.ToString("0.00"))
                  .Append('/').Append(s_lastDry.ToString("0.00"))
                  .Append('/').Append(s_lastRock.ToString("0.00"))
                  .Append('/').Append(s_lastGrass.ToString("0.00"))
                  .Append("  norm=").Append(w[0, 0, 0].ToString("0.00"))
                  .Append('/').Append(w[0, 0, 1].ToString("0.00"))
                  .Append('/').Append(w[0, 0, 2].ToString("0.00"))
                  .Append('/').Append(w[0, 0, 3].ToString("0.00"))
                  .Append('\n');
            }
            WriteProbe(sb.ToString());
        }

        // ═════════════════════════════════════════════════════════ 混合权重

        static void SplatAt(float x, float z, float step, float[,,] w, int xi, int zi)
        {
            float y = LandHeight(x, z);
            float sx = (LandHeight(x + step, z) - LandHeight(x - step, z)) / (2f * step);
            float sz = (LandHeight(x, z + step) - LandHeight(x, z - step)) / (2f * step);
            float slope = Mathf.Sqrt(sx * sx + sz * sz);

            // 判据顺序 = 优先级：深水沙底 → 潮间湿沙 → 干沙滩 → 陡岩 → 平缓高处草皮。
            // ⚠ 坡度阈值第一版给到 0.34，结果 1m 高的小沙洲和整座大岛被判成岩石，
            //   满屏青灰。0.34 只有 19°，海滩后缘都比它陡。岩石要的是**海蚀壁**，
            //   从 0.55（29°）起步；草给平缓的高处 —— 参考里岩顶那层"草帽"就是
            //   陡壁顶部突然变平，靠坡度门限自然切出来的。
            // ⚠ 光看坡度不够：smoothstep 的峰值导数是平均坡度的 1.5 倍，
            //   一座 1.5m 高的小沙洲洲顶也能到 0.5 —— 第二版实测小沙洲和整座大岛
            //   还是青灰岩石色，就是这个原因。岩石必须**同时**要求高度。
            // 上一版实测 land 里草只占 10.2%、岩石 18.6%，大岛整面是岩。
            // 参考1 右侧那座岛**主体是绿的**，只有背后露头一段岩壁，所以：
            //   · 草的高度门限从 2.2 降到 1.8（肩膀下半段也该有植被），
            //     坡度门限放宽到 0.42~0.75 —— 配合大岛 innerW 摊到 16m 之后的缓坡；
            //   · 第二条岩石规则（"高处就算坡不陡也算岩"）的高度从 6m 提到 9m，
            //     只留给岩核本身，不再把 5~8m 的绿肩膀一起吃掉。
            float under = 1f - Ramp(y, -1.75f, -0.75f);                     // 深水沙底
            float wet   = Band(y, -1.35f, 0.30f, -1.90f,  0.75f);          // 潮间带
            float dry   = Band(y, 0.08f, 2.6f, -0.30f, 3.4f) * (1f - Ramp(slope, 0.40f, 0.70f));
            float rock  = Ramp(slope, 0.55f, 0.90f) * BandUp(y, 2.6f, 1.2f);
            rock = Mathf.Max(rock, BandUp(y, 9.0f, 6.5f) * Ramp(slope, 0.26f, 0.48f));
            float grass = (1f - Ramp(slope, 0.42f, 0.75f)) * BandUp(y, 1.8f, 1.10f);

            s_lastUnder = under; s_lastWet = wet; s_lastDry = dry;
            s_lastRock  = rock;  s_lastGrass = grass;

            // 水下必须是**沙**，不是草。潟湖那股亮青绿来自"清水 + 白沙底"，
            // 底给成草绿整个水色就闷掉了。第一版把 under 加到草通道，归一化后
            // 深水格变成 100% 草皮 —— 满屏灰白就是这么来的。
            float sandUnder = under * 0.9f;

            float total = dry + wet + sandUnder + rock + grass;
            if (total < 1e-5f)
            {
                w[xi, zi, 0] = 1f;                       // 兜底：不留全零格
                s_win[0]++; if (y > 0f) s_winLand[0]++;
                return;
            }
            float inv = 1f / total;
            w[xi, zi, 0] = dry   * inv;
            w[xi, zi, 1] = (wet + sandUnder) * inv;
            w[xi, zi, 2] = rock  * inv;
            w[xi, zi, 3] = grass * inv;

            // 主层统计：肉眼在过曝截图上分不出"这是湿沙还是岩石"，只能让数字说话
            int best = 0; float bv = w[xi, zi, 0];
            for (int i = 1; i < layersCount; i++)
                if (w[xi, zi, i] > bv) { bv = w[xi, zi, i]; best = i; }
            s_win[best]++;
            if (y > 0f) s_winLand[best]++;
        }

        /// <summary>区间隶属度：区间内 1，两侧各过渡一段。</summary>
        static float Band(float v, float lo, float hi, float loT, float hiT)
            => Ramp(v, loT, lo) * (1f - Ramp(v, hi, hiT));

        /// <summary>只有下界的隶属度（"越高越是"）。用 Band(v, lo, 99, loT, 99) 代替是错的：
        /// 那样上界那一段会真的按 99 衰减，越高的地方反而越不像它。</summary>
        static float BandUp(float v, float lo, float loT) => Ramp(v, loT, lo);

        static float Ramp(float v, float a, float b)
            => Mathf.Clamp01((v - a) / Mathf.Max(b - a, 1e-4f));

        // ═════════════════════════════════════════════════════════ 贴图

        /// <summary>
        /// 程序化生成一张**可平铺**的贴图。接缝处理：把 (x,y) 两个角各映到单位圆上，
        /// 噪声输入取圆坐标之和 —— 输入点沿闭合曲线走一圈，所以 x=0 与 x=size 必然同值。
        /// 不这么做的话 Terrain 上会看到一张 512m 的方格网。
        /// </summary>
        static Texture2D MakeTex(string assetPath, int size, System.Func<float, float, Color> shade, Color tint)
        {
            var px = new Color[size * size];
            const float TAU = 6.2831853f;
            for (int y = 0; y < size; y++)
            {
                float b = TAU * y / size;
                float p = Mathf.Cos(b) + 2f, q = Mathf.Sin(b) + 2f;
                for (int x = 0; x < size; x++)
                {
                    float a = TAU * x / size;
                    float u = Mathf.Cos(a) + 2f, v = Mathf.Sin(a) + 2f;
                    px[y * size + x] = shade(u + p, v + q) * tint;
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply(false);
            System.IO.File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 8;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = true;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// <summary>
        /// 2022.3 的 TerrainLayer 字段名和网上教程差一整套（照旧名写会直接编译不过）：
        ///   贴图叫 diffuseTexture 不是 texture；高光叫 specular 不是 specularColor；
        ///   **没有** diffuseColor —— 调色走 diffuseRemapMin/Max，
        ///   着色器算的是 remapMin + texel*(remapMax-remapMin)，所以"乘一个色调"
        ///   = min 给黑、max 给那个颜色。
        /// TerrainLayer 同样不是 ScriptableObject，用 new 而不是 CreateInstance。
        /// </summary>
        static TerrainLayer MakeLayer(string name, Texture2D tex, float tile, Color tint, float smooth)
        {
            string p = LayerFolder + "/Islet_" + name + ".terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
            bool fresh = l == null;
            if (fresh) l = new TerrainLayer();
            l.name = "Islet_" + name;
            l.diffuseTexture = tex;
            l.tileSize = new Vector2(tile, tile);
            l.diffuseRemapMin = Color.black;
            l.diffuseRemapMax = tint;
            l.specular = new Color(0.04f, 0.04f, 0.04f);
            l.metallic = 0f;
            l.smoothness = smooth;
            if (fresh) AssetDatabase.CreateAsset(l, p);
            else EditorUtility.SetDirty(l);
            return l;
        }

        // 贴图亮度怎么定（**第二版**，第一版按"目标显示值反推 albedo"算，结果还是过曝）：
        //   上一版实测 SandDry 贴图均值 sRGB 0.80 → 渲出来 (255,255,255) **直接爆顶**，
        //   连带把潟湖的 R 通道从目标 91 抬到 157（水把底下的白沙折射搬上来了）。
        //   教训：反推时只算了太阳那一路增益，漏了 AmbientMode.Skybox 从一个
        //   亮热带天空积分下来的环境光，两路加起来比估的 2.2 高一截。
        //   所以这一版不再"算准"，而是**先降下来再量**：下面四组常数是
        //   按上一版渲出的过曝幅度倒推的一档，跑完必须用
        //   Tools/compare_reference.py 复量，不能就此当成定稿。
        //   锚点仍是 Docs/参考视频帧/海岛目标色板.json（显示空间）：
        //     sand lit (243,230,203) / sea_rock (58,74,86) / foliage (36,122,35)
        static TerrainLayer[] BuildLayers()
        {
            EnsureFolder(TexFolder);
            EnsureFolder(LayerFolder);
            const int S = 512;

            // 干沙：暖白偏米，细颗粒 + 低频沙丘 + 极细 ripple
            var sandDry = MakeTex(TexFolder + "/Islet_SandDry.png", S, (u, v) =>
            {
                float dune  = Fbm(u * 0.9f, v * 0.9f, 3);
                float grain = Fbm(u * 9.0f, v * 9.0f, 2);
                float rip   = 0.5f + 0.5f * Mathf.Sin((u * 7.0f + v * 2.0f) * 3.0f + dune * 4f);
                float k = 0.47f + 0.11f * dune + 0.05f * (grain - 0.5f) + 0.035f * rip;
                return new Color(k, k * 0.965f, k * 0.855f);
            }, Color.white);

            // 湿沙：压暗一档、稍微偏冷（吃水后的沙就是暗）
            var sandWet = MakeTex(TexFolder + "/Islet_SandWet.png", S, (u, v) =>
            {
                float dune  = Fbm(u * 1.1f + 5f, v * 1.1f - 3f, 3);
                float film  = Fbm(u * 5.5f - 2f, v * 5.5f + 8f, 2);
                float k = 0.30f + 0.10f * dune + 0.05f * film;
                return new Color(k * 0.94f, k * 0.93f, k * 0.88f);
            }, Color.white);

            // 海蚀岩：青灰底 + 棱脊条纹（目标 sea_rock ≈ (58,74,86)，B 最高）
            // B 只比 R 高一点点，上一版给到 1.02 再叠上蓝色天光就渲成 (57,87,112)
            // —— 蓝过头，读起来像冰不像玄武岩。
            var rock = MakeTex(TexFolder + "/Islet_Rock.png", S, (u, v) =>
            {
                float ridge = 1f - Mathf.Abs(Fbm(u * 2.6f, v * 2.6f, 4) - 0.5f) * 2f;
                float blot  = Fbm(u * 1.3f - 6f, v * 1.3f + 4f, 3);
                float k = 0.17f + 0.17f * ridge * ridge + 0.07f * blot;
                return new Color(k * 0.90f, k * 0.96f, k * 1.00f);
            }, Color.white);

            // 热带草被：亮翠绿 + 一簇一簇的叶丘。参考1 的植被是 (36,122,35)，
            // R 只有 G 的 0.30 —— 上一版 0.62 太黄，渲出来是"草坪绿"不是"热带绿"。
            var grass = MakeTex(TexFolder + "/Islet_Grass.png", S, (u, v) =>
            {
                float clump = Fbm(u * 2.2f + 3f, v * 2.2f - 1f, 3);
                float blade = Fbm(u * 13.0f, v * 13.0f, 2);
                float k = 0.20f + 0.20f * clump + 0.07f * (blade - 0.5f);
                return new Color(k * 0.50f, k, k * 0.46f);
            }, Color.white);

            return new[]
            {
                MakeLayer("SandDry", sandDry, 1.6f, new Color(1.00f, 0.985f, 0.94f), 0.18f),
                // 湿沙 smoothness 0.42→0.16：水下那片沙是被折射搬进画面的，
                // 高光一亮就等于给水加了一层白底
                MakeLayer("SandWet", sandWet, 1.4f, new Color(0.90f, 0.92f, 0.95f), 0.16f),
                MakeLayer("Rock",    rock,    4.0f, new Color(0.86f, 0.92f, 1.00f), 0.10f),
                MakeLayer("Grass",   grass,   2.2f, new Color(0.92f, 1.00f, 0.90f), 0.12f),
            };
        }

        // ═════════════════════════════════════════════════════════ 噪声
        // 全部确定性：只用坐标哈希，不碰 System.Random / Random.insideUnitSphere，
        // 否则每次跑菜单 91 地形都换一个模样，前后两张截图就没法比了。

        static uint H(uint x, uint y)
        {
            uint n = x * 374761393u + y * 668265263u;
            n = (n ^ (n >> 13)) * 1274126177u;
            return (n ^ (n >> 16)) & 0x7fffffffu;
        }

        static float VNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
            const float INV = 1f / 1073741823.5f;
            float a = H((uint)xi, (uint)yi) * INV;
            float b = H((uint)(xi + 1), (uint)yi) * INV;
            float c = H((uint)xi, (uint)(yi + 1)) * INV;
            float d = H((uint)(xi + 1), (uint)(yi + 1)) * INV;
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        static float Fbm(float x, float y, int oct)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < oct; i++)
            {
                sum += amp * VNoise(x, y);
                norm += amp;
                amp *= 0.5f;
                x = x * 2.03f + 1.7f;
                y = y * 1.97f - 2.3f;
            }
            return sum / Mathf.Max(norm, 1e-5f);
        }

        // ═════════════════════════════════════════════════════════ 工具

        /// <summary>
        /// 必须在 IsletScene 里干活。如果当前打开的是**别的**场景且有未保存改动，
        /// 直接 refuse 而不是自动保存 —— 上一轮差点把 Main.unity 的临时状态一起写回去。
        /// </summary>
        static bool EnsureScene()
        {
            var sc = EditorSceneManager.GetActiveScene();
            if (sc.path == ScenePath) return true;
            if (sc.isDirty)
            {
                WriteProbe("MENU91 REFUSED: 当前场景 " + sc.path + " 有未保存改动，" +
                           "不替你保存别的线的场景。手动存/弃后重跑\n");
                return false;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            { WriteProbe("MENU91 REFUSED: 没有 " + ScenePath + "，先跑 Tools/Islet/90\n"); return false; }
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single).IsValid();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }
    }
}
#endif
