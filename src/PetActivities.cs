using System;
using System.Drawing;

namespace Glasswing
{
    enum ImpActivity { None, Hungry, Playful, Fishing, Firefly }

    partial class PetBody
    {
        ImpActivity impActivity;
        double activitySince, activityUntil;

        public bool CanGesture => Mode == 0 && !Sleeping && T() >= EatUntil && CurrentActivity(T()) == ImpActivity.None;

        ImpActivity CurrentActivity(double t) { return t < activityUntil ? impActivity : ImpActivity.None; }

        public void ClearActivity() { impActivity = ImpActivity.None; activityUntil = 0; }

        public void AskForAttention(bool hungry)
        {
            StartActivity(hungry ? ImpActivity.Hungry : ImpActivity.Playful, 2.5);
        }

        public void Forage(bool fish)
        {
            StartActivity(fish ? ImpActivity.Fishing : ImpActivity.Firefly, fish ? 7 : 5);
        }

        public int TakeForageResult()
        {
            if (T() < activityUntil) return -1;
            var completed = impActivity;
            ClearActivity();
            return completed == ImpActivity.Fishing ? 2 : completed == ImpActivity.Firefly ? 3 : -1;
        }

        void StartActivity(ImpActivity activity, double duration)
        {
            activitySince = T();
            activityUntil = activitySince + duration;
            impActivity = activity;
        }

        string ActivityPose(ImpActivity activity, double t)
        {
            if (activity == ImpActivity.Hungry || activity == ImpActivity.Fishing) return "eat_hold";
            if (activity == ImpActivity.Firefly)
                return t - activitySince > 3.4 ? "eat_hold" : "quarter";
            return "front";
        }

        void ApplyActivityMotion(ImpActivity activity, double t, float u, ref float hop, ref float tilt)
        {
            double age = t - activitySince;
            if (activity == ImpActivity.Hungry) tilt = (float)Math.Sin(age * 4) * 5;
            if (activity == ImpActivity.Fishing && age > 5.2) tilt = -7 * (float)Math.Sin((age - 5.2) * 2);
            if (activity == ImpActivity.Playful || activity == ImpActivity.Firefly)
                hop -= 11 * u * (float)Math.Pow(Math.Sin(age * 4), 2);
        }

        void DrawActivity(Graphics g, ImpActivity activity, double t, float u, float height)
        {
            double age = t - activitySince;
            if (activity == ImpActivity.Playful)
            {
                float y = -18 * u - (float)Math.Abs(Math.Sin(age * 5)) * 32 * u;
                using (var ball = new SolidBrush(Color.FromArgb(255, 170, 97)))
                using (var seam = new Pen(Color.FromArgb(120, 68, 95), 1.5f * u))
                {
                    g.FillEllipse(ball, 42 * u, y, 19 * u, 19 * u);
                    g.DrawArc(seam, 46 * u, y + 2 * u, 10 * u, 15 * u, 70, 210);
                }
            }
            else if (activity == ImpActivity.Fishing)
            {
                float pondX = 85 * u;
                float bob = (float)Math.Sin(age * 5) * 2 * u;
                float tip = -height * 0.7f - (age > 5.2 ? 12 * u : 0);
                using (var water = new SolidBrush(Color.FromArgb(190, 75, 169, 199)))
                using (var rod = new Pen(Color.FromArgb(227, 160, 101), 2 * u))
                using (var line = new Pen(Color.FromArgb(170, 231, 242, 255), u))
                using (var floatBrush = new SolidBrush(Color.FromArgb(255, 121, 130)))
                {
                    g.FillEllipse(water, pondX - 31 * u, -10 * u, 62 * u, 13 * u);
                    g.DrawLine(rod, 7 * u, -height * 0.42f, pondX, tip);
                    g.DrawLine(line, pondX, tip, pondX, -5 * u + bob);
                    g.FillEllipse(floatBrush, pondX - 3 * u, -10 * u + bob, 6 * u, 8 * u);
                }
                if (age > 5.8)
                    DrawFood(g, 2, pondX, -14 * u - (float)(age - 5.8) * 42 * u, 17 * u, 1);
            }
            else if (activity == ImpActivity.Firefly)
            {
                float x = (44 + 18 * (float)Math.Sin(age * 3)) * u;
                float y = -height * (0.54f + 0.18f * (float)Math.Cos(age * 4));
                Sparkle(g, x, y, (5 + (float)Math.Sin(age * 8)) * u, 240, true);
                if (age > 3.4) DrawFood(g, 3, 39 * u, -10 * u, 18 * u, 1);
            }
        }
    }
}
