namespace OrbitTweak.Core;

/// <summary>
/// The beat of the montage music, from its sound alone (mono 16-bit samples, see FfMontage): where notes start (the
/// onset envelope: how much louder the lows, mids and highs get every 11.6 ms), the tempo (autocorrelation of that
/// envelope, then a fine search of beat length and position), and the first strong hit, where the music of the
/// montage starts. Nothing of Windows in here: tests/ClipLogic.Tests tries it on made-up click tracks.
/// </summary>
internal static class MontageBeat
{
    /// <summary>The sample rate the music is read at (ffmpeg -ar).</summary>
    public const int Rate = 11025;
    private const int Hop = 128;   // one value of the envelope per 128 samples

    /// <summary>
    /// The beat. Start: seconds into the music of the first strong hit, moved onto the beat (the montage music starts
    /// there, so in the montage the beats are at 0, Period, 2·Period …); End: length of the music read.
    /// </summary>
    public sealed record Grid(double Bpm, double Period, double Start, double End)
    {
        /// <summary>Seconds of montage that have the beat (after that the music would start over).</summary>
        public double Span => End - Start;
    }

    /// <summary>How much louder the sound gets, per 1/fps s (rises only), with the local average taken off.</summary>
    public static double[] Envelope(short[] pcm, int rate, out double fps)
    {
        fps = (double)rate / Hop;
        int n = pcm.Length / Hop;
        var flux = new double[n];
        if (n < 2) return flux;
        // three bands with one-pole filters: lows (kick) below 200 Hz, mids, highs (snare, hats) above 2 kHz
        double a1 = 1 - Math.Exp(-2 * Math.PI * 200 / rate), a2 = 1 - Math.Exp(-2 * Math.PI * 2000 / rate);
        double l1 = 0, l2 = 0;
        double p0 = 0, p1 = 0, p2 = 0;
        for (int f = 0; f < n; f++)
        {
            double e0 = 0, e1 = 0, e2 = 0;
            for (int i = f * Hop, end = i + Hop; i < end; i++)
            {
                double x = pcm[i] / 32768.0;
                l1 += a1 * (x - l1); l2 += a2 * (x - l2);
                double m = l2 - l1, h = x - l2;
                e0 += l1 * l1; e1 += m * m; e2 += h * h;
            }
            // log-like loudness: a hit counts in quiet parts as in loud ones
            double v0 = Math.Log(1 + 1000 * Math.Sqrt(e0 / Hop)), v1 = Math.Log(1 + 1000 * Math.Sqrt(e1 / Hop)), v2 = Math.Log(1 + 1000 * Math.Sqrt(e2 / Hop));
            if (f > 0) flux[f] = Math.Max(0, v0 - p0) + Math.Max(0, v1 - p1) + Math.Max(0, v2 - p2);
            p0 = v0; p1 = v1; p2 = v2;
        }
        // minus the average of the 0.8 s around it: a steady rise in loudness is no hit
        int w = (int)Math.Round(0.4 * fps);
        var sum = new double[n + 1];
        for (int f = 0; f < n; f++) sum[f + 1] = sum[f] + flux[f];
        var env = new double[n];
        for (int f = 0; f < n; f++)
        {
            int a = Math.Max(0, f - w), b = Math.Min(n, f + w + 1);
            env[f] = Math.Max(0, flux[f] - (sum[b] - sum[a]) / (b - a));
        }
        return env;
    }

    /// <summary>
    /// The beat of a piece of music, or null if it has none that is clear enough. need = seconds of music after the
    /// first strong hit that the montage uses (the tempo is measured over that part).
    /// </summary>
    public static Grid? Find(short[] pcm, int rate = Rate, double need = 240)
    {
        var env = Envelope(pcm, rate, out double fps);
        int n = env.Length;
        if (n < fps * 4) return null;
        double T(double frame) => (frame + 0.5) / fps;   // time of a frame: the middle of it

        // the first strong hit within the first 40 s: nearly as strong as the loud hits of the piece (a soft intro is
        // left out), else at least a third as strong
        var sorted = env.Where(x => x > 0).OrderBy(x => x).ToArray();
        if (sorted.Length < 8) return null;
        double loud = sorted[(int)(sorted.Length * 0.98)];
        int first = -1;
        foreach (var share in new[] { 0.6, 0.35 })
        {
            for (int f = 1; f < Math.Min(n - 1, (int)(40 * fps)) && first < 0; f++)
                if (env[f] >= loud * share && env[f] >= env[f - 1] && env[f] >= env[f + 1]) first = f;
            if (first >= 0) break;
        }
        if (first < 0) return null;

        // the part the tempo is measured on: from just before the first hit over what the montage needs
        int a = Math.Max(0, first - (int)fps), b = Math.Min(n, first + (int)Math.Ceiling((need + 4) * fps));
        if (b - a < fps * 4) return null;

        // smoothed a little (a hit falls into one value or the next): for the tempo and the grid
        var sm = new double[n];
        for (int f = 0; f < n; f++)
        {
            double s = 6 * env[f], w = 6;
            for (int d = 1; d <= 2; d++)
            {
                double k = d == 1 ? 4 : 1;
                if (f - d >= 0) { s += k * env[f - d]; w += k; }
                if (f + d < n) { s += k * env[f + d]; w += k; }
            }
            sm[f] = s / w;
        }

        // tempo: autocorrelation over 60 to 200 BPM, a little weight towards 120 (against half and double tempo)
        int lo = (int)Math.Floor(fps * 60 / 200), hi = (int)Math.Ceiling(fps * 60 / 60);
        var r = new double[2 * hi + 2];
        for (int lag = lo; lag < r.Length; lag++)
        {
            double s = 0;
            for (int f = a; f + lag < b; f++) s += sm[f] * sm[f + lag];
            r[lag] = s / Math.Max(1, b - a - lag);
        }
        int best = -1; double bestScore = 0;
        for (int lag = lo; lag <= hi; lag++)
        {
            double bpm = 60 * fps / lag, wgt = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 120), 2));
            double s = wgt * (r[lag] + 0.5 * r[2 * lag]);
            if (s > bestScore) { bestScore = s; best = lag; }
        }
        if (best < 0 || bestScore <= 0) return null;

        // beat length and position: the grid on which the smoothed envelope adds up highest, first coarse, then fine.
        // (held at a beat in the middle of the part, so a slightly other length does not move the whole grid)
        double At(double x) { int i = (int)Math.Floor(x); if (i < 0 || i >= n - 1) return 0; double u = x - i; return sm[i] * (1 - u) + sm[i + 1] * u; }
        double mid = (a + b) / 2.0;
        double Comb(double period, double anchor)
        {
            double s = 0; int k = 0;
            for (double x = anchor; x >= a; x -= period, k++) s += At(x);
            for (double x = anchor + period; x < b - 1; x += period, k++) s += At(x);
            return k > 0 ? s / k : 0;
        }
        (double period, double anchor, double score) Search(double p0, double pr, double ps, double f0, double fr, double fs)
        {
            (double, double, double) top = (p0, f0, -1);
            for (double p = p0 - pr; p <= p0 + pr + 1e-9; p += ps)
                for (double ph = f0 - fr; ph <= f0 + fr + 1e-9; ph += fs)
                {
                    double s = Comb(p, ph);
                    if (s > top.Item3) top = (p, ph, s);
                }
            return top;
        }
        var c = Search(best, 1.2, 0.05, mid + best / 2.0, best / 2.0, 0.25);
        c = Search(c.period, 0.06, 0.002, c.anchor, 0.4, 0.02);
        // a clear beat stands out: against the same grid half a beat off, and against the average of the envelope
        double off = Comb(c.period, c.anchor + c.period / 2), avg = 0;
        for (int f = a; f < b; f++) avg += sm[f];
        avg /= b - a;
        if (c.score < 1.15 * off || c.score < 2 * avg) return null;

        // the music starts on the beat nearest to the first strong hit
        double period = c.period / fps, phase = T(c.anchor);
        double start = phase + Math.Round((T(first) - phase) / period) * period;
        if (start < 0) start += period;
        return new Grid(60 / period, period, start, (double)pcm.Length / rate);
    }
}
