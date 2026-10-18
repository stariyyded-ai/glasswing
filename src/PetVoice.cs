using System;
using System.IO;
using System.Media;
using System.Text.RegularExpressions;

namespace Glasswing
{
    // ---------------------------------------------------------------- голос питомца

    // Лепет как в Animal Crossing: по писку на слог. Звук собираем в памяти, файлов нет.
    static class PetVoice
    {
        const int Rate = 22050;
        const int MaxSyl = 12;
        static readonly string Vowels = "аеёиоуыэюяaeiouy";

        static SoundPlayer player;
        static MemoryStream stream;      // держим, пока звук не доиграл
        static DateTime last = DateTime.MinValue;

        // Тембр персонажа
        class Timbre
        {
            public double Freq;          // основной тон, Гц
            public double Ms = 78;       // длина слога
            public double Gap = 14;      // пауза между слогами
            public double Spread = 3.5;  // разброс высоты, полутоны
            public double Saw, Square;   // примесь пилы и меандра к синусу
            public double Noise;         // хрип
            public double Growl;         // рычание: дрожь громкости
            public double Vib, VibHz;    // вибрато: глубина (доля) и частота
            public double Glide;         // скольжение внутри слога, полутоны
            public double Decay = 1;     // 1 обычный спад, больше значит гулкий хвост
        }

        static Timbre For(string id)
        {
            switch (id)
            {
                case "shukaku": return new Timbre { Freq = 175, Ms = 84, Spread = 3, Saw = 0.55, Noise = 0.32, Growl = 0.45 };
                case "matatabi": return new Timbre { Freq = 560, Ms = 88, Spread = 3, Vib = 0.035, VibHz = 11, Glide = -1 };
                case "gyuki": return new Timbre { Freq = 105, Ms = 90, Gap = 18, Spread = 2.5, Saw = 0.2, Glide = -2, Decay = 1.8 };
                case "kitsune": return new Timbre { Freq = 470, Ms = 70, Gap = 12, Spread = 4.5, Square = 0.28, Glide = 2.5 };
                default: return new Timbre { Freq = 380, Square = 0.15 };
            }
        }

        // level: 0 выкл, 1 тихо, 2 громко. force: без паузы в полторы секунды (проба в настройках).
        public static void Speak(string text, PetChar ch, string stage, int level, bool force = false)
        {
            if (level <= 0 || string.IsNullOrEmpty(text) || ch == null) return;
            var now = DateTime.Now;
            if (!force && (now - last).TotalSeconds < 1.5) return;
            last = now;
            try
            {
                var wav = Build(text, ch, stage, level == 2 ? 0.55 : 0.22);
                if (wav == null) return;
                Stop();
                stream = new MemoryStream(wav);
                player = new SoundPlayer(stream);
                player.Play();   // играет сам, не задерживая окно
            }
            catch (Exception ex) { Log.Write("голос питомца: " + ex.Message); }
        }

        public static void Stop()
        {
            try { if (player != null) { player.Stop(); player.Dispose(); } } catch { }
            player = null;
            if (stream != null) { stream.Dispose(); stream = null; }
        }

        static byte[] Build(string text, PetChar ch, string stage, double amp)
        {
            // пояснения в скобках («(он скучает)») читает рассказчик, а не питомец
            string say = Regex.Replace(text, @"\([^)]*\)", " ").Trim();
            if (say.Length == 0) say = text;
            string low = say.ToLowerInvariant();
            int syl = 0;
            foreach (char c in low) if (Vowels.IndexOf(c) >= 0) syl++;
            syl = Math.Max(1, Math.Min(MaxSyl, syl));
            string tail = say.TrimEnd(' ', '.', '…', '♥', '♪', ')');
            bool ask = tail.EndsWith("?"), shout = tail.EndsWith("!");

            var tb = For(ch.Id);
            double freq = tb.Freq * (stage == "baby" ? 1.55 : stage == "teen" ? 1.18 : 1.0);
            double ms = tb.Ms * (stage == "baby" ? 0.85 : 1.0);
            if (shout) amp *= 1.3;
            amp = Math.Min(0.8, amp);

            // одна и та же фраза звучит одинаково
            uint seed = 2166136261;
            foreach (char c in low) seed = (seed ^ c) * 16777619;
            var rnd = new Random((int)(seed & 0x7fffffff));

            int sylN = (int)(Rate * ms / 1000), gapN = (int)(Rate * tb.Gap / 1000);
            int tailN = (int)(Rate * 0.04 * tb.Decay);
            int total = syl * (sylN + gapN) + tailN;
            var buf = new short[total];
            var noise = new Random((int)(seed >> 1));
            double phase = 0, lp = 0;
            int pos = 0;
            for (int k = 0; k < syl; k++)
            {
                double semi = (rnd.NextDouble() * 2 - 1) * tb.Spread;
                if (ask && k == syl - 1) semi = Math.Max(semi, 0) + 5;       // вопрос: в конце вверх
                if (ask && k == syl - 2) semi += 1.5;
                if (shout && k == syl - 1) semi += 2;
                double f0 = freq * Math.Pow(2, semi / 12.0);
                double glide = tb.Glide + (ask && k == syl - 1 ? 3 : 0);
                double vol = amp * (0.82 + 0.18 * rnd.NextDouble());
                int len = sylN + (k == syl - 1 ? tailN : 0);
                for (int i = 0; i < len && pos + i < total; i++)
                {
                    double t = i / (double)Rate, u = i / (double)sylN;
                    double f = f0 * Math.Pow(2, glide * Math.Min(1, u) / 12.0);
                    if (tb.Vib > 0) f *= 1 + tb.Vib * Math.Sin(2 * Math.PI * tb.VibHz * t);
                    phase += 2 * Math.PI * f / Rate;
                    if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
                    double s = Math.Sin(phase);
                    if (tb.Saw > 0) s = s * (1 - tb.Saw) + (phase / Math.PI - 1) * tb.Saw;
                    if (tb.Square > 0) s = s * (1 - tb.Square) + Math.Sign(Math.Sin(phase)) * tb.Square * 0.6;
                    if (tb.Noise > 0)
                    {
                        lp += (noise.NextDouble() * 2 - 1 - lp) * 0.35;      // шум помягче
                        s = s * (1 - tb.Noise) + lp * tb.Noise * 2.2;
                    }
                    if (tb.Growl > 0) s *= 1 - tb.Growl * 0.5 * (1 + Math.Sin(2 * Math.PI * 32 * t));
                    // атака 6 мс, затем плавный спад
                    double env = Math.Min(1, t / 0.006);
                    env *= u < 0.55 ? 1 : Math.Exp(-(u - 0.55) * 4.5 / tb.Decay);
                    buf[pos + i] = (short)(Math.Max(-1, Math.Min(1, s * env * vol)) * 32000);
                }
                pos += sylN + gapN;
            }
            return Wav(buf);
        }

        // 16 бит, моно
        static byte[] Wav(short[] pcm)
        {
            var ms = new MemoryStream(44 + pcm.Length * 2);
            using (var w = new BinaryWriter(ms))
            {
                int data = pcm.Length * 2;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + data);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1);           // PCM
                w.Write((short)1);           // моно
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(data);
                foreach (var s in pcm) w.Write(s);
                w.Flush();
                return ms.ToArray();
            }
        }
    }

    partial class PetLife
    {
        // Голос к каждому облачку: не когда всё спрятано, не во сне.
        void Voice(string text)
        {
            if (hidden || pet.Sleeping) return;
            PetVoice.Speak(text, ch, GetStage(), mod.Voice);
        }

        // Проба голоса при смене громкости в настройках.
        public void VoiceSample()
        {
            if (hidden) return;
            PetVoice.Speak(GetStage() == "baby" ? F("{S}-{s}!") : "Привет! Вот мой голос.", ch, GetStage(), mod.Voice, true);
        }
    }
}
