using System.Collections.Generic;
using UnityEngine;

namespace OnlineSeverGame.Client.Model
{
    // 스테이지 지형(바닥·발판)과 화면 크기. 모든 값은 월드 단위, 발판은 중심 x와 윗면 y 기준.
    public sealed class StageLayout
    {
        public struct Solid
        {
            public float CenterX;
            public float TopY;
            public float Width;
            public float Height;
        }

        public float Width;        // 스테이지 좌우 폭 (x: -Width/2 .. +Width/2)
        public float Height;       // 화면에 보일 높이 (y: 0 .. Height)
        public float SwordSpawnY;  // 칼이 생기는 높이
        public Vector2 PlayerSize;
        public Vector2 SwordSize;
        public readonly List<Solid> Solids = new List<Solid>();

        // Shared/ 스테이지 정의가 커밋되기 전까지 쓰는 임시 배치.
        public static StageLayout Placeholder()
        {
            var s = new StageLayout
            {
                Width = 24f,
                Height = 13.5f,
                SwordSpawnY = 13f,
                PlayerSize = new Vector2(0.8f, 1.0f),
                SwordSize = new Vector2(0.3f, 1.6f),
            };
            s.Solids.Add(new Solid { CenterX = 0f, TopY = 0f, Width = 24f, Height = 1f });   // 바닥
            s.Solids.Add(new Solid { CenterX = -6f, TopY = 3f, Width = 5f, Height = 0.4f });
            s.Solids.Add(new Solid { CenterX = 6f, TopY = 3f, Width = 5f, Height = 0.4f });
            s.Solids.Add(new Solid { CenterX = 0f, TopY = 6f, Width = 6f, Height = 0.4f });
            return s;
        }
    }
}
