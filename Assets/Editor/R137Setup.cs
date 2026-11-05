// R137Setup.cs — R137 潟湖水面"太平"（R136 审计 Top1）
//
// 审计数据：水面局部对比 我方 45.3 / 参考 86.4（海面 37.0 / 65.0）。
// 观感：一片均匀浅青绿 + 规则"灯芯绒"波纹。
// 手法：①水底低频斑块（亮沙/深色海草）②波纹第二层错向 37°
// 90  斑块开 / 90b 关
// 91/91b 波纹错向 开/关
// 92  全部开（推荐档）
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R137Setup
    {
        const string Mat = "Assets/Art/Materials/Islet_LagoonWater.mat";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Set(string key, float v, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R137 REFUSED: 无 Islet_LagoonWater\n"); return; }
            if (!m.HasProperty(key)) { P($"R137 REFUSED: 材质没有 {key}\n"); return; }
            m.SetFloat(key, v);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R137 {tag}: {key}={v:F2}\n");
        }

        [MenuItem("Tools/R137/90 水底斑块=开")]
        public static void PatchOn() => Set("_PatchAmt", 1f, "90 斑块开");

        [MenuItem("Tools/R137/90b 水底斑块=关(旧)")]
        public static void PatchOff() => Set("_PatchAmt", 0f, "90b 斑块关");

        [MenuItem("Tools/R137/91 波纹错向=开")]
        public static void RotOn() => Set("_WaveRotate", 1f, "91 错向开");

        [MenuItem("Tools/R137/91b 波纹错向=关(旧)")]
        public static void RotOff() => Set("_WaveRotate", 0f, "91b 错向关");

        [MenuItem("Tools/R137/92 两项都开(推荐)")]
        public static void BothOn()
        {
            Set("_PatchAmt", 1f, "92 斑块开");
            Set("_WaveRotate", 1f, "92 错向开");
        }

        [MenuItem("Tools/R137/93 两项都关(旧)")]
        public static void BothOff()
        {
            Set("_PatchAmt", 0f, "93 斑块关");
            Set("_WaveRotate", 0f, "93 错向关");
        }

        [MenuItem("Tools/R137/95 沙底衰减=0.34(压住返色)")]
        public static void SandAbs34() => Set("_SandAbsorb", 0.34f, "95 沙衰减0.34");

        [MenuItem("Tools/R137/95b 沙底衰减=0.22(旧)")]
        public static void SandAbs22() => Set("_SandAbsorb", 0.22f, "95b 沙衰减0.22");

        [MenuItem("Tools/R137/96 水体吸收=0.55")]
        public static void Absorb55() => Set("_Absorb", 0.55f, "96 吸收0.55");

        [MenuItem("Tools/R137/96b 水体吸收=0.46(旧)")]
        public static void Absorb46() => Set("_Absorb", 0.46f, "96b 吸收0.46");

        [MenuItem("Tools/R137/94 探针")]
        public static void Probe()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R137/94 无材质\n"); return; }
            P($"R137/94 patch={m.GetFloat("_PatchAmt"):F2} rot={m.GetFloat("_WaveRotate"):F2} "
              + $"absorb={m.GetFloat("_Absorb"):F2} sandAbsorb={m.GetFloat("_SandAbsorb"):F2}\n");
        }
    }
}
#endif
