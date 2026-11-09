namespace Client.Common.Models
{
    using System;

    public sealed class ListeningCounter
    {
        private double _lastTime;
        private double _rate = 1;
        private bool _playing;
        private bool _observed;
        public double ListenedSeconds { get; private set; }
        public double DurationSeconds { get; set; }
        public bool Qualified { get { return DurationSeconds > 0 && !double.IsInfinity(DurationSeconds) && ListenedSeconds >= Math.Min(DurationSeconds / 2, 240); } }

        // Integrate real playing intervals. A seek never adds the position jump.
        public void Observe(double monotonicSeconds, bool playing, double rate)
        {
            if (double.IsNaN(monotonicSeconds) || double.IsInfinity(monotonicSeconds) || double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0)
                throw new ArgumentOutOfRangeException("monotonicSeconds/rate");
            if (_observed && monotonicSeconds < _lastTime) throw new ArgumentOutOfRangeException("monotonicSeconds");
            if (_observed && _playing) ListenedSeconds += (monotonicSeconds - _lastTime) * _rate;
            _lastTime = monotonicSeconds; _playing = playing; _rate = rate; _observed = true;
        }
    }
}
