using System;
using System.IO;
using System.Media;
using System.Text;

namespace Pegline
{
    /// <summary>
    /// The little sounds of the line. Windows has no quiet "tink" or "pop", so
    /// they are synthesized once into memory: a bright metallic tick when a
    /// photo is pegged, a soft bubble pop when one comes down.
    /// </summary>
    static class Sounds
    {
        const int Rate = 44100;
        static SoundPlayer tink, pop, recycle, clack;

        public static void Tink() => Play(ref tink, () => new MemoryStream(Wav(MakeTink())));
        public static void Pop() => Play(ref pop, () => new MemoryStream(Wav(MakePop())));
        /// <summary>A magnet snapping onto the screen.</summary>
        public static void Clack() => Play(ref clack, () => new MemoryStream(Wav(MakeClack())));

        /// <summary>The Recycle Bin's own sound, when Windows has it.</summary>
        public static void Recycle()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Windows Recycle.wav");
            if (File.Exists(path)) Play(ref recycle, () => File.OpenRead(path));
            else Pop();
        }

        // The players are kept: PlaySound reads from their buffer while it plays.
        static void Play(ref SoundPlayer player, Func<Stream> source)
        {
            try
            {
                if (player == null)
                {
                    player = new SoundPlayer(source());
                    player.Load();
                }
                player.Play();
            }
            catch (Exception e)
            {
                Log.Error("Could not play a sound", e);
            }
        }

        static float[] MakeTink()
        {
            var s = new float[(int)(Rate * 0.16)];
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / Rate;
                double env = (1 - Math.Exp(-t / 0.0006)) * Math.Exp(-t / 0.03);
                double tone = 0.66 * Math.Sin(2 * Math.PI * 2637 * t)
                            + 0.24 * Math.Sin(2 * Math.PI * 3951 * t)
                            + 0.10 * Math.Sin(2 * Math.PI * 6270 * t) * Math.Exp(-t / 0.008);
                s[i] = (float)(0.24 * env * tone);
            }
            return s;
        }

        static float[] MakePop()
        {
            var s = new float[(int)(Rate * 0.1)];
            double phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / Rate;
                double freq = 190 + 560 * Math.Exp(-t / 0.011);
                phase += 2 * Math.PI * freq / Rate;
                double env = (1 - Math.Exp(-t / 0.0012)) * Math.Exp(-t / 0.022);
                s[i] = (float)(0.34 * env * Math.Sin(phase));
            }
            return s;
        }

        static float[] MakeClack()
        {
            var random = new Random(3);
            var s = new float[(int)(Rate * 0.08)];
            double low = 0;
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / Rate;
                low += 0.25 * ((random.NextDouble() * 2 - 1) - low);
                double click = low * Math.Exp(-t / 0.004);
                double thump = Math.Sin(2 * Math.PI * 130 * t) * Math.Exp(-t / 0.018);
                s[i] = (float)(0.42 * click + 0.3 * thump);
            }
            return s;
        }

        static byte[] Wav(float[] samples)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int data = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + data);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(data);
                foreach (var sample in samples)
                    w.Write((short)Math.Round(Math.Max(-1, Math.Min(1, sample)) * 32767));
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
