using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    // ─────────────────────────────────────────────────────────────
    // 构图探针（只读，不改任何东西）
    //
    // 为什么要它：之前"远处植被太锐/太亮"的判断全部建立在对 _islet_view.png
    // 的肉眼看图上，而那张图里巨岩压根不在画幅内、灌木疑似被沙丘挡死。
    // 对着错的画面调色是无效的 —— 所以先把"该看见的东西有没有被看见"量出来。
    //
    // 两个问题、两份答案：
    //   A 画幅归属：把每个标记投到 16:9 的视口坐标，报它在不在 [0,1]×[0,1]。
    //   B 遮挡：对投进画幅的植被，沿"相机→树冠顶点"的线段逐步采 Terrain 高度，
    //     中间任何一步地形高于视线 ⇒ 这棵树被沙丘吃掉，画面上根本不该出现它。
    //
    // ⚠ WorldToViewportPoint 用的是 cam.aspect，而 GameView 的 aspect 会随窗口
    //   漂移。这里显式临时改成 16/9 再算，finally 里还原。
    // ─────────────────────────────────────────────────────────────
    public static class IsletFraming
    {
        const float Aspect  = 16f / 9f;
        const int   Steps   = 48;
        const float GroundEps = 0.15f;

        static void P(string s)
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Logs"));
            File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/frame_probe.txt"), s);
        }

        // ─────────────────────────────────────────────────────────────
        // 切到 IsletScene，但**绝不**弹"是否保存"模态框。
        //
        // 背景：菜单98 误把 Islet 的雾/天光/天空盒/Volume/太阳/相机写进 PondScene
        //   并存了盘。磁盘那份已用 git 还原，但**内存里那份还是脏的**：
        //   · 直接 OpenScene(Single) 会弹模态框 → MCP 桥挂住，编辑器点不动；
        //   · 手滑点"保存" → 刚还原的文件又被写坏。
        //   ClearDirty 只丢标记、不写文件，内存里的坏值随场景切换一起丢掉。
        // 只有在确认活动场景已经不脏之后才真的切；否则宁可停下报错。
        // ─────────────────────────────────────────────────────────────
        [MenuItem("Tools/Islet/99c 切到 IsletScene(丢弃内存改动，不保存)")]
        public static void SwitchToIslet()
        {
            var sb = new StringBuilder();
            var cur = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            sb.AppendLine("=== SWITCH " + System.DateTime.Now.ToString("HH:mm:ss") + " ===");
            sb.AppendFormat("active={0}  isDirty={1}\n",
                string.IsNullOrEmpty(cur.path) ? "<untitled>" : cur.path, cur.isDirty);

            if (!string.IsNullOrEmpty(cur.path) && cur.path != ScenePathWanted)
            {
                var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(cur.path);
                if (asset != null) UnityEditor.EditorUtility.ClearDirty(asset);
                cur = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                sb.AppendFormat("after ClearDirty: isDirty={0}\n", cur.isDirty);
            }

            if (cur.isDirty)
            {
                sb.Append("REFUSED 还是不干净 —— 不切，避免模态框。人工处理\n");
                P(sb.ToString());
                return;
            }

            var opened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePathWanted, UnityEditor.SceneManagement.OpenSceneMode.Single);
            sb.AppendFormat("opened={0}  valid={1}\n", ScenePathWanted, opened.IsValid());
            P(sb.ToString());
        }

        const string ScenePathWanted = "Assets/Scenes/IsletScene.unity";

        [MenuItem("Tools/Islet/99 构图探针(16:9 视野+沙丘遮挡，只写探针不改东西)")]
        public static void Probe()
        {
            if (!WaterIsletSetup.GuardIsletScene("MENU99"))
            { P("MENU99 REFUSED: 活动场景不是 IsletScene，量出来的构图不算\n"); return; }

            var cam = Camera.main;
            if (cam == null) { P("FRAME NO Camera.main —— 场景没相机或 tag 不是 MainCamera\n"); return; }

            var terrains = Object.FindObjectsOfType<Terrain>();
            var prevAspect = cam.aspect;
            cam.aspect = Aspect;

            var sb = new StringBuilder();
            try
            {
                float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float halfH = halfV * Aspect;
                sb.AppendLine("=== FRAME " + System.DateTime.Now.ToString("HH:mm:ss") + " ===");
                sb.AppendFormat("cam pos={0} eul={1} vFov={2} aspect={3:0.###} hFov={4:0.0}\n",
                    Fmt(cam.transform.position),
                    Fmt(cam.transform.eulerAngles),
                    cam.fieldOfView, Aspect,
                    2f * Mathf.Atan(halfH) * Mathf.Rad2Deg);
                sb.AppendFormat("terrains={0}\n", terrains.Length);

                // 根物体归位检查：地形和水的 y 一旦离开代码给的值，
                // 画面表现是"地面整块消失、石头花草悬在灰天里"，而不是报错。
                var terrGo = UnityEngine.GameObject.Find("IsletTerrain");
                var watGo  = UnityEngine.GameObject.Find("IsletWater");
                sb.AppendFormat("root terr {0} want {1}{2}   water {3} want (0,0,0){4}\n",
                    terrGo != null ? Fmt(terrGo.transform.position) : "MISSING",
                    Fmt(IsletTerrain.Origin),
                    terrGo != null && (terrGo.transform.position - IsletTerrain.Origin).sqrMagnitude > 0.01f
                        ? " <<<漂移" : " ok",
                    watGo != null ? Fmt(watGo.transform.position) : "MISSING",
                    watGo != null && watGo.transform.position.sqrMagnitude > 0.01f ? " <<<漂移" : " ok");

                // ── 分类收集 ──
                var all = Object.FindObjectsOfType<Transform>();
                int rockTot = 0, rockIn = 0, rockOccl = 0;
                int vegTot = 0, vegIn = 0, vegOccl = 0, vegTested = 0;
                int palmTot = 0, palmIn = 0;
                float yMin = float.PositiveInfinity, yMax = float.NegativeInfinity;
                var cells = new int[4, 4];
                var offSide = new int[5];  // 0左 1右 2上 3下 4身后

                foreach (var t in all)
                {
                    string n = t.name;
                    bool isRock = n.StartsWith("Rock_") || n.StartsWith("Reef_");
                    bool isVeg  = n.StartsWith("Bush_") || n.StartsWith("Tuft_");
                    bool isPalm = n.StartsWith("Palm_");
                    if (!isRock && !isVeg && !isPalm) continue;

                    if (t.position.y < yMin) yMin = t.position.y;
                    if (t.position.y > yMax) yMax = t.position.y;

                    var r = t.GetComponentInChildren<Renderer>();
                    Vector3 top = r != null ? r.bounds.max : t.position;
                    Vector3 vp  = cam.WorldToViewportPoint(top);

                    bool behind = vp.z < cam.nearClipPlane;
                    bool inside = !behind && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;

                    if (inside) cells[Mathf.Clamp((int)(vp.x * 4), 0, 3),
                                      Mathf.Clamp((int)((1f - vp.y) * 4), 0, 3)]++;

                    if (isRock)
                    {
                        rockTot++; if (inside) rockIn++;
                        bool occl = inside && Occluded(cam, terrains, top);
                        if (occl) rockOccl++;
                        sb.AppendFormat("{0,-22} wpos={1} top={2} vp=({3:0.00},{4:0.00}) d={5:0.0} {6}{7}\n",
                            n, Fmt(t.position), Fmt(top), vp.x, vp.y, vp.z,
                            inside ? "IN" : "OUT", occl ? " 被地形挡住" : "");
                    }
                    else if (isPalm) { palmTot++; if (inside) palmIn++; }
                    else
                    {
                        vegTot++;
                        if (inside)
                        {
                            vegIn++;
                            // 遮挡只对抽样做：700 个全 march 一遍采样的开销没必要
                            if (vegIn % 17 == 0 && vegTested < 40)
                            {
                                vegTested++;
                                if (Occluded(cam, terrains, top)) vegOccl++;
                            }
                        }
                        else if (behind) offSide[4]++;
                        else
                        {
                            if (vp.x < 0f) offSide[0]++;
                            else if (vp.x > 1f) offSide[1]++;
                            if (vp.y < 0f) offSide[3]++;
                            else if (vp.y > 1f) offSide[2]++;
                        }
                    }
                }

                sb.AppendFormat("rock  {0}/{1} in frame, 其中被地形挡住 {2}\n", rockIn, rockTot, rockOccl);
                sb.AppendFormat("标记世界 y 范围 [{0:0.0}, {1:0.0}]  vs  相机 y {2:0.0}\n",
                                yMin, yMax, cam.transform.position.y);
                sb.AppendFormat("palm  {0}/{1} in frame\n", palmIn, palmTot);
                sb.AppendFormat("veg   {0}/{1} in frame  (遮挡抽测 {2} 个，其中被挡 {3})\n",
                                vegIn, vegTot, vegTested, vegOccl);
                sb.AppendFormat("out-of-frame 原因: 左{0} 右{1} 上{2} 下{3} 身后{4}\n",
                                offSide[0], offSide[1], offSide[2], offSide[3], offSide[4]);
                sb.AppendLine("画幅 4x4 格 occupancy (行=上→下, 列=左→右):");
                for (int j = 0; j < 4; j++)
                {
                    sb.Append("  ");
                    for (int i = 0; i < 4; i++) sb.Append(cells[i, j].ToString().PadLeft(4));
                    sb.Append('\n');
                }

                // 画幅在地面上的实际覆盖：决定"往后退多少才装得下"要用这个
                foreach (float d in new[] { 60f, 100f, 140f, 180f, 240f })
                    sb.AppendFormat("depth {0:0} m -> 半宽 {1:0.0} m, 半高 {2:0.0} m\n",
                                    d, d * halfH, d * halfV);
            }
            finally { cam.aspect = prevAspect; }

            P(sb.ToString());
            Debug.Log("[IsletFraming] 见 Logs/frame_probe.txt");
        }

        // 沿视线 march，任何一步地形高于视线（留 eps）且不在两端 ⇒ 被沙丘吃掉
        static bool Occluded(Camera cam, Terrain[] terrains, Vector3 target)
        {
            Vector3 eye = cam.transform.position;
            Vector3 d = target - eye;
            for (int s = 1; s < Steps; s++)
            {
                float f = s / (float)Steps;
                Vector3 p = eye + d * f;
                float h = HeightAt(terrains, p);
                if (h > p.y + GroundEps) return true;
                // 已经走到目标高度以下还在往下走，就不必再看了
                if (f >= 0.98f) break;
            }
            return false;
        }

        static float HeightAt(Terrain[] terrains, Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var t in terrains)
            {
                var r = t.transform.position;
                var sz = t.terrainData.size;
                if (p.x < r.x || p.x > r.x + sz.x || p.z < r.z || p.z > r.z + sz.z) continue;
                float h = t.SampleHeight(p) + r.y;
                if (h > best) best = h;
            }
            return float.IsNegativeInfinity(best) ? 0f : best;
        }

        static string Fmt(Vector3 v)
        {
            return string.Format("({0:0.0},{1:0.0},{2:0.0})", v.x, v.y, v.z);
        }
    }
}
