using System.Collections.Generic;
using OnlineSeverGame.Shared.Simulation;
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
        // 동작 고르기용(PlayerView)
        public bool Grounded;
        public float VelocityY;    // 위가 +
        public bool Moving;        // 가로로 움직이는 중
        public bool Stunned;       // 밀려나는 중
        public bool Winner;        // 결과 화면의 승자
        public bool Spectator;     // 라운드 도중 들어와 관전 중
        public bool Crouching;     // 아래키로 엎드림(세로로 눌러 그린다)
        public bool Stomped;       // 밟혀서 납작해진 스턴(더 세게 눌러 그린다)
        public float DashCooldown; // 내 캐릭터만: 0 = 대시 가능, 1 = 막 썼음. 다른 플레이어는 0
    }

    // 밀리는 오브젝트(서버 계산, 스냅샷 보간). 위치는 아래쪽 가운데.
    public struct BoxRender
    {
        public int Id;
        public Vector2 Position;
        public Vector2 Size;
        public bool Crumbling;     // 창고 상자 개수 제한으로 곧 사라짐(깜빡인다)
    }

    public enum SwordPhase
    {
        Warning,  // 떨어지기 전 예고
        Falling,
        Landed,   // 바닥·발판에 꽂힘(사라지기 전 잠깐)
    }

    // 떨어지는 위험물(칼·고드름·상자·포크). 이름은 첫 맵의 칼에서 왔다.
    public struct SwordRender
    {
        public int Id;
        public HazardKind Kind;
        public SwordPhase Phase;
        public Vector2 Position;   // 칼끝 기준
        public float WarningProgress; // 0..1, 예고 시작 0 → 낙하 직전 1
        public float LandingY;        // 꽂힐 면의 높이(예고 기둥 아래 끝)
        public float SpawnY;          // 떨어지기 시작하는 높이(고드름은 예고 중 여기 매달려 떨린다)
    }

    public sealed class WorldRender
    {
        public readonly List<PlayerRender> Players = new List<PlayerRender>();
        public readonly List<SwordRender> Swords = new List<SwordRender>();
        public readonly List<BoxRender> Boxes = new List<BoxRender>();

        public void Clear()
        {
            Players.Clear();
            Swords.Clear();
            Boxes.Clear();
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
