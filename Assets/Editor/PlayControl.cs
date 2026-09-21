// PlayControl.cs —— **显式**设定 Play 状态，取代 `Tools/WaterProject/26 切换播放`
//
// 为什么要单独加这个：菜单26 是一个**无状态 toggle**（按一次翻一面），
// 而调用它的我并不知道当前是哪一面。本轮在同一次会话里被它坑了三次：
//   · 以为退出了、其实进去了 ⇒ 建骨架的菜单84 回 "在 Play 模式"；
//   · 以为退出了、其实没退 ⇒ 菜单90 静默 REFUSED，材质新值根本没写进去
//     （探针第 53 行），后面那次"改了没反应"的截图就是这么来的；
//   · 想探状态却只能靠"再翻一次看会不会报错"，等于又改了一次状态。
// 一个会**改状态**的操作不能同时被用来**查状态**。所以这里给三个不会混淆的：
// Enter / Exit / Query，前两个是赋值不是翻转，第三个只写探针。

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class PlayControl
    {
        [MenuItem("Tools/Play/90 进入播放(显式，已在播则不动)")]
        static void Enter()
        {
            if (EditorApplication.isPlaying)
            { Write("PLAY already playing\n"); return; }
            EditorApplication.isPlaying = true;
            Write("PLAY -> enter\n");
        }

        [MenuItem("Tools/Play/91 退出播放(显式，已停则不动)")]
        static void Exit()
        {
            if (!EditorApplication.isPlaying)
            { Write("PLAY already stopped\n"); return; }
            EditorApplication.isPlaying = false;
            Write("PLAY -> exit\n");
        }

        // 这一条是本轮真正缺的东西：**只读**的状态查询。
        // 有了它就不必"靠再翻一次来猜现在在哪一面"。
        [MenuItem("Tools/Play/92 查询播放状态(只写探针，不改任何东西)")]
        static void Query()
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Write("PLAY STATE isPlaying=" + EditorApplication.isPlaying
                  + " isCompiling=" + EditorApplication.isCompiling
                  + " runInBackground=" + Application.runInBackground
                  + " scene=" + (string.IsNullOrEmpty(sc.path) ? "(未保存)" : sc.path)
                  + " dirty=" + sc.isDirty + "\n");
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s + "\n");
        }
    }
}
#endif
