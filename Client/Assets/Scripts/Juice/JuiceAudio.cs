using UnityEngine;

namespace OnlineSeverGame.Client.Juice
{
    public enum Sfx
    {
        Jump, Land, Dash, DashHit, Stomp, Crouch, Death, Stun, Immune,
        Warn, SideWarn, LandKnife, LandFork, LandIcicle, LandCrate, SidePass,
        Count, Go, Win, Lose, Click, Spring,
    }

    // 효과음·배경음 재생(docs/work/juice-sound.md 0장 기준 v2, 2장 구조).
    // 소리는 Client/SoundGen/gen_sounds.py 가 만든 Resources/Audio/*.wav, 소리별 크기 배수는 같은 생성기가 쓴 JuiceLevels.
    // - 효과음: 동시에 14개까지(같은 소리는 3개, 내 소리 0.045초·남의 소리 0.08초 안에 다시 내지 않음). 자리가 없으면 우선순위가
    //   낮은(남의 소리) 가장 오래된 소리를 0.03초 동안 줄여 끄고(바로 끊으면 '딱' 소리) 남는 목소리로 낸다(목소리는 18개).
    //   남의 소리는 0.4배 + 4.5 kHz 로우패스(멀게 들리고 고음이 앞으로 튀지 않게).
    // - 배경음: 소스 두 개로 맵이 바뀌면 1초 동안 겹쳐 바꾼다(같은 크기 곡선). 징글(승리·패배) 동안 낮춘다. 슬로모션 동안 음높이를 조금 내린다.
    // - 설정(효과음·배경음 음량, 화면 흔들림)은 PlayerPrefs 에 기억한다. 화면은 JuiceSettings(설정 창 '소리' 칸).
    public sealed class JuiceAudio : MonoBehaviour
    {
        const int Voices = 18;          // 실제 목소리. 이 중 14개까지 동시에 울리고 나머지는 끊기는 소리가 줄어드는 동안 쓴다
        const int MaxActive = 14;
        const int MaxSameSound = 3;
        const float SameSoundGap = 0.045f;
        const float OtherSameSoundGap = 0.08f;
        const float FadeOutSeconds = 0.03f;
        const float OtherLowpass = 4500f;
        public const float OtherPlayerVolume = 0.4f;
        const float BgmBase = 0.7f;
        const float BgmFade = 1f;

        static readonly string[] Files =
        {
            "sfx_jump", "sfx_land", "sfx_dash", "sfx_dash_hit", "sfx_stomp", "sfx_crouch", "sfx_death", "sfx_stun", "sfx_immune",
            "sfx_warn", "sfx_side_warn", "sfx_land_knife", "sfx_land_fork", "sfx_land_icicle", "sfx_land_crate", "sfx_side_pass",
            "sfx_count", "sfx_go", "sfx_win", "sfx_lose", "sfx_click", "sfx_spring",
        };

        // 소리별 크기 배수(종류별 목표 크기 - 파일 크기). 생성기가 JuiceLevels.cs 로 쓴다.
        static readonly float[] BaseVolume = new float[Files.Length];
        float _bgmGainA = 1f, _bgmGainB = 1f;

        static readonly string[] BgmFiles = { "bgm_kitchen", "bgm_winter", "bgm_warehouse", "bgm_dessert" };

        public static JuiceAudio Instance { get; private set; }

        public static float SfxVolume = 0.8f;
        public static float BgmVolume = 0.6f;
        public static bool ScreenShake = true;

        sealed class Voice
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public int Sound = -1;
            public float StartTime;
            public int Priority;
            public float FadeFrom = -1f;   // >= 0 이면 줄어드는 중(시작한 음량)
            public float FadeLeft;
            public bool Busy => Source.isPlaying;
            public bool Active => Source.isPlaying && FadeFrom < 0f;
        }

        readonly Voice[] _voices = new Voice[Voices];
        readonly AudioClip[] _clips = new AudioClip[Files.Length];
        readonly float[] _lastStart = new float[Files.Length];
        readonly AudioClip[] _bgm = new AudioClip[BgmFiles.Length];
        AudioSource _bgmA, _bgmB;
        int _bgmMap = -1;
        float _fade = 1f;
        float _duckUntil;
        float _duck = 1f;
        public float BgmPitch = 1f;

        // 시험·문서용: 소리별 재생 횟수(로그로 낸다)
        public readonly int[] PlayCounts = new int[Files.Length];
        public int Dropped;

        public static JuiceAudio Create(GameObject host)
        {
            var a = host.AddComponent<JuiceAudio>();
            Instance = a;
            return a;
        }

        void Awake()
        {
            LoadPrefs();
            // 씬 카메라에 AudioListener 가 없다(ProjectSetup 이 카메라만 만든다). 2D 소리라 위치는 상관없다.
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            for (int i = 0; i < Files.Length; i++)
            {
                _clips[i] = Resources.Load<AudioClip>("Audio/" + Files[i]);
                if (_clips[i] == null) Debug.LogWarning("[Juice] missing sound Audio/" + Files[i]);
                _lastStart[i] = -10f;
                BaseVolume[i] = JuiceLevels.Sfx(Files[i]);
            }
            for (int i = 0; i < BgmFiles.Length; i++) _bgm[i] = Resources.Load<AudioClip>("Audio/" + BgmFiles[i]);
            for (int i = 0; i < Voices; i++)
            {
                // 목소리마다 오브젝트를 따로 둔다(로우패스 필터는 같은 오브젝트의 소리에 걸리므로)
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                var f = go.AddComponent<AudioLowPassFilter>();
                f.cutoffFrequency = OtherLowpass;
                f.lowpassResonanceQ = 1f;
                f.enabled = false;
                _voices[i] = new Voice { Source = s, Filter = f };
            }
            _bgmA = NewBgmSource();
            _bgmB = NewBgmSource();
        }

        AudioSource NewBgmSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.volume = 0f;
            return s;
        }

        public static void LoadPrefs()
        {
            SfxVolume = PlayerPrefs.GetFloat("vol_sfx", 0.8f);
            BgmVolume = PlayerPrefs.GetFloat("vol_bgm", 0.6f);
            ScreenShake = PlayerPrefs.GetInt("juice_shake", 1) != 0;
        }

        public static void SavePrefs()
        {
            PlayerPrefs.SetFloat("vol_sfx", SfxVolume);
            PlayerPrefs.SetFloat("vol_bgm", BgmVolume);
            PlayerPrefs.SetInt("juice_shake", ScreenShake ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void UiClick() => Instance?.Play(Sfx.Click, 1f, 0f, true, 0.03f);

        // volume: 0..1 상황별 배수(거리 등). local: 내 캐릭터·내 화면 소리면 true. pan: -1 왼쪽 .. 1 오른쪽.
        public bool Play(Sfx sfx, float volume = 1f, float pan = 0f, bool local = true, float pitchJitter = 0.04f)
        {
            int id = (int)sfx;
            var clip = _clips[id];
            if (clip == null || SfxVolume <= 0f || volume <= 0.01f) return false;
            float now = Time.unscaledTime;
            if (now - _lastStart[id] < (local ? SameSoundGap : OtherSameSoundGap)) { Dropped++; return false; }

            int priority = local ? 2 : 1;
            int same = 0, active = 0;
            Voice oldestSame = null;
            foreach (var v in _voices)
            {
                if (!v.Active) continue;
                active++;
                if (v.Sound != id) continue;
                same++;
                if (oldestSame == null || v.StartTime < oldestSame.StartTime) oldestSame = v;
            }

            // 비울 소리 고르기: 같은 소리가 3개면 그중 가장 오래된 것, 전체가 14개면 우선순위 낮은 가장 오래된 것
            Voice victim = null;
            if (same >= MaxSameSound)
            {
                if (oldestSame.Priority > priority) { Dropped++; return false; }
                victim = oldestSame;
            }
            else if (active >= MaxActive)
            {
                foreach (var v in _voices)
                {
                    if (!v.Active || v.Priority > priority) continue;
                    if (victim == null || v.Priority < victim.Priority || (v.Priority == victim.Priority && v.StartTime < victim.StartTime)) victim = v;
                }
                if (victim == null) { Dropped++; return false; }
            }

            Voice pick = null;
            foreach (var v in _voices)
                if (!v.Busy) { pick = v; break; }
            if (victim != null)
            {
                if (pick == null) pick = victim;      // 남는 목소리가 없을 때만 바로 끊는다
                else StartFade(victim);
            }
            if (pick == null) { Dropped++; return false; }

            pick.Source.Stop();
            pick.FadeFrom = -1f;
            pick.Source.clip = clip;
            pick.Source.volume = Mathf.Clamp01(Mathf.Clamp01(volume) * BaseVolume[id] * (local ? 1f : OtherPlayerVolume) * SfxVolume);
            pick.Filter.enabled = !local;
            pick.Source.panStereo = Mathf.Clamp(pan, -1f, 1f) * (local ? 0.3f : 0.6f);
            pick.Source.pitch = (1f + Random.Range(-pitchJitter, pitchJitter)) * Mathf.Lerp(1f, BgmPitch, 0.5f);
            pick.Source.Play();
            pick.Sound = id;
            pick.StartTime = now;
            pick.Priority = priority;
            _lastStart[id] = now;
            PlayCounts[id]++;
            if (sfx == Sfx.Win || sfx == Sfx.Lose) _duckUntil = now + 2.2f;
            return true;
        }

        static void StartFade(Voice v)
        {
            v.FadeFrom = v.Source.volume;
            v.FadeLeft = FadeOutSeconds;
        }

        // 시험용: 줄여서 끈 소리 수
        public int Faded;

        public int ActiveVoices
        {
            get
            {
                int n = 0;
                foreach (var v in _voices) if (v.Active) n++;
                return n;
            }
        }

        public static string SoundName(int id) => Files[id];

        // 맵 id(1..4) 의 배경음으로 바꾼다. 같은 맵이면 아무것도 안 한다.
        public void SetMap(int mapId)
        {
            int index = Mathf.Clamp(mapId - 1, 0, _bgm.Length - 1);
            if (index == _bgmMap) return;
            _bgmMap = index;
            var clip = _bgm[index];
            if (clip == null) return;
            // 지금 나는 쪽을 B 로 돌리고 새 곡을 A 에서 키운다
            var t = _bgmA; _bgmA = _bgmB; _bgmB = t;
            _bgmGainB = _bgmGainA;
            _bgmGainA = JuiceLevels.Bgm(BgmFiles[index]);
            _bgmA.clip = clip;
            _bgmA.time = 0f;
            _bgmA.Play();
            _fade = 0f;
            Debug.Log("[Juice] bgm " + BgmFiles[index]);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _fade = Mathf.MoveTowards(_fade, 1f, dt / BgmFade);
            _duck = Mathf.MoveTowards(_duck, Time.unscaledTime < _duckUntil ? 0.35f : 1f, dt * 2.5f);
            float v = BgmVolume * BgmBase * _duck;
            // 같은 크기로 들리게 사인·코사인 곡선으로 겹쳐 바꾼다
            _bgmA.volume = Mathf.Clamp01(v * _bgmGainA * Mathf.Sin(_fade * Mathf.PI / 2f));
            _bgmB.volume = Mathf.Clamp01(v * _bgmGainB * Mathf.Cos(_fade * Mathf.PI / 2f));
            if (_fade >= 1f && _bgmB.isPlaying) _bgmB.Stop();
            _bgmA.pitch = _bgmB.pitch = BgmPitch;

            // 끊길 소리를 짧게 줄여서 멈춘다
            foreach (var voice in _voices)
            {
                if (voice.FadeFrom < 0f) continue;
                voice.FadeLeft -= dt;
                if (voice.FadeLeft <= 0f || !voice.Source.isPlaying)
                {
                    voice.Source.Stop();
                    voice.FadeFrom = -1f;
                    Faded++;
                }
                else voice.Source.volume = voice.FadeFrom * (voice.FadeLeft / FadeOutSeconds);
            }
        }
    }
}
