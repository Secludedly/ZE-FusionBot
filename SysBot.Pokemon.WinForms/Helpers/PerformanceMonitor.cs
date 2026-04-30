using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms.Helpers
{
    /// <summary>
    /// Performance monitoring system for tracking UI responsiveness and resource usage
    /// </summary>
    public class PerformanceMonitor
    {
        private static PerformanceMonitor? _instance;
        public static PerformanceMonitor Instance
        {
            get
            {
                _instance ??= new PerformanceMonitor();
                return _instance;
            }
        }

        private readonly Dictionary<string, PerformanceMetric> _metrics = new();
        private readonly object _lock = new();
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _memoryCounter;
        private readonly System.Windows.Forms.Timer _updateTimer;
        
        private long _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private double _currentFps = 0;
        private double _averageFps = 0;

        public event EventHandler<PerformanceEventArgs>? MetricsUpdated;

        private PerformanceMonitor()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Process", "Working Set - Private", Process.GetCurrentProcess().ProcessName);
            }
            catch
            {
                // Performance counters may not be available
                _cpuCounter = null!;
                _memoryCounter = null!;
            }

            _updateTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000 // Update every second
            };
            _updateTimer.Tick += UpdateMetrics;
            
            // Don't start timer in design mode
            if (System.ComponentModel.LicenseManager.UsageMode != System.ComponentModel.LicenseUsageMode.Designtime)
            {
                _updateTimer.Start();
            }
        }

        public void RecordFrame()
        {
            Interlocked.Increment(ref _frameCount);
        }

        public void StartOperation(string operationName)
        {
            lock (_lock)
            {
                if (!_metrics.ContainsKey(operationName))
                {
                    _metrics[operationName] = new PerformanceMetric(operationName);
                }
                _metrics[operationName].Start();
            }
        }

        public void EndOperation(string operationName)
        {
            lock (_lock)
            {
                if (_metrics.TryGetValue(operationName, out var metric))
                {
                    metric.End();
                }
            }
        }

        private void UpdateMetrics(object? sender, EventArgs e)
        {
            // Calculate FPS
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;
            if (elapsed > 0)
            {
                _currentFps = _frameCount / elapsed;
                _averageFps = (_averageFps * 0.9) + (_currentFps * 0.1); // Moving average
                _frameCount = 0;
                _lastFpsUpdate = now;
            }

            // Get system metrics
            float cpuUsage = 0;
            long memoryUsage = 0;
            
            try
            {
                if (_cpuCounter != null)
                    cpuUsage = _cpuCounter.NextValue();
                if (_memoryCounter != null)
                    memoryUsage = (long)_memoryCounter.NextValue();
            }
            catch
            {
                // Ignore counter errors
            }

            // Calculate operation metrics
            Dictionary<string, OperationStats> operationStats;
            lock (_lock)
            {
                operationStats = _metrics.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.GetStats()
                );
            }

            // Raise event
            MetricsUpdated?.Invoke(this, new PerformanceEventArgs
            {
                CurrentFps = _currentFps,
                AverageFps = _averageFps,
                CpuUsage = cpuUsage,
                MemoryUsageMB = memoryUsage / (1024 * 1024),
                OperationStats = operationStats
            });
        }

        public PerformanceReport GetReport()
        {
            Dictionary<string, OperationStats> stats;
            lock (_lock)
            {
                stats = _metrics.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.GetStats()
                );
            }

            return new PerformanceReport
            {
                AverageFps = _averageFps,
                OperationStats = stats
            };
        }

        public void Reset()
        {
            lock (_lock)
            {
                _metrics.Clear();
            }
            _frameCount = 0;
            _currentFps = 0;
            _averageFps = 0;
        }
    }

    public class PerformanceMetric
    {
        private readonly string _name;
        private readonly List<double> _durations = new();
        private DateTime _startTime;
        private bool _isRunning;

        public PerformanceMetric(string name)
        {
            _name = name;
        }

        public void Start()
        {
            _startTime = DateTime.Now;
            _isRunning = true;
        }

        public void End()
        {
            if (_isRunning)
            {
                var duration = (DateTime.Now - _startTime).TotalMilliseconds;
                _durations.Add(duration);
                _isRunning = false;

                // Keep only last 100 measurements
                if (_durations.Count > 100)
                {
                    _durations.RemoveAt(0);
                }
            }
        }

        public OperationStats GetStats()
        {
            if (_durations.Count == 0)
            {
                return new OperationStats
                {
                    Name = _name,
                    Count = 0,
                    AverageMs = 0,
                    MinMs = 0,
                    MaxMs = 0
                };
            }

            return new OperationStats
            {
                Name = _name,
                Count = _durations.Count,
                AverageMs = _durations.Average(),
                MinMs = _durations.Min(),
                MaxMs = _durations.Max()
            };
        }
    }

    public class OperationStats
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public double AverageMs { get; set; }
        public double MinMs { get; set; }
        public double MaxMs { get; set; }
    }

    public class PerformanceEventArgs : EventArgs
    {
        public double CurrentFps { get; set; }
        public double AverageFps { get; set; }
        public float CpuUsage { get; set; }
        public long MemoryUsageMB { get; set; }
        public Dictionary<string, OperationStats> OperationStats { get; set; } = new();
    }

    public class PerformanceReport
    {
        public double AverageFps { get; set; }
        public Dictionary<string, OperationStats> OperationStats { get; set; } = new();

        public override string ToString()
        {
            var report = $"Performance Report:\n";
            report += $"Average FPS: {AverageFps:F1}\n\n";
            report += "Operation Statistics:\n";
            
            foreach (var stat in OperationStats.Values.OrderByDescending(s => s.AverageMs))
            {
                report += $"  {stat.Name}: {stat.AverageMs:F2}ms avg ({stat.Count} calls)\n";
            }

            return report;
        }
    }
}