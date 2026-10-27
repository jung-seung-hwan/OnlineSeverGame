using System.Collections.Generic;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // Blender 로 렌더한 그림(Assets/Resources/Art, 원본 Art/sprites)을 불러와 스프라이트로 자른다.
    // 캐릭터 시트 규칙(프레임 크기·피벗·동작별 행·프레임 수·fps)은 jelly_sheet.json 을 그대로 읽는다.
    // 맵 조각이 덮는 월드 사각형은 Art/sprites/map/map.json 의 값이다.
    // 새 맵(겨울 처마·창고·디저트 가게)은 Resources/Art/map_<이름>/ 에 있고, 좌표·피벗은 그 폴더 map.json
    // (= docs/work/maps.md 5장) 값이다. 그림이 없으면 null 을 돌려주고 부르는 쪽이 단색 도형으로 그린다.
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
        static readonly Dictionary<string, Sprite> _props = new Dictionary<string, Sprite>();

        public const float PropPpu = 128f;
        // 아이스크림 9-slice: 양 끝 고정 폭(여백 0.25 + 둥근 끝 0.5 유닛), 윗면은 그림 위에서 18 px 아래
        public const int IceCreamBorderPx = 54;
        public const int IceCreamTopFromImageTopPx = 18;

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

        // 새 맵 정지 조각(72 px/유닛). 피벗 = 그림 왼쪽 아래 → map.json world_rect 의 (x0, y0)에 놓는다.
        public static Sprite MapPiece(string folder, string name)
        {
            string key = folder + "/" + name;
            if (_map.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>(Dir + key);
            if (tex != null)
            {
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero, MapPpu, 0, SpriteMeshType.FullRect);
                s.name = name;
            }
            _map[key] = s;
            return s;
        }

        // 녹는 아이스크림 발판(9-slice). 피벗 = 윗면 가운데. SpriteRenderer.drawMode = Sliced 로 폭을 바꾼다.
        public static Sprite IceCream(string name)
        {
            string key = "map_dessert/" + name;
            if (_map.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>(Dir + key);
            if (tex != null)
            {
                var pivot = new Vector2(0.5f, (tex.height - IceCreamTopFromImageTopPx) / (float)tex.height);
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, MapPpu, 0, SpriteMeshType.FullRect,
                    new Vector4(IceCreamBorderPx, 0, IceCreamBorderPx, 0));
                s.name = name;
            }
            _map[key] = s;
            return s;
        }

        // 움직이는 것(위험물·상자·흔들리는 장식, 128 px/유닛). pivotPx 는 그림 왼쪽 아래에서 잰 피벗.
        public static Sprite Prop(string folder, string name, float pivotX, float pivotY)
        {
            string key = folder + "/" + name;
            if (_props.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>(Dir + key);
            if (tex != null)
            {
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(pivotX / tex.width, pivotY / tex.height),
                    PropPpu, 0, SpriteMeshType.FullRect);
                s.name = name;
            }
            _props[key] = s;
            return s;
        }

        // 위험물 그림(낙하·예고 중). 칼은 Knife. 피벗 = 끝(아래 가운데).
        public static Sprite Hazard(HazardKind kind)
        {
            switch (kind)
            {
                case HazardKind.Icicle: return Prop("map_winter", "icicle", 32, 8);
                case HazardKind.Crate: return Prop("map_warehouse", "crate", 96, 8);
                case HazardKind.Fork: return Prop("map_dessert", "fork", 40, 8);
                default: return Knife;
            }
        }

        // 착지 뒤 그림: 고드름은 부서진 조각, 상자는 금 간 상자(2단 위에서 부서질 때). 없으면 같은 그림.
        public static Sprite HazardLanded(HazardKind kind)
        {
            switch (kind)
            {
                case HazardKind.Icicle: return Prop("map_winter", "icicle_shards", 64, 8);
                case HazardKind.Crate: return Prop("map_warehouse", "crate_cracked", 96, 8);
                default: return Hazard(kind);
            }
        }

        // 맵별 밀리는 상자 그림(피벗 = 아래 가운데). 주방 버터 블록은 그림이 없어 null.
        public static Sprite Box(int stageId, bool cracked)
        {
            switch (stageId)
            {
                case 2: return Prop("map_winter", "snowball", 72, 8);
                case 3: return cracked ? Prop("map_warehouse", "crate_cracked", 96, 8) : Prop("map_warehouse", "crate", 96, 8);
                case 4: return Prop("map_dessert", "macaron", 72, 8);
                default: return null;
            }
        }

        // 흔들리는 장식 그림(피벗 = 매달린 점). 주방 장식은 도형이라 null.
        public static Sprite DecorSprite(string kind)
        {
            switch (kind)
            {
                case "lantern": return Prop("map_winter", "lantern", 48, 160);
                case "hook": return Prop("map_warehouse", "crane_hook", 32, 248);
                case "lollipop": return Prop("map_dessert", "lollipop_sign", 96, 192);
                case "lamp": return Prop("map_dessert", "pendant_lamp", 48, 208);
                default: return null;
            }
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
