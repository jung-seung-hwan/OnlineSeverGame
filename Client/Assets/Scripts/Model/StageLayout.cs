using System.Collections.Generic;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.Model
{
    // Shared Stage 를 Unity 월드 단위로 옮긴 그리기용 지형. x 는 0(왼쪽 벽) ~ Width, 바닥 윗면 y = 0.
    public sealed class StageLayout
    {
        public struct Solid
        {
            public float CenterX;
            public float TopY;
            public float Width;
            public float Height;
        }

        public Stage Source;
        public float Width;
        public float Height;       // 카메라에 보일 높이
        public float SwordSpawnY;
        public Vector2 PlayerSize;
        public Vector2 SwordSize;
        public readonly List<Solid> Solids = new List<Solid>(); // [0] = 바닥

        public static float ToWorld(int milli) => milli / (float)GameConstants.UnitsPerWorldUnit;

        public static StageLayout FromShared(Stage stage)
        {
            var s = new StageLayout
            {
                Source = stage,
                Width = ToWorld(stage.Width),
                Height = ToWorld(stage.ViewHeight),
                SwordSpawnY = ToWorld(stage.KnifeSpawnY),
                PlayerSize = new Vector2(ToWorld(GameConstants.PlayerWidth), ToWorld(GameConstants.PlayerHeight)),
                SwordSize = new Vector2(ToWorld(GameConstants.KnifeWidth), ToWorld(GameConstants.KnifeHeight)),
            };
            s.Solids.Add(new Solid { CenterX = s.Width / 2f, TopY = 0f, Width = s.Width, Height = 1f });
            foreach (var p in stage.Platforms)
            {
                s.Solids.Add(new Solid
                {
                    CenterX = ToWorld(p.Left + p.Right) / 2f,
                    TopY = ToWorld(p.Top),
                    Width = ToWorld(p.Right - p.Left),
                    Height = 0.35f,
                });
            }
            return s;
        }

        // 공중에서 죽은 캐릭터의 웅덩이를 그릴 높이: 발 아래 첫 바닥·발판 윗면
        public float SurfaceBelow(float x, float y)
        {
            int mx = Mathf.RoundToInt(x * GameConstants.UnitsPerWorldUnit);
            int my = Mathf.RoundToInt(y * GameConstants.UnitsPerWorldUnit);
            Source.TryFindLanding(mx - GameConstants.PlayerHalfWidth, mx + GameConstants.PlayerHalfWidth, my, int.MinValue / 2,
                true, out int top);
            return ToWorld(top);
        }

        // 칼이 꽂힐 높이(예고 표시용)
        public float SwordLandingY(float x)
        {
            int mx = Mathf.RoundToInt(x * GameConstants.UnitsPerWorldUnit);
            return ToWorld(Source.KnifeLandingY(mx, Source.KnifeSpawnY));
        }
    }
}
