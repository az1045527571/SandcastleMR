using UnityEngine;

/// <summary>
/// 标记一个 GameObject 是城堡构件。
/// 持有一些元数据：基础半径（用于沙面印记的范围）、湿度、侵蚀进度。
/// 后续 ImprintCamera 会扫这个组件的所有实例做地形融合。
/// </summary>
public class CastlePiece : MonoBehaviour
{
    [Tooltip("构件底部的近似半径（米），用于地形融合范围")]
    public float baseRadius = 0.4f;

    [Tooltip("构件底部相对自身原点的下沉量，使其'埋'进沙里")]
    public float baseSink = 0.02f;

    [Range(0f, 1f)]
    [Tooltip("湿度，0=干 1=湿。湿沙构件不易侵蚀")]
    public float wetness = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("侵蚀进度，0=完整 1=即将崩塌")]
    public float erosion = 0f;

    [Tooltip("被海浪冲过几次后开始累计侵蚀")]
    public float erosionRatePerWave = 0.05f;
}
