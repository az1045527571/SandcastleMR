using System.Collections.Generic;
using System.Diagnostics;

namespace Sandcastle
{
    /// <summary>
    /// 轻量性能探针：在代码里用 PerfProbe.Begin("名")/End("名") 包住要测的段，
    /// DebugUI 读 PerfProbe.Report() 显示各段耗时(ms, 指数平滑)。
    /// 只在 Editor / Development Build 计时，正式包零开销。
    /// 用于定位侵蚀时到底卡在 CPU 哪一步(SurfaceErode/合成/RemoveUnsupported/回读)。
    /// </summary>
    public static class PerfProbe
    {
        struct Stat { public double smoothedMs; public double lastMs; public int calls; }

        static readonly Dictionary<string, Stat> _stats = new Dictionary<string, Stat>();
        static readonly Dictionary<string, long> _starts = new Dictionary<string, long>();
        static readonly List<string> _order = new List<string>();   // 保持显示顺序稳定
        static readonly Stopwatch _sw = Stopwatch.StartNew();

        public static bool Enabled = true;

        public static void Begin(string label)
        {
            if (!Enabled) return;
            _starts[label] = _sw.ElapsedTicks;
        }

        public static void End(string label)
        {
            if (!Enabled) return;
            if (!_starts.TryGetValue(label, out long startTicks)) return;
            double ms = (_sw.ElapsedTicks - startTicks) * 1000.0 / Stopwatch.Frequency;

            if (!_stats.TryGetValue(label, out Stat s))
            {
                s = new Stat { smoothedMs = ms, lastMs = ms, calls = 0 };
                _order.Add(label);
            }
            // 指数平滑，跟 DebugUI 的帧时间一致风格
            s.smoothedMs = s.smoothedMs * 0.9 + ms * 0.1;
            s.lastMs = ms;
            s.calls++;
            _stats[label] = s;
        }

        /// <summary>返回多行文本：每段 平滑ms (最近ms, 累计调用数)。</summary>
        public static string Report()
        {
            if (_order.Count == 0) return "(无计时数据)";
            var sb = new System.Text.StringBuilder();
            foreach (var label in _order)
            {
                var s = _stats[label];
                sb.AppendLine($"{label}: {s.smoothedMs:F2} ms  (最近 {s.lastMs:F2}, ×{s.calls})");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>清零累计调用数(方便重新采样)。</summary>
        public static void Reset()
        {
            _order.Clear();
            _stats.Clear();
            _starts.Clear();
        }
    }
}
