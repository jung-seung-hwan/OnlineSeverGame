using System.Collections.Generic;
using OnlineSeverGame.Client.View;
using UnityEngine;

namespace OnlineSeverGame.Client.UI
{
    // 타이틀 로고 "칼비 주의보"(칼 + 비, 호우주의보 말장난). 그림 파일 없이 IMGUI 에서 단색 사각형만으로 그린다.
    // 좌표는 로고 단위(1 = ppu 픽셀, 아래로 +y). 글자는 획 두께 2 격자에 손으로 놓은 사각형(ㅈ·ㅇ 은 돌린 사각형)이고,
    // "비"의 ㅣ 는 꽂힌 식칼이다. 조각마다 0.7 단위 넓힌 어두운 사각형을 먼저 칠해 외곽선을 만든다. 그라데이션·광택 없음.
    // 움직이는 판(접속 화면): 빨간 "주의보" 팻말 위 젤리 머리 위로 게임과 같은 빨간 예고(기둥 + 마름모)가 깜빡이고,
    // 젤리가 옆으로 비키면 칼이 떨어져 팻말에 꽂혔다가 사라진다. 4초마다 좌우를 바꿔 되풀이한다.
    public static class TitleLogo
    {
        enum Ink { Cream, Paper, Warn, Handle, Rivet, Bolster, Steel, Spine }

        struct Part
        {
            public float X, Y, W, H, Angle; // 돌리기 전 왼쪽 위와 크기, 가운데 기준 회전(도)
            public Ink Ink;
        }

        static readonly Color[] Inks =
        {
            new Color32(243, 230, 200, 255), // Cream: 큰 글자
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
        const float SmallScale = 0.56f;            // 팻말 글자 = 큰 글자 격자의 0.56배
        const float TagX = 37.5f, TagY = 7.5f;     // 팻말 왼쪽 위
        const float TagPad = 2.2f, TagGap = 1.6f;
        const float AnimTop = 16f;                 // 움직이는 판에서 로고 위로 비워 두는 높이(예고 마름모 자리)
        const float FallingKnifeScale = 0.6f;
        const float HalfCycle = 4f;
        const int JellyColor = 5;                  // 귤(요리사 모자)
        const float JellySize = 12f;               // 젤리 프레임(256 px) 한 변

        static readonly List<Part> Logo = new List<Part>();
        static readonly List<Part> FallingKnife = new List<Part>();
        static readonly float TagW = 3 * 14 * SmallScale + 2 * TagGap + 2 * TagPad;
        static readonly float TagH = 15 * SmallScale + 2 * TagPad;
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

        static TitleLogo()
        {
            Kal(new Pen(Logo, 0f, 0f, 1f, Ink.Cream));
            Bi(new Pen(Logo, 18f, 0.8f, 1f, Ink.Cream));
            Logo.Add(new Part { X = TagX, Y = TagY, W = TagW, H = TagH, Ink = Ink.Warn });
            float step = 14 * SmallScale + TagGap;
            Ju(new Pen(Logo, TagX + TagPad, TagY + TagPad, SmallScale, Ink.Paper));
            Ui(new Pen(Logo, TagX + TagPad + step, TagY + TagPad, SmallScale, Ink.Paper));
            Bo(new Pen(Logo, TagX + TagPad + 2 * step, TagY + TagPad, SmallScale, Ink.Paper));
            Knife(new Pen(FallingKnife, 0f, 0f, FallingKnifeScale, Ink.Steel), 0f, 0f, 22f);

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

        // ---- 큰 글자(격자 16 x 20, 획 2) ----

        static void Kal(Pen p)
        {
            p.R(0, 0, 8, 2); p.R(6, 0, 2, 9.5f); p.R(0, 4, 6.5f, 2);                       // ㅋ
            p.R(11, 0, 2, 10.5f); p.R(13, 4, 3, 2);                                       // ㅏ
            p.R(1, 12, 14, 2); p.R(13, 12, 2, 5); p.R(1, 15, 14, 2);                      // ㄹ
            p.R(1, 15, 2, 5); p.R(1, 18, 14, 2);
        }

        static void Bi(Pen p)
        {
            p.R(0, 2, 2, 16); p.R(7, 2, 2, 16); p.R(0, 9, 9, 2); p.R(0, 16, 9, 2);         // ㅂ
            Knife(p, 12.5f, -1f, 21f);                                                    // ㅣ = 꽂힌 칼
        }

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

        // ---- 팻말 글자(격자 14 x 15, 획 2) ----

        static void Ju(Pen p)
        {
            p.R(1, 0, 12, 2); p.Seg(7, 1.6f, 2, 7.6f, 2); p.Seg(7, 1.6f, 12, 7.6f, 2);     // ㅈ
            p.R(0, 9.5f, 14, 2); p.R(6, 11.5f, 2, 3.5f);                                  // ㅜ
        }

        static void Ui(Pen p)
        {
            // ㅇ: 팔각형 여덟 변. 이음매가 비지 않게 변마다 양 끝을 조금 늘린다.
            const float cx = 4.5f, cy = 4.6f, r = 3.6f, extend = 0.42f;
            for (int i = 0; i < 8; i++)
            {
                float a1 = (22.5f + 45f * i) * Mathf.Deg2Rad, a2 = (22.5f + 45f * (i + 1)) * Mathf.Deg2Rad;
                float x1 = cx + r * Mathf.Cos(a1), y1 = cy + r * Mathf.Sin(a1);
                float x2 = cx + r * Mathf.Cos(a2), y2 = cy + r * Mathf.Sin(a2);
                float len = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                float ex = (x2 - x1) / len * extend, ey = (y2 - y1) / len * extend;
                p.Seg(x1 - ex, y1 - ey, x2 + ex, y2 + ey, 2);
            }
            p.R(0, 11.5f, 10, 2);                                                         // ㅡ
            p.R(12, 0, 2, 15);                                                            // ㅣ
        }

        static void Bo(Pen p)
        {
            p.R(2, 0, 2, 9); p.R(10, 0, 2, 9); p.R(2, 3.5f, 10, 2); p.R(2, 7, 10, 2);      // ㅂ
            p.R(6, 9, 2, 3.5f); p.R(0, 12.5f, 14, 2);                                     // ㅗ
        }

        // ---- 그리기 ----

        // 화면에서 차지하는 크기(픽셀). animated 면 위에 예고 마름모 자리를 더한다.
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
                // 칼을 팻말보다 먼저 그려 팻말 외곽선이 칼을 가로지르게 한다(꽂힌 것처럼 보인다).
                if (a.KnifeAlpha > 0f)
                    DrawParts(FallingKnife, origin + new Vector2(a.KnifeX, a.KnifeTipY - 22f * FallingKnifeScale) * ppu, ppu, a.KnifeAlpha);
            }
            DrawParts(Logo, origin, ppu, 1f);
            if (animated) DrawJelly(origin, ppu, a);
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

        struct Anim
        {
            public float Warn;        // 0 = 없음, 0~1 = 예고 진행
            public float WarnX;
            public float KnifeX, KnifeTipY, KnifeAlpha;
            public float JellyX;
            public bool Running, FaceLeft;
        }

        // 반 주기(4초): 0.3~1.5 예고(젤리는 0.9 에 비키기 시작), 1.5~1.7 낙하, 3.4~3.8 꽂힌 칼이 흐려진다.
        static Anim Animate(float time)
        {
            float spotA = TagX + 6f, spotB = TagX + TagW - 6f;
            float t = Mathf.Repeat(time, 2f * HalfCycle);
            bool second = t >= HalfCycle;
            float u = t - (second ? HalfCycle : 0f);
            float from = second ? spotB : spotA, to = second ? spotA : spotB;

            // 젤리는 비킬 쪽(팻말 안쪽)을 보고 서 있다가 그대로 달려간다.
            var a = new Anim { WarnX = from, KnifeX = from, FaceLeft = to < from };
            if (u >= 0.3f && u < 1.5f) a.Warn = (u - 0.3f) / 1.2f;

            float run = Mathf.InverseLerp(0.9f, 1.4f, u);
            a.JellyX = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, run));
            a.Running = run > 0f && run < 1f;

            float landY = TagY + 1.2f; // 칼끝이 팻말에 1.2 박힌다
            if (u >= 1.5f && u < 3.8f)
            {
                float fall = Mathf.InverseLerp(1.5f, 1.7f, u);
                a.KnifeTipY = Mathf.Lerp(-AnimTop - 4f, landY, fall * fall);
                a.KnifeAlpha = 1f - Mathf.InverseLerp(3.4f, 3.8f, u);
            }
            return a;
        }

        // 게임(SwordView)과 같은 예고: 낙하 지점 기둥 + 위쪽 마름모. 낙하가 가까울수록 빠르고 진하게 깜빡인다.
        static void DrawWarning(Vector2 origin, float ppu, Anim a)
        {
            float blink = Mathf.PingPong(Time.time * Mathf.Lerp(2f, 10f, a.Warn), 1f);
            var warn = Inks[(int)Ink.Warn];
            float top = -AnimTop + 3f;
            GUI.color = new Color(warn.r, warn.g, warn.b, Mathf.Lerp(0.10f, 0.28f, a.Warn) * (0.5f + 0.5f * blink));
            GUI.DrawTexture(new Rect(origin.x + (a.WarnX - 2.2f) * ppu, origin.y + top * ppu, 4.4f * ppu, (TagY - Outline - top) * ppu), Texture2D.whiteTexture);
            var mark = new Part { X = a.WarnX - 1.4f, Y = top + 1f, W = 2.8f, H = 2.8f, Angle = 45f };
            Fill(origin, ppu, mark, Outline, new Color(OutlineColor.r, OutlineColor.g, OutlineColor.b, 0.5f + 0.5f * blink));
            Fill(origin, ppu, mark, 0f, new Color(warn.r, warn.g, warn.b, 0.5f + 0.5f * blink));
        }

        static void DrawJelly(Vector2 origin, float ppu, Anim a)
        {
            var clip = ArtSprites.Character(JellyColor, a.Running ? "run" : "idle");
            var sprite = clip.At(Time.time);
            var tex = sprite.texture;
            var tr = sprite.textureRect;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            float size = JellySize * ppu;
            // 프레임 피벗(발밑)은 아래에서 8/256. 발을 팻말 외곽선 위에 올린다.
            float footY = origin.y + (TagY - Outline + 0.3f) * ppu;
            var r = new Rect(origin.x + a.JellyX * ppu - size / 2f, footY - size + size * 8f / 256f, size, size);
            GUI.color = Color.white;
            var m = GUI.matrix;
            if (a.FaceLeft) GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), r.center); // 시트는 오른쪽을 본다
            GUI.DrawTextureWithTexCoords(r, tex, uv);
            GUI.matrix = m;
        }
    }
}
