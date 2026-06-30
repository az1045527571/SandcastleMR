namespace Sandcastle
{
    /// <summary>
    /// 潮汐/水位数据提供者接口。
    /// </summary>
    public interface ITideProvider
    {
        /// <summary>当前水面的世界 Y 坐标</summary>
        float CurrentWaterLevel { get; }

        /// <summary>当前水面在 SdfVolume 本地坐标系下的 Y 坐标（从体积底部算起）</summary>
        float CurrentTideLocalY { get; }
    }
}
