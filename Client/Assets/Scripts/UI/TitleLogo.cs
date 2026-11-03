using System.Collections.Generic;
using OnlineSeverGame.Client.View;
using UnityEngine;

namespace OnlineSeverGame.Client.UI
{
    // 타이틀 로고 "JELLY SURVIVAL". 그림 파일 없이 IMGUI 에서 단색 사각형만으로 그린다.
    // 좌표는 로고 단위(1 = ppu 픽셀, 아래로 +y). 글자는 캡 높이 13(큰 글자, 획 3)·12(팻말 글자, 획 2) 격자에 손으로 놓은
    // 사각형이고, 비스듬한 획(Y·V·R 다리)만 돌린 사각형이다. JELLY 다섯 글자는 플레이어 색 1~5번(딸기·블루베리·라임·레몬·포도),
    // SURVIVAL 은 게임 예고 색 빨간 팻말 위 흰 글자. 조각마다 0.7 단위 넓힌 어두운 사각형을 먼저 칠해 외곽선을 만든다.
    // 움직이는 판(접속 화면): JELLY 글자 다섯 개 위 자리 중 넷에 젤리가 서 있다. 빈자리 옆 젤리 머리 위로 게임과 같은 빨간 예고
    // (기둥 + 마름모)가 깜빡이면 그 젤리가 빈자리로 뛰어 옮기고, 칼이 떨어져 비운 글자에 꽂혔다가 사라진다. 3초마다 되풀이하며
    // 빈자리는 J → Y → J 로 오간다.
    public static class TitleLogo
    {
        enum Ink { J, E, L1, L2, Y, Paper, Warn, Handle, Rivet, Bolster, Steel, Spine }

        struct Part
        {
            public float X, Y, W, H, Angle; // 돌리기 전 왼쪽 위와 크기, 가운데 기준 회전(도)
            public Ink Ink;
        }

        static readonly Color[] Inks =
        {
            new Color32(226, 71, 90, 255),   // J: 딸기(플레이어 색 1, docs/work/art.md)
            new Color32(63, 111, 230, 255),  // E: 블루베리
            new Color32(77, 195, 90, 255),   // L: 라임
            new Color32(240, 201, 58, 255),  // L: 레몬
            new Color32(154, 93, 219, 255),  // Y: 포도
            new Color32(255, 247, 232, 255), // Paper: 팻말 글자
            new Color32(255, 64, 51, 255),   // Warn: 게임 예고 색(SwordView.WarnColor)
            new Color32(92, 54, 34, 255),    // Handle
            new Color32(232, 217, 176, 255), // Rivet
            new Color32(120, 124, 134, 255), // Bolster
            new Color32(222, 226, 234, 255), // Steel
            new Color32(160, 166, 178, 255), // Spine
        };
        static readonly Color OutlineColor = new Color32(26, 24, 30, 255);

        const float Outline = 0.7f;
        const float Big = 1.35f, BigGap = 1.6f;          // JELLY: 캡 13 × 1.35
        const float Small = 0.78f, SmallGap = 1.6f;      // SURVIVAL: 캡 12 × 0.78
        const float TagPad = 2.2f, LineGap = 2.6f;
        const float AnimTop = 15f;                       // 움직이는 판에서 로고 위로 비워 두는 높이(젤리 + 예고 마름모 자리)
        const float FallingKnifeScale = 0.55f;
        const float KnifeLength = 22f;
        const float Cycle = 3f;
        const float JellySize = 12f;                     // 젤리 프레임(256 px) 한 변
        static readonly int[] Jellies = { 5, 6, 7, 1 };  // 귤·소다·복숭아·블루베리(ArtSprites 시트 번호)

        static readonly List<Part> Logo = new List<Part>();
        static readonly List<Part> FallingKnife = new List<Part>();
        static readonly Vector2[] Slots = new Vector2[5]; // JELLY 글자마다 젤리가 설 자리(발밑, 로고 단위)
        static Rect _bounds; // 외곽선까지 포함한 로고 범위(로고 단위)

        // 조각을 (ox, oy) 에서 s 배로 놓는 붓
        sealed class Pen
        {
            readonly List<Part> _to;
            readonly float _ox, _oy, _s;
            readonly Ink _ink;

            public Pen(List<Part> to, float ox, float oy, float s, Ink ink) { _to = to; _ox = ox; _oy = oy; _s = s; _ink = ink; }

            public void R(float x, float y, float w, float h, float angle = 0f) => Put(x, y, w, h, angle, _ink);

            public void Put(float x, float y, float w, float h, float angle, Ink ink) =>
                _to.Add(new Part { X = _ox + x * _s, Y = _oy + y * _s, W = w * _s, H = h * _s, Angle = angle, Ink = ink });

            // (x1, y1) → (x2, y2) 를 잇는 두께 t 의 획
            public void Seg(float x1, float y1, float x2, float y2, float t)
            {
                float len = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                float cx = (x1 + x2) / 2f, cy = (y1 + y2) / 2f;
                R(cx - len / 2f, cy - t / 2f, len, t, Mathf.Atan2(y2 - y1, x2 - x1) * Mathf.Rad2Deg);
            }
        }

        delegate void Glyph(Pen p);

        static TitleLogo()
        {
            // JELLY: 글자 폭(격자)과 젤리가 설 자리(격자 x, 발밑 y). L 은 기둥 위, Y 는 두 팔 사이 홈에 선다.
            Glyph[] jelly = { J, E, L, L, Y };
            float[] widths = { 9, 8, 8, 8, 11 };
            float[] standX = { 5.5f, 4f, 1.5f, 1.5f, 5.5f };
            float[] standY = { 0, 0, 0, 0, 4.6f };
            float jellyW = (Sum(widths) + BigGap * 4) * Big;

            Glyph[] survival = { S, U, R, V, I, V, A, L2 };
            float[] sWidths = { 8, 8, 8, 10, 2, 10, 9, 7 };
            float tagW = (Sum(sWidths) + SmallGap * 7) * Small + 2 * TagPad;
            float total = Mathf.Max(jellyW, tagW);

            float x = (total - jellyW) / 2f;
            for (int i = 0; i < jelly.Length; i++)
            {
                jelly[i](new Pen(Logo, x, 0f, Big, (Ink)i));
                Slots[i] = new Vector2(x + standX[i] * Big, standY[i] * Big);
                x += (widths[i] + BigGap) * Big;
            }

            float tagY = 13 * Big + LineGap;
            x = (total - tagW) / 2f;
            Logo.Add(new Part { X = x, Y = tagY, W = tagW, H = 12 * Small + 2 * TagPad, Ink = Ink.Warn });
            x += TagPad;
            for (int i = 0; i < survival.Length; i++)
            {
                survival[i](new Pen(Logo, x, tagY + TagPad, Small, Ink.Paper));
                x += (sWidths[i] + SmallGap) * Small;
            }

            Knife(new Pen(FallingKnife, 0f, 0f, FallingKnifeScale, Ink.Steel), 0f, 0f, KnifeLength);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in Logo)
            {
                // 돌린 조각은 가운데에서 돌린 네 모서리로 잡는다
                float cos = Mathf.Abs(Mathf.Cos(p.Angle * Mathf.Deg2Rad)), sin = Mathf.Abs(Mathf.Sin(p.Angle * Mathf.Deg2Rad));
                float hw = (p.W / 2f + Outline) * cos + (p.H / 2f + Outline) * sin;
                float hh = (p.W / 2f + Outline) * sin + (p.H / 2f + Outline) * cos;
                float cx = p.X + p.W / 2f, cy = p.Y + p.H / 2f;
                minX = Mathf.Min(minX, cx - hw); maxX = Mathf.Max(maxX, cx + hw);
                minY = Mathf.Min(minY, cy - hh); maxY = Mathf.Max(maxY, cy + hh);
            }
            _bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        static float Sum(float[] a)
        {
            float s = 0f;
            foreach (var v in a) s += v;
            return s;
        }

        // ---- 큰 글자 JELLY(격자 높이 13, 획 3) ----

        static void J(Pen p) { p.R(2, 0, 7, 3); p.R(6, 0, 3, 13); p.R(0, 10, 9, 3); p.R(0, 7, 3, 6); }
        static void E(Pen p) { p.R(0, 0, 3, 13); p.R(0, 0, 8, 3); p.R(0, 5, 7, 3); p.R(0, 10, 8, 3); }
        static void L(Pen p) { p.R(0, 0, 3, 13); p.R(0, 10, 8, 3); }
        static void Y(Pen p) { p.Seg(1.5f, 1, 5.5f, 7, 3); p.Seg(9.5f, 1, 5.5f, 7, 3); p.R(4, 6, 3, 7); }

        // ---- 팻말 글자 SURVIVAL(격자 높이 12, 획 2) ----

        static void S(Pen p) { p.R(0, 0, 8, 2); p.R(0, 0, 2, 7); p.R(0, 5, 8, 2); p.R(6, 5, 2, 7); p.R(0, 10, 8, 2); }
        static void U(Pen p) { p.R(0, 0, 2, 12); p.R(6, 0, 2, 12); p.R(0, 10, 8, 2); }
        static void R(Pen p) { p.R(0, 0, 2, 12); p.R(0, 0, 8, 2); p.R(6, 0, 2, 7); p.R(0, 5, 8, 2); p.Seg(4.2f, 6.2f, 7.2f, 11.4f, 2); }
        static void V(Pen p) { p.Seg(1, 0.5f, 5, 11.5f, 2); p.Seg(9, 0.5f, 5, 11.5f, 2); }
        static void I(Pen p) { p.R(0, 0, 2, 12); }
        static void A(Pen p) { p.R(0, 1, 2, 11); p.R(7, 1, 2, 11); p.R(1, 0, 7, 2); p.R(0, 6, 9, 2); }
        static void L2(Pen p) { p.R(0, 0, 2, 12); p.R(0, 10, 7, 2); }

        // 칼끝이 아래인 식칼: 손잡이(리벳 둘), 받침쇠, 칼날(오른쪽에 어두운 등), 끝은 45도 돌린 정사각형
        static void Knife(Pen p, float cx, float top, float bottom)
        {
            p.Put(cx - 1.2f, top, 2.4f, 5.5f, 0f, Ink.Handle);
            p.Put(cx - 0.4f, top + 1.3f, 0.8f, 0.8f, 0f, Ink.Rivet);
            p.Put(cx - 0.4f, top + 3.4f, 0.8f, 0.8f, 0f, Ink.Rivet);
            p.Put(cx - 1.7f, top + 5.5f, 3.4f, 1.1f, 0f, Ink.Bolster);
            float bladeTop = top + 6.6f;
            p.Put(cx - 1.5f, bladeTop, 3f, bottom - 1.5f - bladeTop, 0f, Ink.Steel);
            float s = 3f / Mathf.Sqrt(2f);
            p.Put(cx - s / 2f, bottom - 1.5f - s / 2f, s, s, 45f, Ink.Steel);
            p.Put(cx + 0.55f, bladeTop, 0.95f, bottom - 2.2f - bladeTop, 0f, Ink.Spine);
        }

        // ---- 그리기 ----

        // 화면에서 차지하는 크기(픽셀). animated 면 위에 젤리와 예고 마름모 자리를 더한다.
        public static Vector2 Size(float ppu, bool animated) =>
            new Vector2(_bounds.width, _bounds.height + (animated ? AnimTop : 0f)) * ppu;

        public static void Draw(Vector2 topLeft, float ppu, bool animated)
        {
            if (Event.current.type != EventType.Repaint) return;
            var origin = new Vector2(topLeft.x - _bounds.xMin * ppu, topLeft.y + ((animated ? AnimTop : 0f) - _bounds.yMin) * ppu);
            var oldColor = GUI.color;

            Anim a = animated ? Animate(Time.time) : default;
            if (animated)
            {
                if (a.Warn > 0f) DrawWarning(origin, ppu, a);
                // 칼을 글자보다 먼저 그려 글자 외곽선이 칼을 가로지르게 한다(꽂힌 것처럼 보인다).
                if (a.KnifeAlpha > 0f)
                    DrawParts(FallingKnife, origin + new Vector2(a.KnifeX, a.KnifeTipY - KnifeLength * FallingKnifeScale) * ppu, ppu, a.KnifeAlpha);
            }
            DrawParts(Logo, origin, ppu, 1f);
            if (animated)
                for (int i = 0; i < Jellies.Length; i++) DrawJelly(origin, ppu, i, a.Jelly[i]);
            GUI.color = oldColor;
        }

        static void DrawParts(List<Part> parts, Vector2 origin, float ppu, float alpha)
        {
            var c = OutlineColor;
            c.a = alpha;
            foreach (var p in parts) Fill(origin, ppu, p, Outline, c);
            foreach (var p in parts)
            {
                c = Inks[(int)p.Ink];
                c.a = alpha;
                Fill(origin, ppu, p, 0f, c);
            }
        }

        static void Fill(Vector2 origin, float ppu, Part p, float grow, Color color)
        {
            GUI.color = color;
            var r = new Rect(origin.x + (p.X - grow) * ppu, origin.y + (p.Y - grow) * ppu, (p.W + 2 * grow) * ppu, (p.H + 2 * grow) * ppu);
            if (p.Angle == 0f)
            {
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                return;
            }
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(p.Angle, r.center);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.matrix = m;
        }

        // ---- 움직이는 판 ----

        struct JellyPose
        {
            public Vector2 Foot;
            public string Anim;
            public bool FaceLeft;
        }

        struct Anim
        {
            public float Warn;        // 0 = 없음, 0~1 = 예고 진행
            public Vector2 WarnAt;    // 예고가 가리키는 자리(발밑)
            public float KnifeX, KnifeTipY, KnifeAlpha;
            public JellyPose[] Jelly;
        }

        static readonly JellyPose[] Poses = new JellyPose[4];
        static readonly int[] Occupant = new int[5];

        // 한 번(3초): 0.1~1.1 예고, 0.7~1.2 빈자리로 점프, 1.1~1.3 칼 낙하(가속), 2.6~3.0 꽂힌 칼이 흐려진다.
        // 빈자리는 0 → 4 → 0 으로 한 칸씩 오가므로 8번(24초)마다 처음 배치로 돌아온다. 지금 배치는 처음부터 되짚어 구한다.
        static Anim Animate(float time)
        {
            int step = Mathf.FloorToInt(time / Cycle);
            float u = time - step * Cycle;
            step %= 8;

            for (int i = 0; i < 5; i++) Occupant[i] = i - 1; // 처음: 0번 자리(J)만 비어 있다
            int empty = 0;
            for (int k = 0; k < step; k++)
            {
                int target = NextTarget(k, empty);
                Occupant[empty] = Occupant[target];
                Occupant[target] = -1;
                empty = target;
            }
            int from = NextTarget(step, empty);
            int mover = Occupant[from];

            for (int s = 0; s < 5; s++)
            {
                int j = Occupant[s];
                if (j < 0) continue;
                // 서 있는 젤리는 로고 가운데를 본다
                Poses[j] = new JellyPose { Foot = Slots[s], Anim = "idle", FaceLeft = s > 2 };
            }

            var a = new Anim { WarnAt = Slots[from], KnifeX = Slots[from].x, Jelly = Poses };
            if (u >= 0.1f && u < 1.1f) a.Warn = (u - 0.1f) / 1.0f;

            float jump = Mathf.InverseLerp(0.7f, 1.2f, u);
            Vector2 p0 = Slots[from], p1 = Slots[empty];
            bool left = p1.x < p0.x;
            if (jump <= 0f) Poses[mover].FaceLeft = left; // 뛰기 전에 갈 쪽을 본다
            else if (jump < 1f)
            {
                var foot = Vector2.Lerp(p0, p1, jump);
                foot.y -= Mathf.Sin(jump * Mathf.PI) * 6f;
                Poses[mover] = new JellyPose { Foot = foot, Anim = jump < 0.5f ? "jump" : "fall", FaceLeft = left };
            }
            else Poses[mover] = new JellyPose { Foot = p1, Anim = "idle", FaceLeft = left };

            if (u >= 1.1f)
            {
                float fall = Mathf.InverseLerp(1.1f, 1.3f, u);
                float land = p0.y + 1.2f; // 칼끝이 글자에 1.2 박힌다
                a.KnifeTipY = Mathf.Lerp(-AnimTop - 6f, land, fall * fall);
                a.KnifeAlpha = 1f - Mathf.InverseLerp(2.6f, 3.0f, u);
            }
            return a;
        }

        // 빈자리가 0 → 4 → 0 으로 오가도록, k 번째 이동에서 빈자리 옆 어느 젤리가 움직일지 고른다.
        static int NextTarget(int k, int empty) => k % 8 < 4 ? empty + 1 : empty - 1;

        // 게임(SwordView)과 같은 예고: 낙하 지점 기둥 + 위쪽 마름모. 낙하가 가까울수록 빠르고 진하게 깜빡인다.
        static void DrawWarning(Vector2 origin, float ppu, Anim a)
        {
            float blink = Mathf.PingPong(Time.time * Mathf.Lerp(2f, 10f, a.Warn), 1f);
            var warn = Inks[(int)Ink.Warn];
            float top = -AnimTop + 1f;
            GUI.color = new Color(warn.r, warn.g, warn.b, Mathf.Lerp(0.10f, 0.28f, a.Warn) * (0.5f + 0.5f * blink));
            GUI.DrawTexture(new Rect(origin.x + (a.WarnAt.x - 2.6f) * ppu, origin.y + top * ppu, 5.2f * ppu, (a.WarnAt.y - Outline - top) * ppu), Texture2D.whiteTexture);
            var mark = new Part { X = a.WarnAt.x - 1.3f, Y = top + 0.6f, W = 2.6f, H = 2.6f, Angle = 45f };
            Fill(origin, ppu, mark, Outline, new Color(OutlineColor.r, OutlineColor.g, OutlineColor.b, 0.5f + 0.5f * blink));
            Fill(origin, ppu, mark, 0f, new Color(warn.r, warn.g, warn.b, 0.5f + 0.5f * blink));
        }

        static void DrawJelly(Vector2 origin, float ppu, int index, JellyPose pose)
        {
            // 젤리마다 시각을 어긋나게 해서 숨 쉬는 박자가 겹치지 않게 한다
            var sprite = ArtSprites.Character(Jellies[index], pose.Anim).At(Time.time + index * 0.37f);
            var tex = sprite.texture;
            var tr = sprite.textureRect;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            float size = JellySize * ppu;
            // 프레임 피벗(발밑)은 아래에서 8/256. 발을 글자 윗변 외곽선 위에 올린다.
            float footX = origin.x + pose.Foot.x * ppu;
            float footY = origin.y + (pose.Foot.y - Outline + 0.3f) * ppu;
            var r = new Rect(footX - size / 2f, footY - size + size * 8f / 256f, size, size);
            GUI.color = Color.white;
            var m = GUI.matrix;
            if (pose.FaceLeft) GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), r.center); // 시트는 오른쪽을 본다
            GUI.DrawTextureWithTexCoords(r, tex, uv);
            GUI.matrix = m;
        }
    }
}
