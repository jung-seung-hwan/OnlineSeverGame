using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 맵 그리기 (docs/work/map-layout.md 2장 배치). 배경·바닥은 기존 그림, 발판은 맵 비주얼 세션 그림이 오기 전까지
    // 기존 발판 그림을 발판 폭에 맞게 가로로 늘려 쓰거나(주방 선반, 겨울 창턱·들보, 창고 팔레트, 디저트 아이스크림 9-slice)
    // 단색 도형으로 그린다. 그림이 없으면 전부 단색 도형.
    // 발판마다 뿌리(Root)를 윗면 가운데에 두고, Refresh 가 매 프레임 그 시각(layout.DrawRoundTick)의 모양으로 옮긴다:
    // 녹는 아이스크림은 폭, 크레인 팔레트·케이크 리프트는 위치(틱 사이 보간), 눈 차양은 무너지기 전 흔들림·사라짐.
    // 맵 이벤트(불·바람·벨트·시럽)는 자식 MapEventView 가 그린다.
    public sealed class StageView : MonoBehaviour
    {
        static readonly Color Backdrop = new Color(0.08f, 0.09f, 0.11f);   // 배경 그림 밖(넓은 화면)
        static readonly Color OutsideWall = new Color(0f, 0f, 0f, 0.45f);   // 좌우 벽 바깥을 어둡게

        // 임시 도형 맵 색
        struct Palette
        {
            public Color Wall, Floor, FloorTop, Platform, PlatformTop, Ice, IceTop, Accent;
        }

        static readonly Palette Kitchen = new Palette
        {
            Wall = new Color(0.16f, 0.13f, 0.12f), Floor = new Color(0.55f, 0.40f, 0.28f), FloorTop = new Color(0.90f, 0.78f, 0.60f),
            Platform = new Color(0.50f, 0.36f, 0.24f), PlatformTop = new Color(0.95f, 0.85f, 0.68f),
            Ice = Color.clear, IceTop = Color.clear, Accent = new Color(0.26f, 0.22f, 0.20f),
        };

        static readonly Palette Winter = new Palette
        {
            Wall = new Color(0.10f, 0.12f, 0.19f), Floor = new Color(0.70f, 0.74f, 0.82f), FloorTop = new Color(0.95f, 0.97f, 1f),
            Platform = new Color(0.38f, 0.26f, 0.18f), PlatformTop = new Color(0.92f, 0.94f, 1f),
            Ice = new Color(0.45f, 0.70f, 0.88f), IceTop = new Color(0.80f, 0.95f, 1f), Accent = new Color(0.22f, 0.19f, 0.24f),
        };

        static readonly Palette Warehouse = new Palette
        {
            Wall = new Color(0.13f, 0.13f, 0.12f), Floor = new Color(0.36f, 0.36f, 0.35f), FloorTop = new Color(0.85f, 0.72f, 0.20f),
            Platform = new Color(0.40f, 0.45f, 0.50f), PlatformTop = new Color(0.78f, 0.84f, 0.90f),
            Ice = Color.clear, IceTop = Color.clear, Accent = new Color(0.25f, 0.27f, 0.30f),
        };

        static readonly Palette Dessert = new Palette
        {
            Wall = new Color(0.17f, 0.12f, 0.16f), Floor = new Color(0.62f, 0.55f, 0.52f), FloorTop = new Color(0.95f, 0.90f, 0.85f),
            Platform = new Color(0.86f, 0.70f, 0.48f), PlatformTop = new Color(1f, 0.93f, 0.80f),
            Ice = Color.clear, IceTop = Color.clear, Accent = new Color(0.30f, 0.22f, 0.27f),
        };

        // 디저트 가게 아이스크림 맛(발판 순서: 딸기, 민트, 바닐라, 초코)
        static readonly Color[] IceCreamColor =
        {
            new Color(0.98f, 0.62f, 0.72f), new Color(0.62f, 0.93f, 0.80f), new Color(1f, 0.94f, 0.78f), new Color(0.48f, 0.30f, 0.22f),
        };
        static readonly string[] IceCreamArt = { "icecream_strawberry", "icecream_mint", "icecream_vanilla", "icecream_chocolate" };

        // 발판 하나의 그림. Root = 윗면 가운데. 폭이 바뀌면 Root 의 가로 배율(9-slice 는 Sliced.size)로 맞춘다.
        sealed class PlatformVis
        {
            public int Index;
            public Transform Root;
            public float BaseWidth;          // 정의된 폭(유닛)
            public SpriteRenderer Sliced;    // 9-slice 아이스크림이면 그 렌더러
            public readonly List<SpriteRenderer> Renderers = new List<SpriteRenderer>();
            public readonly List<Color> Colors = new List<Color>();
        }

        // 발판을 따라 움직이는 부속(리프트 기둥, 팔레트 사슬)
        enum AttachKind { LiftColumn, ChainLeft, ChainRight }

        struct Attachment
        {
            public int Index;
            public AttachKind Kind;
            public SpriteRenderer Renderer;
        }

        readonly List<PlatformVis> _platforms = new List<PlatformVis>();
        readonly List<Attachment> _attached = new List<Attachment>();
        MapEventView _events;
        Palette _palette;

        public void Build(StageLayout layout)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            _platforms.Clear();
            _attached.Clear();

            Shapes.Rect(transform, "Backdrop", Backdrop, new Vector2(layout.Width + 40f, layout.Height + 40f), -101)
                .transform.localPosition = new Vector3(layout.Width / 2f, layout.Height / 2f, 0);

            Stage stage = layout.Source;
            switch (stage.Id)
            {
                case 2: BuildWinter(layout); break;
                case 3: BuildWarehouse(layout); break;
                case 4: BuildDessert(layout); break;
                default: BuildKitchen(layout); break;
            }
            for (int i = 0; i < stage.Platforms.Count; i++) _platforms.Add(BuildPlatform(stage, i));

            // 벽 바깥(x < 0, x > Width)은 그림이 이어지므로 어둡게 덮어 벽 위치를 보여 준다.
            for (int side = -1; side <= 1; side += 2)
            {
                var wall = Shapes.Rect(transform, side < 0 ? "WallL" : "WallR", OutsideWall, new Vector2(10f, layout.Height + 40f), -8);
                wall.transform.localPosition = new Vector3(layout.Width / 2f + side * (layout.Width / 2f + 5f), layout.Height / 2f, 0);
            }

            var ev = new GameObject("MapEvent");
            ev.transform.SetParent(transform, false);
            _events = ev.AddComponent<MapEventView>();
            _events.Build(layout);
            Refresh(layout);
        }

        // 그 시각의 발판 모양으로 옮기고(틱 사이는 보간) 이벤트를 그린다.
        public void Refresh(StageLayout layout)
        {
            Stage source = layout.Source;
            double tick = layout.DrawRoundTick;
            uint t0 = tick <= 0 ? 0u : (uint)tick;
            float frac = (float)(tick - t0);
            Stage a = source.AtRoundTick(t0);
            Stage b = source.ChangesDuringRound && tick > 0 ? source.AtRoundTick(t0 + 1) : a;

            foreach (var v in _platforms)
            {
                Platform pa = a.Platforms[v.Index], pb = b.Platforms[v.Index];
                bool moves = pa.Motion != null && pa.Moves;
                float cx = StageLayout.ToWorld(pa.CenterX), top = StageLayout.ToWorld(pa.Top);
                if (moves)
                {
                    cx = Mathf.Lerp(cx, StageLayout.ToWorld(pb.CenterX), frac);
                    top = Mathf.Lerp(top, StageLayout.ToWorld(pb.Top), frac);
                }
                float w = StageLayout.ToWorld(pa.Width);
                v.Root.gameObject.SetActive(!pa.Gone);
                if (pa.Gone) continue;
                float shake = pa.Crumbling ? Mathf.Sin(Time.time * 45f) * 0.05f : 0f;
                v.Root.localPosition = new Vector3(cx + shake, top, 0);
                if (v.Sliced != null) v.Sliced.size = new Vector2(w + 0.5f, v.Sliced.size.y);
                else v.Root.localScale = new Vector3((w + 0.5f) / (v.BaseWidth + 0.5f), 1f, 1f);
                // 무너지기 직전: 깜빡임(낙하 예고처럼 빨라짐은 아니고 일정하게)
                float alpha = pa.Crumbling ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 12f)) : 1f;
                for (int i = 0; i < v.Renderers.Count; i++)
                {
                    Color c = v.Colors[i];
                    c.a *= alpha;
                    v.Renderers[i].color = c;
                }
            }

            foreach (var att in _attached)
            {
                Platform pa = a.Platforms[att.Index], pb = b.Platforms[att.Index];
                float cx = Mathf.Lerp(StageLayout.ToWorld(pa.CenterX), StageLayout.ToWorld(pb.CenterX), frac);
                float top = Mathf.Lerp(StageLayout.ToWorld(pa.Top), StageLayout.ToWorld(pb.Top), frac);
                float w = StageLayout.ToWorld(pa.Width);
                switch (att.Kind)
                {
                    case AttachKind.LiftColumn:
                        float h = Mathf.Max(0.05f, top - 0.2f);
                        Shapes.Resize(att.Renderer, new Vector2(0.5f, h));
                        att.Renderer.transform.localPosition = new Vector3(cx, h / 2f, 0);
                        break;
                    default:
                        float x = att.Kind == AttachKind.ChainLeft ? cx - w / 2f + 0.5f : cx + w / 2f - 0.5f;
                        float len = 13.2f - top;
                        Shapes.Resize(att.Renderer, new Vector2(0.08f, len));
                        att.Renderer.transform.localPosition = new Vector3(x, top + len / 2f, 0);
                        break;
                }
            }

            if (_events != null) _events.Refresh(layout, a);
        }

        // ---------------------------------------------------------------- 맵별 배경·바닥·고정 부속

        void BuildKitchen(StageLayout layout)
        {
            _palette = Kitchen;
            if (Resources.Load<Texture2D>("Art/map/kitchen_bg") != null)
            {
                Place("kitchen_bg", ArtSprites.BackgroundRect, Vector2.zero, -100);
                Place("floor_board", ArtSprites.FloorRect, Vector2.zero, -10);
            }
            else
            {
                Wall(layout, _palette);
                Floor(layout, _palette);
            }
            // 양념 선반 탑 기둥(뒤)
            foreach (float x in new[] { 1.06f, 3.94f }) Box("RackPost", _palette.Accent, x, 3.6f, 0.12f, 7.2f, -11);
            // 가스레인지 상판과 화구 4개(불은 MapEventView)
            Box("Stovetop", new Color(0.18f, 0.18f, 0.20f), 16.75f, -0.05f, 14.5f, 0.12f, -9);
            foreach (var f in Stage.KitchenBurners)
            {
                float l = StageLayout.ToWorld(f.Left), r = StageLayout.ToWorld(f.Right);
                Box("Burner", new Color(0.35f, 0.33f, 0.33f), (l + r) / 2f, -0.01f, r - l, 0.05f, -8);
            }
        }

        void BuildWinter(StageLayout layout)
        {
            _palette = Winter;
            if (ArtSprites.MapPiece("map_winter", "winter_bg") != null)
            {
                Piece("map_winter", "winter_bg", -1.5f, -1.5f, -100);
                Piece("map_winter", "winter_floor", -1.5f, -1.5f, -10);
                Piece("map_winter", "winter_eave", -1.5f, 12.875f, 30); // 처마는 매달린 고드름보다 앞
            }
            else
            {
                Wall(layout, _palette);
                Floor(layout, _palette);
                float eaveBottom = StageLayout.ToWorld(layout.Source.KnifeSpawnY + HazardSpec.Icicle.Height);
                Box("Eave", _palette.Accent, layout.Width / 2f, eaveBottom + 0.75f, layout.Width + 3f, 1.5f, 30);
                Box("EaveSnow", _palette.FloorTop, layout.Width / 2f, eaveBottom + 0.05f, layout.Width + 3f, 0.1f, 31);
            }
            // 바닥 얼음 웅덩이(새 위치, 그림의 웅덩이 위치와 다를 수 있어 위에 덧그림)
            foreach (var span in layout.Source.FloorIce)
            {
                float l = StageLayout.ToWorld(span.Left), r = StageLayout.ToWorld(span.Right);
                Box("FloorIce", new Color(_palette.Ice.r, _palette.Ice.g, _palette.Ice.b, 0.85f), (l + r) / 2f, -0.04f, r - l, 0.12f, -9);
                Box("FloorIceTop", _palette.IceTop, (l + r) / 2f, 0.01f, r - l, 0.05f, -9);
            }
            // 돌 선반을 받치는 굴뚝 몸체(뒤, 세로)
            Box("Chimney", new Color(0.20f, 0.18f, 0.22f), 16.5f, 6.0f, 2.2f, 7.2f, -20);
        }

        void BuildWarehouse(StageLayout layout)
        {
            _palette = Warehouse;
            if (ArtSprites.MapPiece("map_warehouse", "warehouse_bg") != null)
            {
                Piece("map_warehouse", "warehouse_bg", -1.5f, -1.5f, -100);
                Piece("map_warehouse", "warehouse_floor", -1.5f, -1.5f, -10);
            }
            else
            {
                Wall(layout, _palette);
                Floor(layout, _palette);
                Box("CraneRail", _palette.Accent, layout.Width / 2f, 13.4f, layout.Width + 3f, 0.4f, -20);
            }
            // 컨베이어 벨트 바탕(무늬·흐름은 MapEventView)
            Box("Belt", new Color(0.12f, 0.12f, 0.13f), 12.5f, -0.07f, 13f, 0.16f, -9);
            // 3단 랙 기둥, 작업대 다리, 벽 선반·통로 받침
            foreach (float x in new[] { 19.06f, 23.44f }) Box("RackPost", _palette.Accent, x, 3.6f, 0.12f, 7.2f, -11);
            foreach (float x in new[] { 0.3f, 5.7f }) Box("BenchLeg", _palette.Accent, x, 0.6f, 0.12f, 1.2f, -11);
            Box("CatwalkPost", _palette.Accent, 5.9f, 4.8f, 0.1f, 2.4f, -11);
            // 크레인 팔레트 사슬(팔레트를 따라 움직임)
            Attach(Stage.WarehousePalletIndex, AttachKind.ChainLeft, new Color(0.30f, 0.30f, 0.32f), -12);
            Attach(Stage.WarehousePalletIndex, AttachKind.ChainRight, new Color(0.30f, 0.30f, 0.32f), -12);
        }

        void BuildDessert(StageLayout layout)
        {
            _palette = Dessert;
            if (ArtSprites.MapPiece("map_dessert", "dessert_bg") != null)
            {
                Piece("map_dessert", "dessert_bg", -1.5f, -1.5f, -100);
                Piece("map_dessert", "dessert_floor", -1.5f, -1.5f, -10);
            }
            else
            {
                Wall(layout, _palette);
                Floor(layout, _palette);
            }
            // 케이크 리프트 기둥(리프트를 따라 늘었다 줄었다)
            Attach(Stage.DessertLiftIndex, AttachKind.LiftColumn, new Color(0.78f, 0.72f, 0.76f), -10);
        }

        // ---------------------------------------------------------------- 발판

        PlatformVis BuildPlatform(Stage stage, int index)
        {
            Platform p = stage.Platforms[index];
            var root = new GameObject("Platform" + index).transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(StageLayout.ToWorld(p.CenterX), StageLayout.ToWorld(p.Top), 0);
            var v = new PlatformVis { Index = index, Root = root, BaseWidth = StageLayout.ToWorld(p.Width) };
            float w = v.BaseWidth;

            switch (stage.Id)
            {
                case 1:
                    // 레인지 후드(가장 높은 오른쪽)는 냄비 뚜껑 그림, 나머지는 선반 그림
                    bool hood = index == 6;
                    if (!StretchKitchen(v, hood ? "pot_lid" : "shelf", hood ? ArtSprites.LidRect : ArtSprites.ShelfRect, w))
                        ShapePlatform(v, w, 0.25f, _palette.Platform, _palette.PlatformTop);
                    break;
                case 2:
                    if (p.Blinks)
                    {
                        // 눈 차양: 나무 + 눈 덮개(그림 대기)
                        if (!StretchPiece(v, "map_winter", "winter_beam", w, new Color(0.85f, 0.88f, 0.95f)))
                            ShapePlatform(v, w, 0.3f, _palette.Platform, _palette.PlatformTop);
                        Child(v, "Snow", Color.white, 0f, 0.06f, w + 0.3f, 0.14f, -7);
                    }
                    else if (!StretchPiece(v, "map_winter", p.Ice ? "winter_ice_sill" : "winter_beam", w, Color.white))
                        ShapePlatform(v, w, 0.3f, p.Ice ? _palette.Ice : _palette.Platform, p.Ice ? _palette.IceTop : _palette.PlatformTop);
                    break;
                case 3:
                    if (!StretchPiece(v, "map_warehouse", "warehouse_pallet", w, Color.white))
                        ShapePlatform(v, w, 0.2f, _palette.Platform, _palette.PlatformTop);
                    break;
                default:
                    if (p.MeltPerTick > 0 && index < IceCreamArt.Length)
                    {
                        Sprite art = ArtSprites.IceCream(IceCreamArt[index]);
                        if (art != null)
                        {
                            var go = new GameObject(IceCreamArt[index]);
                            go.transform.SetParent(root, false);
                            var sr = go.AddComponent<SpriteRenderer>();
                            sr.sprite = art;
                            sr.drawMode = SpriteDrawMode.Sliced;
                            sr.size = new Vector2(w + 0.5f, art.bounds.size.y);
                            sr.sortingOrder = -9;
                            v.Sliced = sr;
                            v.Renderers.Add(sr);
                            v.Colors.Add(sr.color);
                        }
                        else
                        {
                            Color flavor = IceCreamColor[index];
                            ShapePlatform(v, w, 0.9f, flavor, Color.Lerp(flavor, Color.white, 0.6f));
                        }
                    }
                    else if (index == Stage.DessertLiftIndex)
                        ShapePlatform(v, w, 0.25f, new Color(0.95f, 0.92f, 0.95f), new Color(1f, 0.75f, 0.85f));
                    else if (p.Top >= 9000)
                    {
                        // 체리 케이크 윗면
                        ShapePlatform(v, w, 0.7f, new Color(0.98f, 0.92f, 0.86f), Color.white);
                        Child(v, "Cherry", new Color(0.85f, 0.10f, 0.18f), -w / 4f, -0.2f, 0.32f, 0.3f, -8);
                    }
                    else ShapePlatform(v, w, 0.3f, _palette.Platform, _palette.PlatformTop); // 웨하스
                    break;
            }
            return v;
        }

        // 맵 조각(왼쪽 아래 피벗, 윗면 = 그림 위에서 0.125)을 발판 폭 + 0.5 로 가로로 늘린다.
        bool StretchPiece(PlatformVis v, string folder, string name, float w, Color tint)
        {
            Sprite s = ArtSprites.MapPiece(folder, name);
            if (s == null) return false;
            var go = new GameObject(name);
            go.transform.SetParent(v.Root, false);
            float nw = s.bounds.size.x, nh = s.bounds.size.y;
            go.transform.localPosition = new Vector3(-(w + 0.5f) / 2f, 0.125f - nh, 0);
            go.transform.localScale = new Vector3((w + 0.5f) / nw, 1f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.color = tint;
            sr.sortingOrder = -9;
            v.Renderers.Add(sr);
            v.Colors.Add(tint);
            return true;
        }

        // 주방 그림(피벗 = 윗면 가운데, rect 폭 6.5)을 발판 폭 + 0.5 로 늘린다.
        bool StretchKitchen(PlatformVis v, string name, Rect rect, float w)
        {
            if (Resources.Load<Texture2D>("Art/map/" + name) == null) return false;
            Sprite s = ArtSprites.Map(name, rect);
            var go = new GameObject(name);
            go.transform.SetParent(v.Root, false);
            go.transform.localScale = new Vector3((w + 0.5f) / rect.width, 1f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = -9;
            v.Renderers.Add(sr);
            v.Colors.Add(Color.white);
            return true;
        }

        void ShapePlatform(PlatformVis v, float w, float thickness, Color body, Color top)
        {
            Child(v, "Body", body, 0f, -thickness / 2f, w, thickness, -9);
            Child(v, "Top", top, 0f, -0.04f, w, 0.08f, -8);
        }

        void Child(PlatformVis v, string name, Color color, float x, float y, float w, float h, int order)
        {
            var sr = Shapes.Rect(v.Root, name, color, new Vector2(w, h), order);
            sr.transform.localPosition = new Vector3(x, y, 0);
            v.Renderers.Add(sr);
            v.Colors.Add(color);
        }

        void Attach(int index, AttachKind kind, Color color, int order)
        {
            var sr = Shapes.Rect(transform, kind.ToString(), color, Vector2.one, order);
            _attached.Add(new Attachment { Index = index, Kind = kind, Renderer = sr });
        }

        void Wall(StageLayout layout, Palette c)
        {
            Box("Wall", c.Wall, layout.Width / 2f, layout.Height / 2f, layout.Width + 3f, layout.Height + 3f, -100);
        }

        void Floor(StageLayout layout, Palette c)
        {
            Box("Floor", c.Floor, layout.Width / 2f, -0.75f, layout.Width + 3f, 1.5f, -10);
            Box("FloorTop", c.FloorTop, layout.Width / 2f, -0.04f, layout.Width + 3f, 0.08f, -10);
        }

        // 새 맵 정지 조각: 그림 왼쪽 아래를 world_rect 의 (x0, y0)에 놓는다.
        SpriteRenderer Piece(string folder, string name, float x0, float y0, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x0, y0, 0);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ArtSprites.MapPiece(folder, name);
            sr.sortingOrder = order;
            return sr;
        }

        SpriteRenderer Box(string name, Color color, float cx, float cy, float w, float h, int order)
        {
            var sr = Shapes.Rect(transform, name, color, new Vector2(w, h), order);
            sr.transform.localPosition = new Vector3(cx, cy, 0);
            return sr;
        }

        // rect 는 그림이 덮는 사각형(at 기준). at 에 피벗을 놓는다.
        GameObject Place(string name, Rect rect, Vector2 at, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(at.x, at.y, 0);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ArtSprites.Map(name, rect);
            sr.sortingOrder = order;
            return go;
        }
    }
}
