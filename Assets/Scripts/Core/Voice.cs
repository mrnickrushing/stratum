using System;

namespace Stratum.Core
{
    // Voice chat audio: 16 kHz mono in 40 ms frames, packed 4:1 with IMA ADPCM (~8 KB/s per speaker). Each frame
    // starts with the coder's state, so a lost or late frame never garbles the ones after it.
    public static class Voice
    {
        public const int Rate = 16000;
        public const int FrameSamples = 640;                         // 40 ms
        public const int FrameBytes = 4 + FrameSamples / 2;          // i16 predictor, u8 step index, u8 spare, nibbles
        public const int MaxFramesPerSecond = 30;                    // the server drops anything faster (25 is normal)

        static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
        static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97,
            107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
            876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428,
            4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350,
            22385, 24623, 27086, 29794, 32767,
        };

        // The encoder carries its state from frame to frame (smoother), but writes it into every frame.
        public sealed class Encoder
        {
            int _pred, _index;

            public byte[] Encode(float[] pcm, int offset = 0)
            {
                var o = new byte[FrameBytes];
                o[0] = (byte)(_pred & 0xFF); o[1] = (byte)((_pred >> 8) & 0xFF); o[2] = (byte)_index;
                for (int i = 0; i < FrameSamples; i++)
                {
                    int s = (int)Math.Round(Math.Clamp(pcm[offset + i], -1f, 1f) * 32767f);
                    int step = StepTable[_index], diff = s - _pred, code = 0;
                    if (diff < 0) { code = 8; diff = -diff; }
                    int delta = step >> 3;
                    if (diff >= step) { code |= 4; diff -= step; delta += step; }
                    step >>= 1;
                    if (diff >= step) { code |= 2; diff -= step; delta += step; }
                    step >>= 1;
                    if (diff >= step) { code |= 1; delta += step; }
                    _pred = Math.Clamp(_pred + ((code & 8) != 0 ? -delta : delta), -32768, 32767);
                    _index = Math.Clamp(_index + IndexTable[code], 0, 88);
                    if ((i & 1) == 0) o[4 + (i >> 1)] = (byte)code; else o[4 + (i >> 1)] |= (byte)(code << 4);
                }
                return o;
            }
        }

        // Decodes one frame into `into` (FrameSamples floats). False if the frame is malformed.
        public static bool Decode(byte[] frame, float[] into, int offset = 0)
        {
            if (frame == null || frame.Length != FrameBytes || into.Length - offset < FrameSamples) return false;
            int pred = (short)(frame[0] | frame[1] << 8), index = frame[2];
            if (index > 88) return false;
            for (int i = 0; i < FrameSamples; i++)
            {
                int b = frame[4 + (i >> 1)], code = (i & 1) == 0 ? b & 15 : b >> 4;
                int step = StepTable[index], delta = step >> 3;
                if ((code & 4) != 0) delta += step;
                if ((code & 2) != 0) delta += step >> 1;
                if ((code & 1) != 0) delta += step >> 2;
                pred = Math.Clamp(pred + ((code & 8) != 0 ? -delta : delta), -32768, 32767);
                index = Math.Clamp(index + IndexTable[code], 0, 88);
                into[offset + i] = pred / 32768f;
            }
            return true;
        }

        public static float Rms(float[] a, int offset, int count)
        {
            double s = 0;
            for (int i = 0; i < count; i++) s += a[offset + i] * a[offset + i];
            return (float)Math.Sqrt(s / Math.Max(1, count));
        }
    }
}
