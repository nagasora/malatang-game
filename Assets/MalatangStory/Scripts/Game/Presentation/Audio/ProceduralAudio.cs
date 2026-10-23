using System;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>調理、店舗および画面操作で使用する効果音。</summary>
    public enum Sfx
    {
        /// <summary>柔らかな操作音。</summary>
        Click,
        /// <summary>決定音。</summary>
        Confirm,
        /// <summary>取消音。</summary>
        Cancel,
        /// <summary>トングの金属音。</summary>
        TongClick,
        /// <summary>食材が落ちる音。</summary>
        IngredientDrop,
        /// <summary>秤の電子音。</summary>
        ScaleBeep,
        /// <summary>レジのベルと引き出し音。</summary>
        Register,
        /// <summary>硬貨が触れ合う音。</summary>
        CoinJingle,
        /// <summary>鍋へ投入する水音。</summary>
        PotSplash,
        /// <summary>湯切りの連続した水音。</summary>
        StrainerShake,
        /// <summary>スープを注ぐ音。</summary>
        Pour,
        /// <summary>調味料を振る音。</summary>
        CondimentShake,
        /// <summary>料理の提供音。</summary>
        Serve,
        /// <summary>入店のチャイム。</summary>
        DoorChime,
        /// <summary>足音。</summary>
        Footstep,
        /// <summary>皿を置く音。</summary>
        Plate,
        /// <summary>レベルアップのファンファーレ。</summary>
        LevelUp,
        /// <summary>解禁のファンファーレ。</summary>
        Unlock,
        /// <summary>成功のファンファーレ。</summary>
        Success,
        /// <summary>失敗音。</summary>
        Fail,
        /// <summary>注意音。</summary>
        Warning,
        /// <summary>炒める音。</summary>
        Sizzle,
        /// <summary>食器が触れ合う音。</summary>
        Dishes,
        /// <summary>通知音。</summary>
        Notification,
        /// <summary>小さな弾ける音。</summary>
        Pop,
        /// <summary>移動の風切り音。</summary>
        Whoosh,
        /// <summary>レジの引き出しが開く音。</summary>
        CashDrawer,
        /// <summary>硬貨をトレーに置く音。</summary>
        CoinDrop,
        /// <summary>紙幣をめくる音。</summary>
        BillRustle,
        /// <summary>QRコードの読み取り音。</summary>
        QrBeep,
        /// <summary>カードを通す音。</summary>
        CardSwipe,
        /// <summary>お客さんの話し声（短いつぶやき）。</summary>
        Voice,
        /// <summary>喜びの声。</summary>
        Cheer,
        /// <summary>不満のつぶやき。</summary>
        Grumble,
        Slurp,
        BicycleBell
    }

    /// <summary>店舗に重ねる循環環境音。</summary>
    public enum AmbientLoop
    {
        /// <summary>鍋の沸騰。</summary>
        Boil,
        /// <summary>客のざわめき。</summary>
        Crowd,
        /// <summary>雨。</summary>
        Rain,
        /// <summary>街のざわめき（車の走行音・遠くの人声）。</summary>
        Street,
        Birds,
        Insects
    }

    /// <summary>進行状況に対応する五音音階の音楽。</summary>
    public enum MusicMood
    {
        /// <summary>ゆったりしたタイトル。</summary>
        Title,
        /// <summary>穏やかな開店準備。</summary>
        Preparation,
        /// <summary>軽快な営業。</summary>
        Business,
        /// <summary>高密度の繁忙時。</summary>
        Busy,
        /// <summary>しっとりした夜。</summary>
        Night,
        /// <summary>明るい営業報告。</summary>
        Report,
        Evening
    }

    /// <summary>固定シードでモノラルPCMを作る、Unity APIに依存しない合成処理。</summary>
    public static class ProceduralAudioSynth
    {
        private const double Tau = Math.PI * 2.0;
        private static readonly int[] Pentatonic = { 0, 2, 4, 7, 9 };

        /// <summary>実行環境の乱数実装によらず同じ列を返す生成器。</summary>
        private struct NoiseGenerator
        {
            private uint state;

            /// <summary>ゼロ以外の種で初期化する。</summary>
            public NoiseGenerator(uint seed)
            {
                state = seed == 0 ? 1u : seed;
            }

            /// <summary>符号付きの一様乱数を返す。</summary>
            public float Signed()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state >> 8) * (2f / 16777215f) - 1f;
            }

            /// <summary>上限を含まない整数乱数を返す。</summary>
            public int Index(int count)
            {
                return Math.Min(count - 1, (int)((Signed() + 1f) * 0.5f * count));
            }
        }

        /// <summary>音楽の調、拍数、テンポおよび楽器密度。</summary>
        private readonly struct MusicSettings
        {
            public readonly int Root;
            public readonly int Bars;
            public readonly double Tempo;
            public readonly double Step;
            public readonly float Flute;
            public readonly float Percussion;

            /// <summary>四拍子の循環フレーズを設定する。</summary>
            public MusicSettings(int root, int bars, double tempo, double step, float flute, float percussion)
            {
                Root = root;
                Bars = bars;
                Tempo = tempo;
                Step = step;
                Flute = flute;
                Percussion = percussion;
            }
        }

        /// <summary>指定効果音を生成する。標本化周波数は1000〜192000 Hz。</summary>
        /// <exception cref="ArgumentOutOfRangeException">音種または標本化周波数が範囲外。</exception>
        public static float[] Effect(Sfx id, int sampleRate)
        {
            ValidateRate(sampleRate);
            ValidateId((int)id, (int)Sfx.BicycleBell + 1, nameof(id));
            NoiseGenerator noise = new NoiseGenerator(0x13579BDFu + (uint)id * 7919u);
            float[] samples = new float[(int)Math.Ceiling(EffectDuration(id) * sampleRate)];
            switch (id)
            {
                case Sfx.Click:
                    AddMallet(samples, sampleRate, 0, 0.18, 660, 0.52f, false);
                    break;
                case Sfx.Confirm:
                    AddMallet(samples, sampleRate, 0, 0.3, 523.25, 0.42f, false);
                    AddMallet(samples, sampleRate, 0.1, 0.35, 783.99, 0.45f, false);
                    break;
                case Sfx.Cancel:
                    AddMallet(samples, sampleRate, 0, 0.22, 440, 0.4f, false);
                    AddMallet(samples, sampleRate, 0.08, 0.22, 329.63, 0.32f, false);
                    break;
                case Sfx.TongClick:
                    AddMetal(samples, sampleRate, 0, 0.14, 1850, 0.44f);
                    AddNoise(samples, sampleRate, 0, 0.035, 1400, 7500, 0.23f, 0, false, ref noise);
                    break;
                case Sfx.IngredientDrop:
                    AddMallet(samples, sampleRate, 0, 0.16, 145, 0.5f, false);
                    AddNoise(samples, sampleRate, 0.015, 0.18, 180, 2200, 0.45f, 0, false, ref noise);
                    break;
                case Sfx.ScaleBeep:
                    AddTone(samples, sampleRate, 0, 0.14, 1350, 0.32f, 0, false);
                    AddTone(samples, sampleRate, 0.2, 0.12, 1350, 0.28f, 0, false);
                    break;
                case Sfx.Register:
                    AddMetal(samples, sampleRate, 0, 0.9, 1318.51, 0.45f);
                    AddNoise(samples, sampleRate, 0.16, 0.33, 180, 3200, 0.4f, 3, false, ref noise);
                    AddMallet(samples, sampleRate, 0.42, 0.12, 130, 0.32f, false);
                    break;
                case Sfx.CoinJingle:
                    for (int i = 0; i < 6; i++)
                        AddMetal(samples, sampleRate, i * 0.055, 0.32, 2100 + noise.Index(1500), 0.22f);
                    break;
                case Sfx.PotSplash:
                    AddNoise(samples, sampleRate, 0, 0.6, 180, 4800, 0.64f, 2, false, ref noise);
                    AddBubbles(samples, sampleRate, 12, 0.5f, false, ref noise);
                    break;
                case Sfx.StrainerShake:
                    for (int i = 0; i < 5; i++)
                    {
                        AddNoise(samples, sampleRate, i * 0.14, 0.16, 650, 6200, 0.62f, 0, false, ref noise);
                        AddMetal(samples, sampleRate, i * 0.14, 0.09, 2500, 0.07f);
                    }
                    break;
                case Sfx.Pour:
                    AddNoise(samples, sampleRate, 0, 1.2, 280, 3600, 0.7f, 5, false, ref noise);
                    AddBubbles(samples, sampleRate, 22, 0.16f, false, ref noise);
                    break;
                case Sfx.CondimentShake:
                    for (int i = 0; i < 4; i++)
                        AddNoise(samples, sampleRate, i * 0.105, 0.075, 1800, 8500, 0.52f, 0, false, ref noise);
                    break;
                case Sfx.Serve:
                    AddMallet(samples, sampleRate, 0, 0.3, 523.25, 0.35f, false);
                    AddMallet(samples, sampleRate, 0.09, 0.4, 659.25, 0.35f, false);
                    AddMetal(samples, sampleRate, 0, 0.18, 1600, 0.15f);
                    break;
                case Sfx.DoorChime:
                    AddMetal(samples, sampleRate, 0, 1.05, 880, 0.35f);
                    AddMetal(samples, sampleRate, 0.18, 0.95, 1174.66, 0.3f);
                    break;
                case Sfx.Footstep:
                    AddMallet(samples, sampleRate, 0, 0.1, 90, 0.62f, false);
                    AddNoise(samples, sampleRate, 0, 0.12, 100, 1800, 0.35f, 0, false, ref noise);
                    break;
                case Sfx.Plate:
                    AddMetal(samples, sampleRate, 0, 0.28, 1520, 0.34f);
                    AddMallet(samples, sampleRate, 0, 0.08, 180, 0.26f, false);
                    break;
                case Sfx.LevelUp:
                    AddFanfare(samples, sampleRate, 60, 0.14, 0.43f);
                    break;
                case Sfx.Unlock:
                    AddFanfare(samples, sampleRate, 67, 0.11, 0.38f);
                    break;
                case Sfx.Success:
                    AddFanfare(samples, sampleRate, 64, 0.09, 0.4f);
                    break;
                case Sfx.Fail:
                    AddMallet(samples, sampleRate, 0, 0.35, 293.66, 0.43f, false);
                    AddMallet(samples, sampleRate, 0.2, 0.4, 220, 0.36f, false);
                    break;
                case Sfx.Warning:
                    AddMallet(samples, sampleRate, 0, 0.2, 740, 0.4f, false);
                    AddMallet(samples, sampleRate, 0.25, 0.2, 740, 0.35f, false);
                    break;
                case Sfx.Sizzle:
                    AddNoise(samples, sampleRate, 0, 1.15, 900, 9500, 0.57f, 13, false, ref noise);
                    for (int i = 0; i < 30; i++)
                        AddNoise(samples, sampleRate, noise.Index(samples.Length) / (double)sampleRate,
                            0.012, 2100, 10000, 0.2f, 0, false, ref noise);
                    break;
                case Sfx.Slurp:
                    AddNoise(samples, sampleRate, 0, .42, 450, 2800, .3f, 4, false, ref noise);
                    AddTone(samples, sampleRate, .08, .18, 95, .08f, 2, false); break;
                case Sfx.BicycleBell:
                    AddMetal(samples, sampleRate, 0, .5, 2400, .3f); break;
                case Sfx.Dishes:
                    for (int i = 0; i < 4; i++)
                        AddMetal(samples, sampleRate, i * 0.11, 0.3, 1350 + noise.Index(900), 0.28f);
                    break;
                case Sfx.Notification:
                    AddMallet(samples, sampleRate, 0, 0.42, 880, 0.37f, false);
                    AddMallet(samples, sampleRate, 0.12, 0.4, 1174.66, 0.32f, false);
                    break;
                case Sfx.Pop:
                    AddBubble(samples, sampleRate, 0, 0.09, 700, 0.55f, false);
                    break;
                case Sfx.Whoosh:
                    AddNoise(samples, sampleRate, 0, 0.42, 450, 5000, 0.66f, 1.8, false, ref noise);
                    break;
                case Sfx.CashDrawer:
                    AddNoise(samples, sampleRate, 0, 0.22, 120, 2600, 0.5f, 1.5, false, ref noise);
                    AddMallet(samples, sampleRate, 0.2, 0.18, 110, 0.5f, false);
                    AddMetal(samples, sampleRate, 0.21, 0.4, 2637, 0.2f);
                    break;
                case Sfx.CoinDrop:
                    AddMetal(samples, sampleRate, 0, 0.22, 3100, 0.3f);
                    AddMetal(samples, sampleRate, 0.06, 0.16, 2700, 0.16f);
                    break;
                case Sfx.BillRustle:
                    AddNoise(samples, sampleRate, 0, 0.16, 1500, 9000, 0.4f, 4, false, ref noise);
                    break;
                case Sfx.QrBeep:
                    AddTone(samples, sampleRate, 0, 0.09, 1975.5, 0.32f, 0, false);
                    AddTone(samples, sampleRate, 0.11, 0.16, 2637, 0.3f, 0, false);
                    break;
                case Sfx.CardSwipe:
                    AddNoise(samples, sampleRate, 0, 0.28, 600, 5200, 0.5f, 0.6, false, ref noise);
                    AddTone(samples, sampleRate, 0.32, 0.12, 1760, 0.28f, 0, false);
                    break;
                case Sfx.Voice:
                    for (int i = 0; i < 4; i++)
                        AddTone(samples, sampleRate, i * 0.075, 0.07, 190 + noise.Index(160), 0.2f, 6, false);
                    AddNoise(samples, sampleRate, 0, 0.3, 300, 2400, 0.08f, 9, false, ref noise);
                    break;
                case Sfx.Cheer:
                    AddTone(samples, sampleRate, 0, 0.18, 330, 0.22f, 7, false);
                    AddTone(samples, sampleRate, 0.12, 0.3, 494, 0.24f, 6, false);
                    AddFanfare(samples, sampleRate, 72, 0.06, 0.18f);
                    break;
                case Sfx.Grumble:
                    AddTone(samples, sampleRate, 0, 0.45, 150, 0.24f, 3, false);
                    AddTone(samples, sampleRate, 0.15, 0.4, 120, 0.2f, 2.5, false);
                    break;
            }
            Finish(samples, sampleRate, 0.004, 0.015);
            return samples;
        }

        /// <summary>3秒の循環環境音を生成する。標本化周波数は1000〜192000 Hz。</summary>
        /// <exception cref="ArgumentOutOfRangeException">音種または標本化周波数が範囲外。</exception>
        public static float[] Ambient(AmbientLoop id, int sampleRate)
        {
            ValidateRate(sampleRate);
            ValidateId((int)id, (int)AmbientLoop.Insects + 1, nameof(id));
            float[] samples = new float[sampleRate * 3];
            NoiseGenerator noise = new NoiseGenerator(0x2468ACE1u + (uint)id * 3571u);
            switch (id)
            {
                case AmbientLoop.Boil:
                    AddNoise(samples, sampleRate, 0, 3, 25, 280, 0.38f, 0.7, true, ref noise);
                    AddBubbles(samples, sampleRate, 95, 0.32f, true, ref noise);
                    break;
                case AmbientLoop.Crowd:
                    AddNoise(samples, sampleRate, 0, 3, 130, 1200, 0.35f, 1.1, true, ref noise);
                    for (int i = 0; i < 26; i++)
                    {
                        double start = noise.Index(samples.Length) / (double)sampleRate;
                        double frequency = 115 + noise.Index(130);
                        AddTone(samples, sampleRate, start, 0.2 + noise.Index(450) / 1000.0,
                            frequency, 0.045f, 3.5, true);
                        AddNoise(samples, sampleRate, start, 0.32, 300, 2200, 0.13f, 7, true, ref noise);
                    }
                    break;
                case AmbientLoop.Rain:
                    AddNoise(samples, sampleRate, 0, 3, 550, 8200, 0.62f, 0.5, true, ref noise);
                    for (int i = 0; i < 65; i++)
                        AddBubble(samples, sampleRate, noise.Index(samples.Length) / (double)sampleRate,
                            0.025, 1200 + noise.Index(1800), 0.07f, true);
                    break;
                case AmbientLoop.Birds:
                    for (int chirp = 0; chirp < 6; chirp++)
                        AddTone(samples, sampleRate, .2 + chirp * .37, .1, 1800 + chirp % 3 * 550, .08f, 8, true);
                    break;
                case AmbientLoop.Insects:
                    for (int chirp = 0; chirp < 16; chirp++)
                        AddTone(samples, sampleRate, chirp * .18, .075, 3400 + chirp % 2 * 600, .06f, 8, true);
                    break;
                case AmbientLoop.Street:
                    AddNoise(samples, sampleRate, 0, 3, 40, 500, 0.3f, 0.4, true, ref noise);
                    AddNoise(samples, sampleRate, .4, 1.6, 90, 1400, 0.22f, 0.9, true, ref noise);
                    for (int i = 0; i < 10; i++)
                        AddTone(samples, sampleRate, noise.Index(samples.Length) / (double)sampleRate, 0.25 + noise.Index(300) / 1000.0,
                            140 + noise.Index(120), 0.03f, 3, true);
                    AddMetal(samples, sampleRate, 2.1, 0.5, 2093, 0.035f);
                    break;
            }
            ApplyReverb(samples, sampleRate, 0.071, 0.16f, true);
            Finish(samples, sampleRate, 0.004, 0.004);
            return samples;
        }

        /// <summary>五音音階の循環BGMを生成する。20〜32秒、推奨標本化周波数は22050 Hz。</summary>
        /// <exception cref="ArgumentOutOfRangeException">ムードまたは標本化周波数が範囲外。</exception>
        public static float[] Music(MusicMood mood, int sampleRate)
        {
            ValidateRate(sampleRate);
            MusicSettings settings = SettingsFor(mood);
            double beat = 60.0 / settings.Tempo;
            int beats = settings.Bars * 4;
            float[] samples = new float[(int)Math.Round(beats * beat * sampleRate)];
            NoiseGenerator noise = new NoiseGenerator(0x5A17C9E3u + (uint)mood * 1013u);
            int scaleIndex = 5;
            int notesPerBar = (int)Math.Round(4.0 / settings.Step);
            for (int bar = 0; bar < settings.Bars; bar++)
            {
                double barStart = bar * 4 * beat;
                AddPluck(samples, sampleRate, barStart, Math.Min(2.4, beat * 3),
                    MidiFrequency(settings.Root - 12), 0.22f, ref noise);
                AddPluck(samples, sampleRate, barStart + 2 * beat, Math.Min(1.8, beat * 2),
                    MidiFrequency(settings.Root - 5), 0.12f, ref noise);
                for (int note = 0; note < notesPerBar; note++)
                {
                    scaleIndex = Math.Max(2, Math.Min(9, scaleIndex + noise.Index(3) - 1));
                    if (note == notesPerBar - 1) scaleIndex = 5;
                    int midi = settings.Root + ScaleSemitone(scaleIndex);
                    double start = barStart + note * settings.Step * beat;
                    double duration = Math.Min(1.7, settings.Step * beat * 1.9);
                    float accent = note == 0 ? 0.27f : 0.21f;
                    AddPluck(samples, sampleRate, start, duration, MidiFrequency(midi), accent, ref noise);
                    if (note % 2 == 0 && settings.Flute > 0)
                        AddTone(samples, sampleRate, start + 0.028, Math.Min(2.2, duration * 1.2),
                            MidiFrequency(midi), settings.Flute, 4.8, true);
                }
                for (int hit = 0; hit < 4; hit++)
                {
                    double start = barStart + hit * beat;
                    AddMallet(samples, sampleRate, start, 0.095, hit % 2 == 0 ? 540 : 780,
                        settings.Percussion, true);
                    if (mood == MusicMood.Busy)
                        AddNoise(samples, sampleRate, start + beat * 0.5, 0.065,
                            1400, 6500, 0.07f, 0, true, ref noise);
                }
            }
            ApplyReverb(samples, sampleRate, 0.113, 0.27f, true);
            ApplyReverb(samples, sampleRate, 0.173, 0.18f, true);
            Finish(samples, sampleRate, 0.006, 0.006);
            return samples;
        }

        /// <summary>音楽の速度、密度、調をムードごとに選ぶ。</summary>
        private static MusicSettings SettingsFor(MusicMood mood)
        {
            switch (mood)
            {
                case MusicMood.Title: return new MusicSettings(60, 8, 80, 2, 0.105f, 0.055f);
                case MusicMood.Preparation: return new MusicSettings(62, 8, 88, 2, 0.07f, 0.07f);
                case MusicMood.Business: return new MusicSettings(65, 10, 96, 1, 0.075f, 0.10f);
                case MusicMood.Busy: return new MusicSettings(67, 12, 120, 0.5, 0.05f, 0.12f);
                case MusicMood.Night: return new MusicSettings(57, 8, 72, 2, 0.12f, 0.025f);
                case MusicMood.Evening: return new MusicSettings(60, 8, 84, 1, .13f, .05f);
                case MusicMood.Report: return new MusicSettings(62, 9, 100, 1, 0.095f, 0.085f);
                default: throw new ArgumentOutOfRangeException(nameof(mood));
            }
        }

        /// <summary>効果音の余韻を含む長さを返す。</summary>
        private static double EffectDuration(Sfx id)
        {
            switch (id)
            {
                case Sfx.Click: return 0.22;
                case Sfx.Confirm: return 0.5;
                case Sfx.Cancel: return 0.35;
                case Sfx.TongClick: return 0.18;
                case Sfx.IngredientDrop: return 0.25;
                case Sfx.ScaleBeep: return 0.36;
                case Sfx.Register: return 1.0;
                case Sfx.CoinJingle: return 0.65;
                case Sfx.PotSplash: return 0.75;
                case Sfx.StrainerShake: return 0.85;
                case Sfx.Pour: return 1.35;
                case Sfx.CondimentShake: return 0.45;
                case Sfx.Serve: return 0.55;
                case Sfx.DoorChime: return 1.25;
                case Sfx.Footstep: return 0.16;
                case Sfx.Plate: return 0.35;
                case Sfx.LevelUp: return 1.5;
                case Sfx.Unlock: return 1.35;
                case Sfx.Success: return 1.25;
                case Sfx.Fail: return 0.7;
                case Sfx.Warning: return 0.5;
                case Sfx.Sizzle: return 1.25;
                case Sfx.Dishes: return 0.7;
                case Sfx.Notification: return 0.6;
                case Sfx.Pop: return 0.14;
                case Sfx.Whoosh: return 0.5;
                case Sfx.CashDrawer: return 0.65;
                case Sfx.CoinDrop: return 0.28;
                case Sfx.BillRustle: return 0.2;
                case Sfx.QrBeep: return 0.3;
                case Sfx.CardSwipe: return 0.48;
                case Sfx.Voice: return 0.34;
                case Sfx.Cheer: return 0.7;
                case Sfx.Slurp: return .5;
                case Sfx.BicycleBell: return .6;
                case Sfx.Grumble: return 0.6;
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        /// <summary>音階番号を宮商角徴羽の半音差に変換する。</summary>
        private static int ScaleSemitone(int index)
        {
            return Pentatonic[index % 5] + index / 5 * 12;
        }

        /// <summary>MIDI番号mに対して440・2^((m−69)/12) Hzを返す。</summary>
        private static double MidiFrequency(int midi)
        {
            return 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
        }

        /// <summary>短い五音音階の上昇と主音の重なりを作る。</summary>
        private static void AddFanfare(float[] samples, int rate, int root, double step, float gain)
        {
            for (int i = 0; i < 6; i++)
                AddMallet(samples, rate, i * step, 0.65, MidiFrequency(root + ScaleSemitone(i)), gain, false);
            AddTone(samples, rate, step * 5, 0.6, MidiFrequency(root + 7), gain * 0.25f, 4.5, false);
        }

        /// <summary>攻撃と減衰を掛け合わせた滑らかな包絡を返す。</summary>
        private static double Envelope(double time, double duration, double attack, double release)
        {
            double rise = Math.Min(1.0, time / attack);
            double fall = Math.Min(1.0, Math.Max(0.0, duration - time) / release);
            return rise * fall;
        }

        /// <summary>非整数倍音を持つ木質の減衰共鳴を加える。</summary>
        private static void AddMallet(float[] samples, int rate, double start, double duration,
            double frequency, float gain, bool loop)
        {
            frequency = Math.Min(frequency, rate * 0.2);
            AddResonance(samples, rate, start, duration, frequency, gain, 10, loop);
            AddResonance(samples, rate, start, duration * 0.55, frequency * 2.76, gain * 0.22f, 18, loop);
            AddResonance(samples, rate, start, duration * 0.3, frequency * 5.4, gain * 0.065f, 25, loop);
        }

        /// <summary>整数倍に揃わない金属の共鳴モードを加える。</summary>
        private static void AddMetal(float[] samples, int rate, double start, double duration,
            double frequency, float gain)
        {
            frequency = Math.Min(frequency, rate * 0.2);
            AddResonance(samples, rate, start, duration, frequency, gain, 5.5, false);
            AddResonance(samples, rate, start, duration * 0.7, frequency * 1.523, gain * 0.45f, 9, false);
            AddResonance(samples, rate, start, duration * 0.5, frequency * 2.117, gain * 0.25f, 13, false);
        }

        /// <summary>三角関数の漸化式で減衰正弦共鳴を合成する。</summary>
        private static void AddResonance(float[] samples, int rate, double start, double duration,
            double frequency, float gain, double decay, bool loop)
        {
            if (frequency >= rate * 0.45) return;
            int offset = (int)Math.Round(start * rate);
            int length = (int)Math.Ceiling(duration * rate);
            double angle = Tau * frequency / rate;
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            double x = 1;
            double y = 0;
            double amplitude = gain;
            double damping = Math.Exp(-decay / rate);
            for (int i = 0; i < length; i++)
            {
                double time = i / (double)rate;
                double value = y * amplitude * Envelope(time, duration, 0.0025, 0.025);
                Mix(samples, offset + i, (float)value, loop);
                double nextX = x * cosine - y * sine;
                y = y * cosine + x * sine;
                x = nextX;
                amplitude *= damping;
            }
        }

        /// <summary>ビブラートと柔らかな倍音を持つ竹笛風の持続音を加える。</summary>
        private static void AddTone(float[] samples, int rate, double start, double duration,
            double frequency, float gain, double vibrato, bool loop)
        {
            frequency = Math.Min(frequency, rate * 0.4);
            int offset = (int)Math.Round(start * rate);
            int length = (int)Math.Ceiling(duration * rate);
            double phase = 0;
            double phaseStep = Tau * frequency / rate;
            double breathX = 1;
            double breathY = 0;
            double breathCos = Math.Cos(Tau * vibrato / rate);
            double breathSin = Math.Sin(Tau * vibrato / rate);
            for (int i = 0; i < length; i++)
            {
                double time = i / (double)rate;
                double fundamental = Math.Sin(phase);
                double harmonic = frequency * 2 < rate * 0.45 ? Math.Sin(phase * 2 + 0.3) * 0.12 : 0;
                double envelope = Envelope(time, duration, Math.Min(0.08, duration * 0.2), Math.Min(0.18, duration * 0.3));
                double value = (fundamental + harmonic) * gain * envelope * (0.95 + breathY * 0.05);
                Mix(samples, offset + i, (float)value, loop);
                phase += phaseStep * (1 + breathY * (vibrato > 0 ? 0.004 : 0));
                if (phase >= Tau) phase -= Tau;
                double nextX = breathX * breathCos - breathY * breathSin;
                breathY = breathY * breathCos + breathX * breathSin;
                breathX = nextX;
            }
        }

        /// <summary>雑音励起と平均化遅延線によるKarplus–Strong撥弦を加える。</summary>
        private static void AddPluck(float[] samples, int rate, double start, double duration,
            double frequency, float gain, ref NoiseGenerator noise)
        {
            frequency = Math.Min(frequency, rate * 0.35);
            int delay = Math.Max(2, (int)Math.Round(rate / frequency - 0.5));
            float[] stringLine = new float[delay];
            double mean = 0;
            for (int i = 0; i < delay; i++)
            {
                stringLine[i] = noise.Signed();
                mean += stringLine[i];
            }
            float center = (float)(mean / delay);
            for (int i = 0; i < delay; i++) stringLine[i] -= center;
            int offset = (int)Math.Round(start * rate);
            int length = (int)Math.Ceiling(duration * rate);
            int cursor = 0;
            float damping = (float)Math.Exp(-3.2 / frequency);
            for (int i = 0; i < length; i++)
            {
                int next = cursor + 1 == delay ? 0 : cursor + 1;
                float value = stringLine[cursor];
                stringLine[cursor] = (value + stringLine[next]) * 0.5f * damping;
                double envelope = Envelope(i / (double)rate, duration, 0.003, 0.08);
                Mix(samples, offset + i, value * gain * (float)envelope, true);
                cursor = next;
            }
        }

        /// <summary>二つの一次低域フィルタの差で帯域雑音と流量のうねりを作る。</summary>
        private static void AddNoise(float[] samples, int rate, double start, double duration,
            double lowHz, double highHz, float gain, double modulation, bool loop, ref NoiseGenerator noise)
        {
            int offset = (int)Math.Round(start * rate);
            int length = (int)Math.Ceiling(duration * rate);
            double fastCoefficient = 1 - Math.Exp(-Tau * Math.Min(highHz, rate * 0.43) / rate);
            double slowCoefficient = 1 - Math.Exp(-Tau * Math.Min(lowHz, rate * 0.3) / rate);
            double fast = 0;
            double slow = 0;
            double modX = 1;
            double modY = 0;
            double modCos = Math.Cos(Tau * modulation / rate);
            double modSin = Math.Sin(Tau * modulation / rate);
            for (int i = 0; i < length; i++)
            {
                float input = noise.Signed();
                fast += fastCoefficient * (input - fast);
                slow += slowCoefficient * (input - slow);
                double time = i / (double)rate;
                double envelope = Envelope(time, duration, Math.Min(0.045, duration * 0.15), Math.Min(0.12, duration * 0.25));
                double flow = modulation > 0 ? 0.72 + 0.28 * modY : 1;
                Mix(samples, offset + i, (float)((fast - slow) * gain * envelope * flow), loop);
                double nextX = modX * modCos - modY * modSin;
                modY = modY * modCos + modX * modSin;
                modX = nextX;
            }
        }

        /// <summary>不規則な泡の破裂を全体へ散らす。</summary>
        private static void AddBubbles(float[] samples, int rate, int count, float gain,
            bool loop, ref NoiseGenerator noise)
        {
            for (int i = 0; i < count; i++)
            {
                double start = noise.Index(samples.Length) / (double)rate;
                double duration = 0.018 + noise.Index(85) / 1000.0;
                double frequency = 240 + noise.Index(1250);
                AddBubble(samples, rate, start, duration, frequency,
                    gain * (0.55f + noise.Index(45) / 100f), loop);
            }
        }

        /// <summary>周波数が下がる短い泡の共鳴を加える。</summary>
        private static void AddBubble(float[] samples, int rate, double start, double duration,
            double frequency, float gain, bool loop)
        {
            frequency = Math.Min(frequency, rate * 0.4);
            int offset = (int)Math.Round(start * rate);
            int length = (int)Math.Ceiling(duration * rate);
            double phase = 0;
            double amplitude = gain;
            double damping = Math.Exp(-7.0 / (duration * rate));
            for (int i = 0; i < length; i++)
            {
                double time = i / (double)rate;
                double envelope = Envelope(time, duration, 0.0015, 0.008);
                Mix(samples, offset + i, (float)(Math.Sin(phase) * amplitude * envelope), loop);
                phase += Tau * frequency * (1 - 0.65 * time / duration) / rate;
                if (phase >= Tau) phase -= Tau;
                amplitude *= damping;
            }
        }

        /// <summary>循環時には末尾の余韻を先頭へ折り返して加算する。</summary>
        private static void Mix(float[] samples, int index, float value, bool loop)
        {
            if (loop) index %= samples.Length;
            if (index >= 0 && index < samples.Length) samples[index] += value;
        }

        /// <summary>周期境界の遅延方程式y[n]=x[n]+g・y[n−d]を解き、軽い残響を加える。</summary>
        private static void ApplyReverb(float[] samples, int rate, double delaySeconds, float feedback, bool loop)
        {
            int delay = Math.Max(1, (int)Math.Round(rate * delaySeconds));
            if (loop)
            {
                // ゼロ状態からの一回処理では、ループ先頭だけ残響が欠けるため周期状態を求める。
                int divisor = GreatestCommonDivisor(samples.Length, delay);
                int cycleLength = samples.Length / divisor;
                double cycleFeedback = Math.Pow(feedback, cycleLength);
                for (int start = 0; start < divisor; start++)
                {
                    int index = start;
                    double tail = 0;
                    for (int j = 0; j < cycleLength; j++)
                    {
                        tail = samples[index] + feedback * tail;
                        index = (index + delay) % samples.Length;
                    }
                    double previous = tail / (1 - cycleFeedback);
                    index = start;
                    for (int j = 0; j < cycleLength; j++)
                    {
                        double value = samples[index] + feedback * previous;
                        samples[index] = (float)value;
                        previous = value;
                        index = (index + delay) % samples.Length;
                    }
                }
                return;
            }
            float[] line = new float[delay];
            int cursor = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float value = samples[i] + line[cursor] * feedback;
                line[cursor] = value;
                samples[i] = value;
                cursor = cursor + 1 == delay ? 0 : cursor + 1;
            }
        }

        /// <summary>遅延線が作る独立した周期の数を求める。</summary>
        private static int GreatestCommonDivisor(int a, int b)
        {
            while (b != 0)
            {
                int remainder = a % b;
                a = b;
                b = remainder;
            }
            return a;
        }

        /// <summary>直流成分を除き、短い端点フェードと0.94以下のピーク制限を施す。</summary>
        private static void Finish(float[] samples, int rate, double attack, double release)
        {
            double sum = 0;
            for (int i = 0; i < samples.Length; i++) sum += samples[i];
            float mean = (float)(sum / samples.Length);
            int fadeIn = Math.Max(1, (int)(attack * rate));
            int fadeOut = Math.Max(1, (int)(release * rate));
            float peak = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float envelope = Math.Min(1f, Math.Min(i / (float)fadeIn, (samples.Length - 1 - i) / (float)fadeOut));
                samples[i] = (samples[i] - mean) * envelope;
                peak = Math.Max(peak, Math.Abs(samples[i]));
            }
            if (peak > 0.94f)
            {
                float scale = 0.94f / peak;
                for (int i = 0; i < samples.Length; i++) samples[i] *= scale;
            }
        }

        /// <summary>現実的な標本化周波数だけを受け付ける。</summary>
        private static void ValidateRate(int rate)
        {
            if (rate < 1000 || rate > 192000) throw new ArgumentOutOfRangeException(nameof(rate), "標本化周波数は1000〜192000 Hzです。");
        }

        /// <summary>列挙値の範囲外を合成前に拒否する。</summary>
        private static void ValidateId(int id, int count, string name)
        {
            if (id < 0 || id >= count) throw new ArgumentOutOfRangeException(name);
        }
    }

    /// <summary>手続きクリップの所有、再生プール、環境音とBGMのフェードを管理する。</summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        private const int EffectCount = (int)Sfx.BicycleBell + 1;
        private const int AmbientCount = (int)AmbientLoop.Insects + 1;
        private const int MusicCount = (int)MusicMood.Evening + 1;
        private const int PoolSize = 24;
        private const int EffectRate = 44100;
        private const int LoopRate = 22050;
        private const float CrossfadeSeconds = 1.5f;
        private AudioClip[] effectClips;
        private AudioClip[] ambientClips;
        private AudioClip[] musicClips;
        private AudioSource[] effectSources;
        private AudioSource[] ambientSources;
        private AudioSource[] musicSources;
        private int[] lastEffectFrames;
        private float[] effectGains;
        private float[] ambientGains;
        private float[] ambientTargets;
        private float[] musicGains;
        private float[] musicStarts;
        private GameObject sourceRoot;
        private System.Random pitchRandom;
        private int nextEffectSource;
        private int currentMood = -1;
        private float musicFadeElapsed = CrossfadeSeconds;
        private float effectsVolume = 1f;
        private float musicVolume = 1f;
        private bool initialized;

        /// <summary>効果音および環境音のマスター音量。0〜1に制限し再生中の音にも即時反映する。</summary>
        public float EffectsVolume
        {
            get { return effectsVolume; }
            set
            {
                effectsVolume = Unit(value);
                ApplyEffectsVolume();
            }
        }

        /// <summary>BGMのマスター音量。0〜1に制限しクロスフェード中の音にも即時反映する。</summary>
        public float MusicVolume
        {
            get { return musicVolume; }
            set
            {
                musicVolume = Unit(value);
                ApplyMusicVolume();
            }
        }

        /// <summary>効果音と固定再生プールを作る。再呼び出しは無操作で、ループ音は初回使用時に生成する。</summary>
        public void Initialize()
        {
            if (initialized) return;
            try
            {
                effectClips = new AudioClip[EffectCount];
                ambientClips = new AudioClip[AmbientCount];
                musicClips = new AudioClip[MusicCount];
                effectSources = new AudioSource[PoolSize];
                ambientSources = new AudioSource[AmbientCount];
                musicSources = new AudioSource[MusicCount];
                lastEffectFrames = new int[EffectCount];
                effectGains = new float[PoolSize];
                ambientGains = new float[AmbientCount];
                ambientTargets = new float[AmbientCount];
                musicGains = new float[MusicCount];
                musicStarts = new float[MusicCount];
                pitchRandom = new System.Random(1729);
                sourceRoot = new GameObject("Procedural Audio Sources");
                sourceRoot.transform.SetParent(transform, false);
                for (int i = 0; i < EffectCount; i++)
                {
                    lastEffectFrames[i] = -1;
                    effectClips[i] = CreateClip(((Sfx)i).ToString(), ProceduralAudioSynth.Effect((Sfx)i, EffectRate), EffectRate);
                }
                for (int i = 0; i < PoolSize; i++) effectSources[i] = CreateSource("Effect " + i, false);
                for (int i = 0; i < AmbientCount; i++) ambientSources[i] = CreateSource("Ambient " + i, true);
                for (int i = 0; i < MusicCount; i++) musicSources[i] = CreateSource("Music " + i, true);
                initialized = true;
            }
            catch
            {
                ReleaseResources();
                throw;
            }
        }

        /// <summary>2D効果音を再生する。同一音の同一フレーム呼び出しは一度だけ再生する。</summary>
        public void Play(Sfx id, float volume = 1f, float pitchVariance = 0.05f)
        {
            int index = CheckedIndex((int)id, EffectCount, nameof(id));
            if (!isActiveAndEnabled) return;
            Initialize();
            float variance = Unit(pitchVariance);
            float pitch = 1f + (float)(pitchRandom.NextDouble() * 2 - 1) * variance;
            PlayEffect(index, transform.position, false, Unit(volume), Mathf.Max(0.1f, pitch));
        }

        public void PlayPitched(Sfx id, float pitch, float volume = 1f)
        {
            int index = CheckedIndex((int)id, EffectCount, nameof(id));
            if (!isActiveAndEnabled || !float.IsFinite(pitch)) return;
            Initialize();
            PlayEffect(index, transform.position, false, Unit(volume), Mathf.Clamp(pitch, .6f, 1.6f));
        }

        /// <summary>指定ワールド位置で3D効果音を再生する。空間混合率は0.7。</summary>
        public void PlayAt(Sfx id, Vector3 position, float volume = 1f)
        {
            int index = CheckedIndex((int)id, EffectCount, nameof(id));
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
                throw new ArgumentException("再生位置は有限値で指定してください。", nameof(position));
            if (!isActiveAndEnabled) return;
            Initialize();
            PlayEffect(index, position, true, Unit(volume), 1f);
        }

        /// <summary>環境音の強さを0〜1で設定する。音量は滑らかに追従し、ゼロへのフェード後に停止する。</summary>
        public void SetAmbient(AmbientLoop id, float intensity)
        {
            int index = CheckedIndex((int)id, AmbientCount, nameof(id));
            Initialize();
            float target = Unit(intensity);
            if (target > 0)
            {
                if (ambientClips[index] == null)
                    ambientClips[index] = CreateClip(id.ToString(), ProceduralAudioSynth.Ambient(id, LoopRate), LoopRate);
                AudioSource source = ambientSources[index];
                if (source.clip != ambientClips[index]) source.clip = ambientClips[index];
                if (!source.isPlaying && isActiveAndEnabled)
                {
                    source.volume = ambientGains[index] * effectsVolume;
                    source.Play();
                }
            }
            ambientTargets[index] = target;
        }

        /// <summary>BGMを必要時に生成し1.5秒でクロスフェードする。同じムードへの再設定は無操作。</summary>
        public void SetMusic(MusicMood mood)
        {
            int index = CheckedIndex((int)mood, MusicCount, nameof(mood));
            Initialize();
            if (currentMood == index) return;
            if (musicClips[index] == null)
                musicClips[index] = CreateClip(mood.ToString(), ProceduralAudioSynth.Music(mood, LoopRate), LoopRate);
            AudioSource source = musicSources[index];
            if (source.clip != musicClips[index]) source.clip = musicClips[index];
            if (!source.isPlaying && isActiveAndEnabled)
            {
                source.volume = musicGains[index] * musicVolume;
                source.Play();
            }
            // 二本だけの切替では途中の再変更で片方を突然切るため、ムードごとの固定ソースを使う。
            for (int i = 0; i < MusicCount; i++) musicStarts[i] = musicGains[i];
            currentMood = index;
            musicFadeElapsed = 0;
        }

        /// <summary>停止時間に影響されない音量追従を、配列の再利用だけで更新する。</summary>
        private void Update()
        {
            if (!initialized) return;
            float delta = Time.unscaledDeltaTime;
            for (int i = 0; i < AmbientCount; i++)
            {
                ambientGains[i] = Mathf.MoveTowards(ambientGains[i], ambientTargets[i], delta * 1.5f);
                ambientSources[i].volume = ambientGains[i] * effectsVolume;
                if (ambientTargets[i] == 0 && ambientGains[i] == 0 && ambientSources[i].isPlaying)
                    ambientSources[i].Stop();
            }
            if (musicFadeElapsed < CrossfadeSeconds)
            {
                musicFadeElapsed = Mathf.Min(CrossfadeSeconds, musicFadeElapsed + delta);
                float fraction = musicFadeElapsed / CrossfadeSeconds;
                for (int i = 0; i < MusicCount; i++)
                {
                    musicGains[i] = Mathf.Lerp(musicStarts[i], i == currentMood ? 1f : 0f, fraction);
                    musicSources[i].volume = musicGains[i] * musicVolume;
                    if (fraction == 1f && i != currentMood && musicSources[i].isPlaying) musicSources[i].Stop();
                }
            }
        }

        /// <summary>再有効化ではループの再生位置を保って再開する。</summary>
        private void OnEnable()
        {
            if (!initialized) return;
            for (int i = 0; i < AmbientCount; i++)
            {
                if (ambientTargets[i] > 0 || ambientGains[i] > 0)
                {
                    ambientSources[i].UnPause();
                    if (!ambientSources[i].isPlaying) ambientSources[i].Play();
                }
            }
            for (int i = 0; i < MusicCount; i++)
            {
                if (i == currentMood || musicGains[i] > 0)
                {
                    musicSources[i].UnPause();
                    if (!musicSources[i].isPlaying) musicSources[i].Play();
                }
            }
        }

        /// <summary>無効化時に効果音を停止し、ループを一時停止する。</summary>
        private void OnDisable()
        {
            if (!initialized) return;
            for (int i = 0; i < PoolSize; i++) effectSources[i].Stop();
            for (int i = 0; i < AmbientCount; i++) ambientSources[i].Pause();
            for (int i = 0; i < MusicCount; i++) musicSources[i].Pause();
        }

        /// <summary>自身が所有するクリップと再生オブジェクトだけを解放する。</summary>
        private void OnDestroy()
        {
            ReleaseResources();
        }

        /// <summary>空きソースを優先し、満杯なら巡回順で置き換える。</summary>
        private void PlayEffect(int index, Vector3 position, bool spatial, float volume, float pitch)
        {
            if (volume <= 0 || lastEffectFrames[index] == Time.frameCount) return;
            lastEffectFrames[index] = Time.frameCount;
            int slot = nextEffectSource;
            for (int i = 0; i < PoolSize; i++)
            {
                int candidate = (nextEffectSource + i) % PoolSize;
                if (!effectSources[candidate].isPlaying)
                {
                    slot = candidate;
                    break;
                }
            }
            nextEffectSource = (slot + 1) % PoolSize;
            AudioSource source = effectSources[slot];
            source.Stop();
            source.clip = effectClips[index];
            source.transform.position = position;
            source.spatialBlend = spatial ? 0.7f : 0f;
            source.pitch = pitch;
            effectGains[slot] = volume;
            source.volume = volume * effectsVolume;
            source.Play();
        }

        /// <summary>再生用の子オブジェクトを初期化時だけ作る。</summary>
        private AudioSource CreateSource(string sourceName, bool loop)
        {
            GameObject child = new GameObject(sourceName);
            child.transform.SetParent(sourceRoot.transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = 0;
            source.spatialBlend = 0;
            source.dopplerLevel = 0;
            source.minDistance = 1f;
            source.maxDistance = 18f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            return source;
        }

        /// <summary>生成PCMをネイティブクリップに転送し、失敗時には解放する。</summary>
        private static AudioClip CreateClip(string clipName, float[] samples, int rate)
        {
            AudioClip clip = AudioClip.Create("Procedural " + clipName, samples.Length, 1, rate, false);
            if (clip == null) throw new InvalidOperationException("オーディオクリップを作成できませんでした。");
            try
            {
                if (!clip.SetData(samples, 0)) throw new InvalidOperationException("オーディオデータを転送できませんでした。");
                return clip;
            }
            catch
            {
                UnityEngine.Object.Destroy(clip);
                throw;
            }
        }

        /// <summary>再生中の効果音と環境音へマスター音量を反映する。</summary>
        private void ApplyEffectsVolume()
        {
            if (!initialized) return;
            for (int i = 0; i < PoolSize; i++) effectSources[i].volume = effectGains[i] * effectsVolume;
            for (int i = 0; i < AmbientCount; i++) ambientSources[i].volume = ambientGains[i] * effectsVolume;
        }

        /// <summary>クロスフェードの割合を保ってマスター音量を反映する。</summary>
        private void ApplyMusicVolume()
        {
            if (!initialized) return;
            for (int i = 0; i < MusicCount; i++) musicSources[i].volume = musicGains[i] * musicVolume;
        }

        /// <summary>部分初期化を含めて所有リソースを解放する。</summary>
        private void ReleaseResources()
        {
            initialized = false;
            if (sourceRoot != null)
            {
                sourceRoot.SetActive(false);
                UnityEngine.Object.Destroy(sourceRoot);
                sourceRoot = null;
            }
            DestroyClips(effectClips);
            DestroyClips(ambientClips);
            DestroyClips(musicClips);
            currentMood = -1;
            nextEffectSource = 0;
            musicFadeElapsed = CrossfadeSeconds;
        }

        /// <summary>配列に格納されたクリップを重複なく解放する。</summary>
        private static void DestroyClips(AudioClip[] clips)
        {
            if (clips == null) return;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null) UnityEngine.Object.Destroy(clips[i]);
                clips[i] = null;
            }
        }

        /// <summary>非数を無音へ変換し、音量または変動幅を単位区間へ制限する。</summary>
        private static float Unit(float value)
        {
            return float.IsNaN(value) ? 0 : Mathf.Clamp01(value);
        }


        /// <summary>無効な列挙値を初期化や配列アクセスの前に拒否する。</summary>
        private static int CheckedIndex(int index, int count, string name)
        {
            if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(name);
            return index;
        }
    }
}
