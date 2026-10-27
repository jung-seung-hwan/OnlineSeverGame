using System.Collections.Generic;
using UnityEngine;

namespace OnlineSeverGame.Client.Model
{
    // 화면에 그릴 상태. 네트워크 스냅샷(보간·예측 결과)이나 오프라인 미리보기가 이 형태로 채운다.
    public struct PlayerRender
    {
        public int Id;
        public string Name;
        public int ColorIndex;
        public Vector2 Position;   // 발 중앙(바닥 접점) 기준
        public bool Alive;
        public bool Dashing;
        public int Facing;         // -1 왼쪽, +1 오른쪽
        public bool IsLocal;
    }

    public enum SwordPhase
    {
        Warning,  // 떨어지기 전 예고
        Falling,
        Landed,   // 바닥·발판에 꽂힘(사라지기 전 잠깐)
    }

    public struct SwordRender
    {
        public int Id;
        public SwordPhase Phase;
        public Vector2 Position;   // 칼끝 기준
        public float WarningProgress; // 0..1, 예고 시작 0 → 낙하 직전 1
        public float LandingY;        // 꽂힐 면의 높이(예고 기둥 아래 끝)
    }

    public sealed class WorldRender
    {
        public readonly List<PlayerRender> Players = new List<PlayerRender>();
        public readonly List<SwordRender> Swords = new List<SwordRender>();

        public void Clear()
        {
            Players.Clear();
            Swords.Clear();
        }
    }

    public static class PlayerColors
    {
        public static readonly Color[] All =
        {
            new Color(0.90f, 0.25f, 0.25f), // 빨강
            new Color(0.25f, 0.45f, 0.95f), // 파랑
            new Color(0.25f, 0.80f, 0.35f), // 초록
            new Color(0.95f, 0.85f, 0.20f), // 노랑
            new Color(0.65f, 0.35f, 0.90f), // 보라
            new Color(0.98f, 0.55f, 0.15f), // 주황
            new Color(0.20f, 0.85f, 0.90f), // 하늘
            new Color(0.95f, 0.45f, 0.75f), // 분홍
        };

        public static Color Get(int index)
        {
            if (index < 0) index = 0;
            return All[index % All.Length];
        }
    }
}
