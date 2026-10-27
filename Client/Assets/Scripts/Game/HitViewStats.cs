using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.Game
{
    // 칼 판정 진단(docs/work/map-select-hit.md). 매 프레임 화면에 그린 내 몸과 위험물이 닿아 보이는지를
    // 두 가지 그리기 방식으로 따로 센다.
    //   - 예전: 위험물을 스냅샷 보간 시각(약 2틱 과거)에 그림
    //   - 지금: 위험물을 내 예측 시각에 맞춰 계산해 그림
    // 서버 판정(내 사망)과 맞춰 보고 라운드 끝에 한 줄로 남긴다:
    //   "닿아 보였는데 살았다"(보였는데 안 맞음) / "죽었는데 직전 0.5초 안에 닿아 보인 위험물이 없다"(안 보였는데 맞음).
    // 닿음 판정은 서버와 같은 그림 모양(HitShapes)을 그린 위치에 놓고 잰다.
    public sealed class HitViewStats
    {
        const double KillWindowSeconds = 0.5;

        sealed class Track
        {
            public double LastOld = -1, LastNew = -1;
            public bool KillOld, KillNew;
        }

        readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        readonly Dictionary<int, float> _oldY = new Dictionary<int, float>();
        int _deaths, _unseenOld, _unseenNew;
        double _leadSum, _offsetSum;
        int _leadFrames, _offsetCount;
        float _offsetMax;

        // 지금 방식으로 그린 위험물이 처음 내 몸에 닿아 보인 횟수(라운드가 바뀌어도 계속 센다. 시험용 -touchshot 이
        // 이 값이 오를 때 화면을 찍는다)
        public int TouchCount { get; private set; }

        // me: 예측 상태(그림 모양 고르기), drawn: 화면에 그린 발 위치(유닛)
        public void Frame(double now, PlayerState me, Vector2 drawn, List<SwordRender> oldDraw, List<SwordRender> newDraw,
            double leadTicks)
        {
            me.X = Mathf.RoundToInt(drawn.x * GameConstants.UnitsPerWorldUnit);
            me.Y = Mathf.RoundToInt(drawn.y * GameConstants.UnitsPerWorldUnit);
            _leadSum += leadTicks;
            _leadFrames++;

            _oldY.Clear();
            foreach (var s in oldDraw)
            {
                if (s.Phase != SwordPhase.Falling) continue;
                _oldY[s.Id] = s.Position.y;
                if (Touches(me, s)) Get(s.Id).LastOld = now;
            }
            foreach (var s in newDraw)
            {
                if (s.Phase != SwordPhase.Falling) continue;
                if (Touches(me, s))
                {
                    var t = Get(s.Id);
                    if (t.LastNew < 0) TouchCount++;
                    t.LastNew = now;
                }
                if (_oldY.TryGetValue(s.Id, out float y))
                {
                    float d = Mathf.Abs(y - s.Position.y);
                    _offsetSum += d;
                    _offsetCount++;
                    if (d > _offsetMax) _offsetMax = d;
                }
            }
        }

        static bool Touches(PlayerState me, SwordRender s)
        {
            int x = Mathf.RoundToInt(s.Position.x * GameConstants.UnitsPerWorldUnit);
            int y = Mathf.RoundToInt(s.Position.y * GameConstants.UnitsPerWorldUnit);
            return HitShapes.Separation(me, s.Kind, x, y, y) < 0;
        }

        Track Get(int id)
        {
            if (!_tracks.TryGetValue(id, out var t)) _tracks[id] = t = new Track();
            return t;
        }

        // 서버가 나를 탈락시켰다(스냅샷에서 Alive 가 꺼짐).
        public void Death(double now)
        {
            _deaths++;
            bool old = false, neu = false;
            foreach (var t in _tracks.Values)
            {
                if (t.LastOld >= 0 && now - t.LastOld <= KillWindowSeconds) { t.KillOld = true; old = true; }
                if (t.LastNew >= 0 && now - t.LastNew <= KillWindowSeconds) { t.KillNew = true; neu = true; }
            }
            if (!old) _unseenOld++;
            if (!neu) _unseenNew++;
        }

        public string TakeSummary()
        {
            int survivedOld = 0, survivedNew = 0;
            foreach (var t in _tracks.Values)
            {
                if (t.LastOld >= 0 && !t.KillOld) survivedOld++;
                if (t.LastNew >= 0 && !t.KillNew) survivedNew++;
            }
            string line = $"hitview deaths={_deaths} " +
                          $"old(interp) touchedButSurvived={survivedOld} diedUntouched={_unseenOld} " +
                          $"new(predicted) touchedButSurvived={survivedNew} diedUntouched={_unseenNew} " +
                          $"lead={(_leadFrames > 0 ? _leadSum / _leadFrames : 0):0.00}ticks " +
                          $"fallingOffset avg={(_offsetCount > 0 ? _offsetSum / _offsetCount : 0):0.00}u max={_offsetMax:0.00}u";
            _tracks.Clear();
            _deaths = _unseenOld = _unseenNew = 0;
            _leadSum = _offsetSum = 0;
            _leadFrames = _offsetCount = 0;
            _offsetMax = 0;
            return line;
        }
    }
}
