using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pegline
{
    enum Ease { Linear, In, Out, InOut, InOutCubic, InCubic }

    /// <summary>Anything that moves once per frame. It returns false when it has come to rest.</summary>
    interface IAnimated
    {
        bool Step(double dt);
    }

    /// <summary>
    /// One animated number. It either springs towards a target, keeping its
    /// velocity when the target changes mid-flight the way SwiftUI springs do,
    /// or tweens over a fixed time. Every frame it hands the value to a setter.
    /// </summary>
    sealed class Motion : IAnimated
    {
        readonly Action<double> apply;
        readonly double precision;
        double target, stiffness, damping;
        double from, to, duration, elapsed;
        Ease ease;
        bool springing, tweening;
        Action then;

        public double Value { get; private set; }
        public double Velocity { get; private set; }
        public bool IsActive => springing || tweening;
        public double Target => springing ? target : tweening ? to : Value;

        /// <param name="precision">How close counts as arrived, in the value's own units.</param>
        public Motion(double initial, Action<double> apply, double precision = 0.001)
        {
            this.apply = apply;
            this.precision = precision;
            Value = initial;
            apply?.Invoke(initial);
        }

        public void Set(double value)
        {
            springing = tweening = false;
            then = null;
            Value = value;
            Velocity = 0;
            apply?.Invoke(value);
        }

        /// <summary>A spring as SwiftUI describes it: response is the period in seconds, and a damping fraction of 1 settles without overshoot.</summary>
        public void Spring(double to, double response, double dampingFraction, Action then = null)
        {
            double w = 2 * Math.PI / response;
            SpringWith(to, w * w, 2 * dampingFraction * w, then);
        }

        /// <summary>A spring by stiffness and damping with unit mass, like SwiftUI's interpolatingSpring.</summary>
        public void SpringWith(double to, double stiffness, double damping, Action then = null)
        {
            tweening = false;
            springing = true;
            target = to;
            this.stiffness = stiffness;
            this.damping = damping;
            this.then = then;
            Animator.Add(this);
        }

        /// <summary>A push: adds velocity and lets the spring bring it back to rest, the way a hanging photo takes a gust.</summary>
        public void Kick(double velocity, double rest, double stiffness, double damping, double limit)
        {
            double v = Velocity + velocity;
            // A photo already swung far does not wind up further the same way.
            if (Math.Abs(Value) > limit && Math.Sign(v) == Math.Sign(Value)) return;
            then = null;
            Velocity = Math.Max(-limit * 12, Math.Min(limit * 12, v));
            SpringWith(rest, stiffness, damping);
        }

        public void Tween(double to, double seconds, Ease ease, Action then = null)
        {
            springing = false;
            if (seconds <= 0)
            {
                Set(to);
                then?.Invoke();
                return;
            }
            tweening = true;
            from = Value;
            this.to = to;
            duration = seconds;
            elapsed = 0;
            this.ease = ease;
            this.then = then;
            Velocity = 0;
            Animator.Add(this);
        }

        public bool Step(double dt)
        {
            if (tweening)
            {
                elapsed += dt;
                double t = Math.Min(1, elapsed / duration);
                double before = Value;
                Value = from + (to - from) * Easing.Apply(ease, t);
                Velocity = (Value - before) / dt;
                apply?.Invoke(Value);
                if (t >= 1)
                {
                    tweening = false;
                    Velocity = 0;
                    Finish();
                }
            }
            else if (springing)
            {
                // Small fixed steps keep stiff springs stable at any frame rate.
                int steps = Math.Max(1, (int)Math.Ceiling(dt * 480));
                double h = dt / steps, x = Value, v = Velocity;
                for (int i = 0; i < steps; i++)
                {
                    double a = -stiffness * (x - target) - damping * v;
                    v += a * h;
                    x += v * h;
                }
                Value = x;
                Velocity = v;
                if (Math.Abs(x - target) < precision && Math.Abs(v) < precision * 10)
                {
                    Value = target;
                    Velocity = 0;
                    springing = false;
                    apply?.Invoke(Value);
                    Finish();
                }
                else
                {
                    apply?.Invoke(Value);
                }
            }
            return IsActive;
        }

        void Finish()
        {
            var next = then;
            then = null;
            next?.Invoke();
        }
    }

    static class Easing
    {
        public static double Apply(Ease ease, double t)
        {
            switch (ease)
            {
                case Ease.In: return t * t;
                case Ease.Out: return 1 - (1 - t) * (1 - t);
                case Ease.InOut: return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                case Ease.InOutCubic: return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                case Ease.InCubic: return t * t * t;
                default: return t;
            }
        }

        public static double Smooth(double x, double a, double b)
        {
            double t = Math.Max(0, Math.Min(1, (x - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }

        public static double Lerp(double a, double b, double k) => a + (b - a) * k;
    }

    /// <summary>Runs everything that moves once per rendered frame, and sleeps when nothing does.</summary>
    static class Animator
    {
        const double MaxFps = 50;
        static readonly List<IAnimated> active = new List<IAnimated>();
        static bool hooked;
        static TimeSpan last;

        public static void Add(IAnimated motion)
        {
            if (!active.Contains(motion)) active.Add(motion);
            if (hooked) return;
            hooked = true;
            last = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        static void OnRendering(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == last) return;
            // At most 50 frames a second. Uncapped, WPF's CPU renderer can run
            // ahead of the display and spend three times the time for nothing
            // anyone sees.
            if (last != TimeSpan.Zero && (now - last).TotalSeconds < 1 / (MaxFps + 2)) return;
            double dt = last == TimeSpan.Zero ? 1.0 / 60 : (now - last).TotalSeconds;
            last = now;
            dt = Math.Max(0.001, Math.Min(dt, 1.0 / 20));

            foreach (var motion in active.ToArray())
            {
                try
                {
                    if (!motion.Step(dt)) active.Remove(motion);
                }
                catch (Exception ex)
                {
                    active.Remove(motion);
                    Log.Error("Animation step failed", ex);
                }
            }

            if (active.Count == 0)
            {
                CompositionTarget.Rendering -= OnRendering;
                hooked = false;
            }
        }
    }

    static class Delay
    {
        /// <summary>Runs an action on the UI thread after a delay, like asyncAfter.</summary>
        public static void Run(double seconds, Action action)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(Math.Max(0, seconds))
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try { action(); }
                catch (Exception ex) { Log.Error("Delayed action failed", ex); }
            };
            timer.Start();
        }
    }
}
