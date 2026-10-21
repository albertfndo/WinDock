using System;
using System.Media;
using System.Windows.Threading;

namespace WinDock.Services
{
    /// <summary>
    /// Represents the current execution state of the Pomodoro / Quick Timer.
    /// </summary>
    public enum TimerState
    {
        Idle,
        Running,
        Paused,
        Completed
    }

    /// <summary>
    /// Real-time Pomodoro & Quick Timer service providing focus tracking, break intervals,
    /// and audio notifications with smooth progress calculation.
    /// </summary>
    public class PomodoroTimerService
    {
        private static PomodoroTimerService? _instance;
        public static PomodoroTimerService Instance => _instance ??= new PomodoroTimerService();

        private readonly DispatcherTimer _timer;

        public event Action<PomodoroTimerService>? TimerTicked;
        public event Action<string>? TimerCompleted;

        public TimeSpan RemainingTime { get; private set; }
        public TimeSpan TotalDuration { get; private set; }
        public double ProgressPercentage { get; private set; }
        public string CurrentMode { get; private set; }
        public TimerState State { get; private set; }

        public string FormattedTime
        {
            get
            {
                if (RemainingTime.TotalHours >= 1)
                {
                    return $"{(int)RemainingTime.TotalHours:D2}:{RemainingTime.Minutes:D2}:{RemainingTime.Seconds:D2}";
                }
                return $"{RemainingTime.Minutes:D2}:{RemainingTime.Seconds:D2}";
            }
        }

        public PomodoroTimerService()
        {
            TotalDuration = TimeSpan.FromMinutes(25);
            RemainingTime = TimeSpan.FromMinutes(25);
            CurrentMode = "Pomodoro";
            State = TimerState.Idle;
            ProgressPercentage = 0.0;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTick;
        }

        public void StartTimer(TimeSpan duration, string mode = "Pomodoro")
        {
            if (duration <= TimeSpan.Zero)
            {
                duration = TimeSpan.FromMinutes(25);
            }

            TotalDuration = duration;
            RemainingTime = duration;
            CurrentMode = string.IsNullOrWhiteSpace(mode) ? "Pomodoro" : mode;
            ProgressPercentage = 0.0;
            State = TimerState.Running;

            _timer.Stop();
            _timer.Start();

            TimerTicked?.Invoke(this);
        }

        public void PauseTimer()
        {
            if (State != TimerState.Running) return;

            State = TimerState.Paused;
            _timer.Stop();
            TimerTicked?.Invoke(this);
        }

        public void ResumeTimer()
        {
            if (State == TimerState.Paused)
            {
                State = TimerState.Running;
                _timer.Start();
                TimerTicked?.Invoke(this);
            }
            else if (State == TimerState.Idle || State == TimerState.Completed)
            {
                StartTimer(RemainingTime > TimeSpan.Zero ? RemainingTime : (TotalDuration > TimeSpan.Zero ? TotalDuration : TimeSpan.FromMinutes(25)), CurrentMode);
            }
        }

        public void ResetTimer()
        {
            _timer.Stop();
            State = TimerState.Idle;
            RemainingTime = TotalDuration > TimeSpan.Zero ? TotalDuration : TimeSpan.FromMinutes(25);
            ProgressPercentage = 0.0;
            TimerTicked?.Invoke(this);
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (State != TimerState.Running) return;

            if (RemainingTime > TimeSpan.FromSeconds(1))
            {
                RemainingTime = RemainingTime.Subtract(TimeSpan.FromSeconds(1));
                if (TotalDuration.TotalSeconds > 0)
                {
                    double elapsed = TotalDuration.TotalSeconds - RemainingTime.TotalSeconds;
                    ProgressPercentage = Math.Clamp((elapsed / TotalDuration.TotalSeconds) * 100.0, 0.0, 100.0);
                }
                else
                {
                    ProgressPercentage = 0.0;
                }

                TimerTicked?.Invoke(this);
            }
            else
            {
                // Timer completed
                RemainingTime = TimeSpan.Zero;
                ProgressPercentage = 100.0;
                State = TimerState.Completed;
                _timer.Stop();

                try
                {
                    SystemSounds.Beep.Play();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PomodoroTimerService] Beep playback failed: {ex.Message}");
                }

                TimerTicked?.Invoke(this);
                TimerCompleted?.Invoke(CurrentMode);
            }
        }
    }
}
