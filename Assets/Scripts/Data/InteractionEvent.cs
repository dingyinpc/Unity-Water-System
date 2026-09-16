// InteractionEvent.cs — 数据层：交互事件（数据不可替代，可扩展为存档记录）
using UnityEngine;

namespace WaterProject.Data
{
    /// <summary>
    /// 一次水体交互事件：位置/强度/半径/生命周期。
    /// 由逻辑层（WaterInteractionManager）产生，供交互 RT 与程序化 Ripple 使用。
    /// </summary>
    [System.Serializable]
    public struct InteractionEvent
    {
        public Vector3 position;  // 世界位置
        public float   strength;  // 强度 0~1
        public float   radius;    // 影响半径（米）
        public float   life;      // 生命周期（秒）
        public float   bornTime;  // 出生时间（Time.time）

        public static InteractionEvent Create(Vector3 pos, float strength = 1f, float radius = 1.2f, float life = 1.8f)
        {
            return new InteractionEvent
            {
                position = pos,
                strength = strength,
                radius = radius,
                life = life,
                bornTime = Time.time
            };
        }

        public float Age => Time.time - bornTime;
        public bool  IsAlive => Age >= 0f && Age <= life;
        public float AgeNorm => Mathf.Clamp01(Age / Mathf.Max(life, 1e-4f));
    }
}