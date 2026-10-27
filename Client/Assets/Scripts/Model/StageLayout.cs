using System.Collections.Generic;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.Model
{
    // Shared Stage 를 Unity 월드 단위로 옮긴 그리기용 지형. x 는 0(왼쪽 벽) ~ Width, 바닥 윗면 y = 0.
    // 라운드마다 맵이 바뀌므로 같은 객체를 SetStage 로 다시 채운다(Version 이 오르면 화면을 다시 만든다).
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
        // 맵이 바뀔 때마다 1씩 오른다.
        public int Version;
        // 그리는 시점의 라운드 틱(Playing 이 아니면 0). 녹는 발판의 지금 모양을 정한다.
        public uint RoundTick;

        // 지금 라운드 틱의 지형(녹는 발판이 줄어든 모양)
        public Stage Current => Source.AtRoundTick(RoundTick);

        public static float ToWorld(int milli) => milli / (float)GameConstants.UnitsPerWorldUnit;

        public static StageLayout FromShared(Stage stage)
        {
            var s = new StageLayout();
            s.SetStage(stage);
            return s;
        }

        public void SetStage(Stage stage)
        {
            stage = stage.Base;
            if (Source == stage) return;
            Source = stage;
            Version++;
            RoundTick = 0;
            Width = ToWorld(stage.Width);
            Height = ToWorld(stage.ViewHeight);
            SwordSpawnY = ToWorld(stage.KnifeSpawnY);
            PlayerSize = new Vector2(ToWorld(GameConstants.PlayerWidth), ToWorld(GameConstants.PlayerHeight));
            SwordSize = HazardSize(stage.Hazard);
            Solids.Clear();
            Solids.Add(new Solid { CenterX = Width / 2f, TopY = 0f, Width = Width, Height = 1f });
            foreach (var p in stage.Platforms)
            {
                Solids.Add(new Solid
                {
                    CenterX = ToWorld(p.Left + p.Right) / 2f,
                    TopY = ToWorld(p.Top),
                    Width = ToWorld(p.Right - p.Left),
                    Height = 0.35f,
                });
            }
        }

        public static Vector2 HazardSize(HazardKind kind)
        {
            HazardSpec spec = HazardSpec.Of(kind);
            return new Vector2(ToWorld(spec.Width), ToWorld(spec.Height));
        }

        // 화면에 보일 맵 이름
        public static string DisplayName(byte stageId)
        {
            switch (stageId)
            {
                case 1: return "주방";
                case 2: return "겨울 처마";
                case 3: return "창고";
                case 4: return "디저트 가게";
                default: return "맵 " + stageId;
            }
        }

        // 공중에서 죽은 캐릭터의 웅덩이를 그릴 높이: 발 아래 첫 바닥·발판 윗면
        public float SurfaceBelow(float x, float y)
        {
            int mx = Mathf.RoundToInt(x * GameConstants.UnitsPerWorldUnit);
            int my = Mathf.RoundToInt(y * GameConstants.UnitsPerWorldUnit);
            Current.TryFindLanding(mx - GameConstants.PlayerHalfWidth, mx + GameConstants.PlayerHalfWidth, my, int.MinValue / 2,
                true, out int top);
            return ToWorld(top);
        }

        // 위험물이 꽂힐 높이(예고 표시용). 밀리는 상자 윗면에도 꽂히고, 고드름은 얼음 발판을 뚫는다.
        public float SwordLandingY(float x, IReadOnlyList<PushableState> boxes = null)
        {
            return SwordLandingY(Source.Hazard, x, boxes);
        }

        public float SwordLandingY(HazardKind kind, float x, IReadOnlyList<PushableState> boxes = null)
        {
            int mx = Mathf.RoundToInt(x * GameConstants.UnitsPerWorldUnit);
            Stage stage = Current;
            return ToWorld(PushablePhysics.HazardLandingY(stage, boxes, kind, mx, stage.KnifeSpawnY));
        }
    }
}
