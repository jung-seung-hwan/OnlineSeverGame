using System.Collections.Generic;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // Blender 로 렌더한 그림(Assets/Resources/Art, 원본 Art/sprites)을 불러와 스프라이트로 자른다.
    // 캐릭터 시트 규칙(프레임 크기·피벗·동작별 행·프레임 수·fps)은 jelly_sheet.json 을 그대로 읽는다.
    // 맵 조각이 덮는 월드 사각형은 Art/sprites/map/map.json 의 값이다.
    public static class ArtSprites
    {
        [System.Serializable]
        internal sealed class SheetAnim
        {
            public string name;
            public int row;
            public int frames;
            public float fps;
            public bool loop;
        }

        [System.Serializable]
        internal sealed class SheetMeta
        {
            public int ppu;
            public int[] frame_size;
            public int[] pivot_px_from_bottom_left;
            public SheetAnim[] animations;
        }

        public sealed class Clip
        {
            public string Name;
            public Sprite[] Frames;
            public float Fps;
            public bool Loop;

            // 반복하지 않는 동작은 마지막 프레임에서 멈춘다.
            public Sprite At(float time)
            {
                int i = Mathf.FloorToInt(time * Fps);
                i = Loop ? i % Frames.Length : Mathf.Min(i, Frames.Length - 1);
                return Frames[Mathf.Max(0, i)];
            }
        }

        const string Dir = "Art/";
        static readonly string[] Sheets =
        {
            "jelly_1_strawberry", "jelly_2_blueberry", "jelly_3_lime", "jelly_4_lemon",
            "jelly_5_grape", "jelly_6_tangerine", "jelly_7_soda", "jelly_8_peach",
        };

        public const float MapPpu = 72f;
        public static readonly Rect BackgroundRect = Rect.MinMaxRect(-1.5f, -1.5f, 25.5f, 14.0f);
        public static readonly Rect FloorRect = Rect.MinMaxRect(-1.5f, -1.5f, 25.5f, 0.25f);
        // 발판 윗면 가운데 기준
        public static readonly Rect ShelfRect = Rect.MinMaxRect(-3.25f, -1.0f, 3.25f, 0.125f);
        public static readonly Rect LidRect = Rect.MinMaxRect(-3.25f, -1.25f, 3.25f, 0.25f);

        static Dictionary<string, Clip>[] _characters;
        static Sprite _knife;
        static readonly Dictionary<string, Sprite> _map = new Dictionary<string, Sprite>();

        public static Clip Character(int colorIndex, string anim)
        {
            if (_characters == null) LoadCharacters();
            var set = _characters[((colorIndex % Sheets.Length) + Sheets.Length) % Sheets.Length];
            return set.TryGetValue(anim, out var clip) ? clip : set["idle"];
        }

        public static Sprite Knife
        {
            get
            {
                if (_knife != null) return _knife;
                var tex = Resources.Load<Texture2D>(Dir + "knife/knife");
                // 칼끝(32, 8) px 이 피벗 = 칼 위치
                _knife = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(32f / tex.width, 8f / tex.height), 128f, 0, SpriteMeshType.FullRect);
                _knife.name = "knife";
                return _knife;
            }
        }

        // rect 는 그림이 덮는 사각형. 원점(0, 0)이 피벗이 되도록 자른다.
        public static Sprite Map(string name, Rect rect)
        {
            if (_map.TryGetValue(name, out var s)) return s;
            var tex = Resources.Load<Texture2D>(Dir + "map/" + name);
            var pivot = new Vector2(-rect.xMin / rect.width, -rect.yMin / rect.height);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, MapPpu, 0, SpriteMeshType.FullRect);
            s.name = name;
            _map.Add(name, s);
            return s;
        }

        static void LoadCharacters()
        {
            var meta = JsonUtility.FromJson<SheetMeta>(Resources.Load<TextAsset>(Dir + "characters/jelly_sheet").text);
            int fw = meta.frame_size[0], fh = meta.frame_size[1];
            var pivot = new Vector2((float)meta.pivot_px_from_bottom_left[0] / fw, (float)meta.pivot_px_from_bottom_left[1] / fh);
            _characters = new Dictionary<string, Clip>[Sheets.Length];
            for (int c = 0; c < Sheets.Length; c++)
            {
                var tex = Resources.Load<Texture2D>(Dir + "characters/" + Sheets[c]);
                var set = new Dictionary<string, Clip>();
                foreach (var a in meta.animations)
                {
                    var frames = new Sprite[a.frames];
                    for (int i = 0; i < a.frames; i++)
                    {
                        // 시트 맨 위 행이 row 0
                        var r = new Rect(i * fw, tex.height - (a.row + 1) * fh, fw, fh);
                        frames[i] = Sprite.Create(tex, r, pivot, meta.ppu, 0, SpriteMeshType.FullRect);
                        frames[i].name = Sheets[c] + "_" + a.name + "_" + i;
                    }
                    set.Add(a.name, new Clip { Name = a.name, Frames = frames, Fps = a.fps, Loop = a.loop });
                }
                _characters[c] = set;
            }
        }
    }
}
