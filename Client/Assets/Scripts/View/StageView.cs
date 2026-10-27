using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 맵 그리기. 주방(id 1)은 ArtSprites 그림: 배경, 도마 바닥, 선반 발판, 가장 높은 발판은 매단 냄비 뚜껑.
    // 그림은 Stage.Default 좌표로 렌더했고, 발판 그림은 각 발판 윗면 가운데에 놓는다.
    // 겨울 처마·창고·디저트 가게는 신규 맵 아트 세션 그림(Resources/Art/map_<이름>/, map.json world_rect 그대로).
    // 그림이 없으면 단색 임시 도형(docs/work/maps.md 배치 그대로)으로 그린다.
    // 녹는 아이스크림 발판은 Refresh 가 매 프레임 지금 폭으로 줄인다(그림은 9-slice 로 가운데만 줄어듦).
    public sealed class StageView : MonoBehaviour
    {
        static readonly Color Backdrop = new Color(0.08f, 0.09f, 0.11f);   // 배경 그림 밖(넓은 화면)
        static readonly Color OutsideWall = new Color(0f, 0f, 0f, 0.45f);   // 좌우 벽 바깥을 어둡게

        // 임시 도형 맵 색
        struct Palette
        {
            public Color Wall, Floor, FloorTop, Platform, PlatformTop, Ice, IceTop, Accent;
        }

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
            Platform = Color.white, PlatformTop = Color.white,
            Ice = Color.clear, IceTop = Color.clear, Accent = new Color(0.30f, 0.22f, 0.27f),
        };

        // 디저트 가게 아이스크림 맛(발판 순서: 딸기, 민트, 바닐라, 초코)
        static readonly Color[] IceCream =
        {
            new Color(0.98f, 0.62f, 0.72f), new Color(0.62f, 0.93f, 0.80f), new Color(1f, 0.94f, 0.78f), new Color(0.48f, 0.30f, 0.22f),
        };

        struct Melting
        {
            public int Index;
            public SpriteRenderer Body;
            public SpriteRenderer Top;
            public float Height;
            public SpriteRenderer Sliced; // 그림일 때: 9-slice, 피벗 = 윗면 가운데
        }

        readonly List<Melting> _melting = new List<Melting>();

        public void Build(StageLayout layout)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            _melting.Clear();

            Shapes.Rect(transform, "Backdrop", Backdrop, new Vector2(layout.Width + 40f, layout.Height + 40f), -101)
                .transform.localPosition = new Vector3(layout.Width / 2f, layout.Height / 2f, 0);

            switch (layout.Source.Id)
            {
                case 2: BuildWinter(layout); break;
                case 3: BuildWarehouse(layout); break;
                case 4: BuildDessert(layout); break;
                default: BuildKitchen(layout); break;
            }

            // 벽 바깥(x < 0, x > Width)은 그림이 이어지므로 어둡게 덮어 벽 위치를 보여 준다.
            for (int side = -1; side <= 1; side += 2)
            {
                var wall = Shapes.Rect(transform, side < 0 ? "WallL" : "WallR", OutsideWall, new Vector2(10f, layout.Height + 40f), -8);
                wall.transform.localPosition = new Vector3(layout.Width / 2f + side * (layout.Width / 2f + 5f), layout.Height / 2f, 0);
            }
        }

        // 녹는 발판을 지금 라운드 틱의 폭으로 맞춘다.
        public void Refresh(StageLayout layout)
        {
            if (_melting.Count == 0) return;
            Stage now = layout.Current;
            foreach (var m in _melting)
            {
                Platform p = now.Platforms[m.Index];
                float w = StageLayout.ToWorld(p.Width), cx = StageLayout.ToWorld(p.CenterX), top = StageLayout.ToWorld(p.Top);
                if (m.Sliced != null)
                {
                    // 그림 폭 = 발판 폭 + 양쪽 여백 0.25
                    m.Sliced.size = new Vector2(w + 0.5f, m.Sliced.size.y);
                    continue;
                }
                Shapes.Resize(m.Body, new Vector2(w, m.Height));
                m.Body.transform.localPosition = new Vector3(cx, top - m.Height / 2f, 0);
                Shapes.Resize(m.Top, new Vector2(w, 0.12f));
                m.Top.transform.localPosition = new Vector3(cx, top - 0.06f, 0);
            }
        }

        void BuildKitchen(StageLayout layout)
        {
            Place("kitchen_bg", ArtSprites.BackgroundRect, Vector2.zero, -100);
            Place("floor_board", ArtSprites.FloorRect, Vector2.zero, -10);

            float highest = float.MinValue;
            for (int i = 1; i < layout.Solids.Count; i++) highest = Mathf.Max(highest, layout.Solids[i].TopY);
            for (int i = 1; i < layout.Solids.Count; i++)
            {
                var s = layout.Solids[i];
                bool lid = s.TopY >= highest && layout.Solids.Count > 2;
                Place(lid ? "pot_lid" : "shelf", lid ? ArtSprites.LidRect : ArtSprites.ShelfRect,
                    new Vector2(s.CenterX, s.TopY), -9).name = "Platform" + i;
            }
        }

        void BuildWinter(StageLayout layout)
        {
            if (ArtSprites.MapPiece("map_winter", "winter_bg") != null)
            {
                Piece("map_winter", "winter_bg", -1.5f, -1.5f, -100);
                Piece("map_winter", "winter_floor", -1.5f, -1.5f, -10);
                Piece("map_winter", "winter_ice_sill", 1.25f, 1.525f, -9);
                Piece("map_winter", "winter_ice_sill", 17.25f, 1.525f, -9);
                Piece("map_winter", "winter_bench", 9.75f, 0f, -9);
                Piece("map_winter", "winter_beam", 4.75f, 4.3f, -9);
                Piece("map_winter", "winter_beam", 14.25f, 4.3f, -9);
                Piece("map_winter", "winter_eave", -1.5f, 12.875f, 30); // 처마는 매달린 고드름보다 앞
                return;
            }
            Palette c = Winter;
            Wall(layout, c);
            Floor(layout, c);
            foreach (var span in layout.Source.FloorIce)
            {
                float l = StageLayout.ToWorld(span.Left), r = StageLayout.ToWorld(span.Right);
                Box("FloorIce", c.Ice, (l + r) / 2f, -0.04f, r - l, 0.12f, -9);
                Box("FloorIceTop", c.IceTop, (l + r) / 2f, 0.01f, r - l, 0.05f, -9);
            }
            Platforms(layout, c, 0.3f);
            // 처마: 아랫면 y = 13.0 (고드름이 매달리는 곳). 위험물보다 앞에 그린다.
            float eaveBottom = StageLayout.ToWorld(layout.Source.KnifeSpawnY + HazardSpec.Icicle.Height);
            Box("Eave", c.Accent, layout.Width / 2f, eaveBottom + 0.75f, layout.Width + 3f, 1.5f, 30);
            Box("EaveSnow", c.FloorTop, layout.Width / 2f, eaveBottom + 0.05f, layout.Width + 3f, 0.1f, 31);
        }

        void BuildWarehouse(StageLayout layout)
        {
            if (ArtSprites.MapPiece("map_warehouse", "warehouse_bg") != null)
            {
                Piece("map_warehouse", "warehouse_bg", -1.5f, -1.5f, -100); // 크레인 레일·팔레트 사슬 포함
                Piece("map_warehouse", "warehouse_floor", -1.5f, -1.5f, -10);
                Piece("map_warehouse", "warehouse_rack", 0.75f, 0f, -9);
                Piece("map_warehouse", "warehouse_rack", 18.75f, 0f, -9);
                Piece("map_warehouse", "warehouse_pallet", 9.25f, 3.1f, -9);
                return;
            }
            Palette c = Warehouse;
            Wall(layout, c);
            Floor(layout, c);
            // 랙 기둥(같은 x 범위의 선반 두 칸을 잇는다)과 팔레트 사슬은 뒤에 그린다.
            foreach (var p in layout.Source.Platforms)
            {
                float l = StageLayout.ToWorld(p.Left), r = StageLayout.ToWorld(p.Right), top = StageLayout.ToWorld(p.Top);
                if (p.Top == 3600)
                {
                    for (int side = 0; side < 2; side++)
                        Box("Chain", c.Accent, side == 0 ? l + 0.5f : r - 0.5f, (top + 13.2f) / 2f, 0.08f, 13.2f - top, -20);
                }
                else
                {
                    Box("Post", c.Accent, l + 0.06f, top / 2f, 0.12f, top, -20);
                    Box("Post", c.Accent, r - 0.06f, top / 2f, 0.12f, top, -20);
                }
            }
            Box("CraneRail", c.Accent, layout.Width / 2f, 13.4f, layout.Width + 3f, 0.4f, -20);
            Platforms(layout, c, 0.2f);
        }

        static readonly string[] IceCreamArt = { "icecream_strawberry", "icecream_mint", "icecream_vanilla", "icecream_chocolate" };

        void BuildDessert(StageLayout layout)
        {
            var platforms = layout.Source.Platforms;
            if (ArtSprites.MapPiece("map_dessert", "dessert_bg") != null)
            {
                Piece("map_dessert", "dessert_bg", -1.5f, -1.5f, -100);
                Piece("map_dessert", "dessert_floor", -1.5f, -1.5f, -10);
                for (int i = 0; i < platforms.Count && i < IceCreamArt.Length; i++)
                {
                    Platform p = platforms[i];
                    var go = new GameObject(IceCreamArt[i]);
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(StageLayout.ToWorld(p.CenterX), StageLayout.ToWorld(p.Top), 0);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = ArtSprites.IceCream(IceCreamArt[i]);
                    sr.drawMode = SpriteDrawMode.Sliced;
                    sr.size = sr.sprite.bounds.size;
                    sr.sortingOrder = -9;
                    if (p.MeltPerTick > 0) _melting.Add(new Melting { Index = i, Sliced = sr });
                }
                return;
            }
            Palette c = Dessert;
            Wall(layout, c);
            Floor(layout, c);
            for (int i = 0; i < platforms.Count; i++)
            {
                Platform p = platforms[i];
                Color flavor = IceCream[i % IceCream.Length];
                float h = 0.9f;
                var body = Box("IceCream" + i, flavor, StageLayout.ToWorld(p.CenterX), StageLayout.ToWorld(p.Top) - h / 2f,
                    StageLayout.ToWorld(p.Width), h, -9);
                var top = Box("IceCreamTop" + i, Color.Lerp(flavor, Color.white, 0.6f), StageLayout.ToWorld(p.CenterX),
                    StageLayout.ToWorld(p.Top) - 0.06f, StageLayout.ToWorld(p.Width), 0.12f, -8);
                if (p.MeltPerTick > 0) _melting.Add(new Melting { Index = i, Body = body, Top = top, Height = h });
            }
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

        void Platforms(StageLayout layout, Palette c, float thickness)
        {
            foreach (var p in layout.Source.Platforms)
            {
                float cx = StageLayout.ToWorld(p.CenterX), w = StageLayout.ToWorld(p.Width), top = StageLayout.ToWorld(p.Top);
                Box("Platform", p.Ice ? c.Ice : c.Platform, cx, top - thickness / 2f, w, thickness, -9);
                Box("PlatformTop", p.Ice ? c.IceTop : c.PlatformTop, cx, top - 0.04f, w, 0.08f, -8);
            }
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
