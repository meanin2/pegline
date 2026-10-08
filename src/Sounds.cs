using System;
using System.IO;
using System.Media;
using System.Threading;

namespace Pegline
{
    internal static class Sounds
    {
        // Original short sounds synthesized locally; no Apple sound assets are redistributed.
        static readonly byte[][] waves = new byte[][] { Generate(0), Generate(1), Generate(2) };
        public static void Play(int kind)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { using (var s = new MemoryStream(waves[kind])) using (var player = new SoundPlayer(s)) player.PlaySync(); } catch { }
            });
        }
        static byte[] Generate(int kind)
        {
            const int rate = 22050; int count = (int)(rate * (kind == 2 ? .16 : .09)); var random = new Random(17);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / rate, u = (double)i / count, envelope = Math.Sin(Math.PI * u) * Math.Exp(-5 * u);
                    double sample = kind == 0 ? Math.Sin(2 * Math.PI * 1600 * t) + .3 * Math.Sin(2 * Math.PI * 2300 * t)
                        : kind == 1 ? Math.Sin(2 * Math.PI * (400 * t - 900 * t * t)) : (random.NextDouble() * 2 - 1);
                    writer.Write((short)(sample * envelope * 5500));
                }
                writer.Flush(); return stream.ToArray();
            }
        }
    }
}
