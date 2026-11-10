using System.Collections.Generic;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // Blender 로 렌더한 그림(Assets/Resources/Art, 원본 Art/sprites)을 불러와 스프라이트로 자른다.
    // 캐릭터 시트 규칙(프레임 크기·피벗·동작별 행·프레임 수·fps)은 종류별 <종류>_sheet.json 을 그대로 읽는다.
    // 캐릭터는 종류(젤리·젤리곰·푸딩, docs/work/char-select.md) × 색 8가지. 시트 한 장이 2048 × 4096 비압축(32MB)이라
    // 24장을 한꺼번에 읽지 않고 처음 쓰일 때 그 (종류, 색) 한 장만 읽는다. 시트가 없으면 젤리로 그린다.
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
            public float[] draw_scale;   // 그릴 때 곱할 (x, y) 배율. 없으면 1. 납작 기절은 프레임보다 넓어 x 1.1
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
            public Vector2 DrawScale = Vector2.one;

            public float Duration => Frames.Length / Fps;

            // 반복하지 않는 동작은 마지막 프레임에서 멈춘다.
            public Sprite At(float time)
            {
                int i = Mathf.FloorToInt(time * Fps);
                i = Loop ? i % Frames.Length : Mathf.Min(i, Frames.Length - 1);
                return Frames[Mathf.Max(0, i)];
            }
        }

        const string Dir = "Art/";
        // 시트 파일 = <종류>_<색>. 색 순서 = 플레이어 번호 순서(PlayerColors).
        static readonly string[] ColorSheets =
        {
            "1_strawberry", "2_blueberry", "3_lime", "4_lemon", "5_grape", "6_tangerine", "7_soda", "8_peach",
        };
        const int Kinds = ProtocolConstants.MaxCharacter;

        public const float MapPpu = 72f;
        public static readonly Rect BackgroundRect = Rect.MinMaxRect(-1.5f, -1.5f, 25.5f, 14.0f);
        public static readonly Rect FloorRect = Rect.MinMaxRect(-1.5f, -1.5f, 25.5f, 0.25f);
        // 발판 윗면 가운데 기준
        public static readonly Rect ShelfRect = Rect.MinMaxRect(-3.25f, -1.0f, 3.25f, 0.125f);
        public static readonly Rect LidRect = Rect.MinMaxRect(-3.25f, -1.25f, 3.25f, 0.25f);

        // [종류 - 1, 색] = 동작 이름 → 클립. null = 아직 안 읽음
        static readonly Dictionary<string, Clip>[,] _characters = new Dictionary<string, Clip>[Kinds, 8];
        static readonly SheetMeta[] _metas = new SheetMeta[Kinds];
        static Sprite _knife;
        static readonly Dictionary<string, Sprite> _map = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> _props = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite[]> _frames = new Dictionary<string, Sprite[]>();

        public const float PropPpu = 128f;
        // 아이스크림 9-slice: 양 끝 고정 폭(여백 0.25 + 둥근 끝 0.5 유닛), 윗면은 그림 위에서 18 px 아래
        public const int IceCreamBorderPx = 54;
        public const int IceCreamTopFromImageTopPx = 18;

        // 시트 파일 이름 앞부분(jelly / bear / pudding). 모르는 값은 젤리.
        public static string KindPrefix(CharacterKind kind)
        {
            switch (kind)
            {
                case CharacterKind.Bear: return "bear";
                case CharacterKind.Pudding: return "pudding";
                default: return "jelly";
            }
        }

        public static Clip Character(int colorIndex, string anim) => Character(CharacterKind.Jelly, colorIndex, anim);

        public static Clip Character(CharacterKind kind, int colorIndex, string anim)
        {
            var set = CharacterSet(kind, colorIndex);
            return set.TryGetValue(anim, out var clip) ? clip : set["idle"];
        }

        // 시트에 그 동작 행이 있는가(예전 8행 시트에는 엎드림·납작 기절·이모트 등이 없다). 세 종류의 json 은 행이 같다.
        public static bool HasCharacter(string anim)
        {
            var meta = Meta(CharacterKind.Jelly);
            foreach (var a in meta.animations) if (a.name == anim) return true;
            return false;
        }

        static Dictionary<string, Clip> CharacterSet(CharacterKind kind, int colorIndex)
        {
            int k = ProtocolConstants.IsValidCharacter((byte)kind) ? (byte)kind - 1 : 0;
            int c = ((colorIndex % ColorSheets.Length) + ColorSheets.Length) % ColorSheets.Length;
            var set = _characters[k, c];
            if (set != null) return set;
            set = LoadSheet((CharacterKind)(k + 1), c);
            if (set == null && k != 0) set = CharacterSet(CharacterKind.Jelly, c); // 그림이 없으면 젤리
            _characters[k, c] = set;
            return set;
        }

        static SheetMeta Meta(CharacterKind kind)
        {
            int k = (byte)kind - 1;
            if (_metas[k] != null) return _metas[k];
            var text = Resources.Load<TextAsset>(Dir + "characters/" + KindPrefix(kind) + "_sheet");
            _metas[k] = text != null ? JsonUtility.FromJson<SheetMeta>(text.text) : null;
            return _metas[k];
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

        // 새 배치의 72 px/유닛 그림을 임의 피벗(0~1)·9-slice 테두리(px)로 자른다: 케이크 리프트 기둥(위 가운데),
        // 시럽 웅덩이(양 끝 36 px), 컨베이어 벨트 무늬(왼쪽 아래). docs/work/map-visual.md 6장.
        public static Sprite MapSprite(string folder, string name, Vector2 pivot, Vector4 border = default)
        {
            string key = folder + "/" + name + "#" + pivot.x + "," + pivot.y;
            if (_map.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>(Dir + folder + "/" + name);
            if (tex != null)
            {
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, MapPpu, 0, SpriteMeshType.FullRect, border);
                s.name = name;
            }
            _map[key] = s;
            return s;
        }

        // 프레임이 가로로 늘어선 128 px/유닛 그림(화구 불꽃 4프레임). pivotPx 는 한 프레임 왼쪽 아래에서 잰 피벗. 없으면 null.
        public static Sprite[] PropFrames(string folder, string name, int frames, float pivotX, float pivotY)
        {
            string key = folder + "/" + name + "#" + frames;
            if (_frames.TryGetValue(key, out var list)) return list;
            var tex = Resources.Load<Texture2D>(Dir + folder + "/" + name);
            if (tex != null)
            {
                int fw = tex.width / frames;
                list = new Sprite[frames];
                for (int i = 0; i < frames; i++)
                {
                    list[i] = Sprite.Create(tex, new Rect(i * fw, 0, fw, tex.height), new Vector2(pivotX / fw, pivotY / tex.height),
                        PropPpu, 0, SpriteMeshType.FullRect);
                    list[i].name = name + "_" + i;
                }
            }
            _frames[key] = list;
            return list;
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

        // 옆 위험물 그림(칼은 Knife 를 눕혀 쓰므로 null). 피벗 = 보이는 그림의 가운데(판정 띠를 잰 기준, HitShapes).
        // 밀리는 상자·낙하물과 같은 그림이라 캐시 키를 따로 둔다(피벗이 다르다). 크기는 SideHazardScale 배로 그린다.
        public static Sprite SideHazard(SideHazardKind kind)
        {
            switch (kind)
            {
                case SideHazardKind.Snowball: return CenteredProp("map_winter", "snowball", 72f, 70f);
                case SideHazardKind.Crate: return CenteredProp("map_warehouse", "crate", 96f, 85f);
                case SideHazardKind.Macaron: return CenteredProp("map_dessert", "macaron", 72f, 52.5f);
                default: return null;
            }
        }

        // 옆 위험물 그림 배율(128 px/유닛 그림 기준). HitShapes 의 띠를 잴 때 쓴 배율과 같아야 한다.
        public static float SideHazardScale(SideHazardKind kind)
        {
            switch (kind)
            {
                case SideHazardKind.Snowball: return 0.5f;
                case SideHazardKind.Crate: return 0.5f;
                case SideHazardKind.Macaron: return 0.6f;
                default: return 1f;
            }
        }

        static Sprite CenteredProp(string folder, string name, float pivotX, float pivotY)
        {
            string key = folder + "/" + name + "#center";
            if (_props.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>(Dir + folder + "/" + name);
            if (tex != null)
            {
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(pivotX / tex.width, pivotY / tex.height),
                    PropPpu, 0, SpriteMeshType.FullRect);
                s.name = name + "_side";
            }
            _props[key] = s;
            return s;
        }

        // 맵별 밀리는 상자 그림(피벗 = 아래 가운데). 주방 버터 블록은 새 배치 그림(butter.png, 1.2 × 1.0, 여백 없음).
        public static Sprite Box(int stageId, bool cracked)
        {
            switch (stageId)
            {
                case 2: return Prop("map_winter", "snowball", 72, 8);
                case 3: return cracked ? Prop("map_warehouse", "crate_cracked", 96, 8) : Prop("map_warehouse", "crate", 96, 8);
                case 4: return Prop("map_dessert", "macaron", 72, 8);
                default: return Prop("map", "butter", 77, 0);
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

        static Dictionary<string, Clip> LoadSheet(CharacterKind kind, int color)
        {
            var meta = Meta(kind);
            string sheet = KindPrefix(kind) + "_" + ColorSheets[color];
            var tex = meta != null ? Resources.Load<Texture2D>(Dir + "characters/" + sheet) : null;
            if (tex == null) return null;
            int fw = meta.frame_size[0], fh = meta.frame_size[1];
            var pivot = new Vector2((float)meta.pivot_px_from_bottom_left[0] / fw, (float)meta.pivot_px_from_bottom_left[1] / fh);
            var set = new Dictionary<string, Clip>();
            foreach (var a in meta.animations)
            {
                var frames = new Sprite[a.frames];
                for (int i = 0; i < a.frames; i++)
                {
                    // 시트 맨 위 행이 row 0
                    var r = new Rect(i * fw, tex.height - (a.row + 1) * fh, fw, fh);
                    frames[i] = Sprite.Create(tex, r, pivot, meta.ppu, 0, SpriteMeshType.FullRect);
                    frames[i].name = sheet + "_" + a.name + "_" + i;
                }
                var scale = a.draw_scale != null && a.draw_scale.Length == 2 ? new Vector2(a.draw_scale[0], a.draw_scale[1]) : Vector2.one;
                set.Add(a.name, new Clip { Name = a.name, Frames = frames, Fps = a.fps, Loop = a.loop, DrawScale = scale });
            }
            return set;
        }
    }
}
