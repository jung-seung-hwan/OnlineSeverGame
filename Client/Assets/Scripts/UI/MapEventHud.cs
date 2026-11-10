using OnlineSeverGame.Client.Game;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 맵 이벤트 알림(docs/work/map-layout.md 4.5). UI 개편의 확장 자리(UiHooks, HUD 아래 가운데)에 알약 하나를 붙인다:
    // 예고 2초는 깜빡이며 "곧: 강풍 →", 켜져 있는 동안 "강풍 →  7초". 이벤트는 라운드 틱만으로 정해지므로 맵이 그리는 시각
    // (StageLayout.DrawRoundTick, 위험물·움직이는 발판과 같은 시각)의 상태를 그대로 읽는다. GameUI.cs 는 고치지 않는다.
    static class MapEventHud
    {
        static GameBootstrap _game;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            UiHooks.AddHudElement(HudSlot.BottomCenter, slot =>
            {
                var pill = UiKit.El("pill warn-pill", slot);
                pill.name = "map-event-pill";
                pill.pickingMode = PickingMode.Ignore;
                var text = UiKit.Text("", "t-small", pill);
                UiKit.Show(pill, false);
                pill.schedule.Execute(() => Refresh(pill, text)).Every(50);
            });
        }

        static void Refresh(VisualElement pill, Label text)
        {
            if (_game == null) _game = Object.FindAnyObjectByType<GameBootstrap>();
            var layout = _game != null ? _game.Layout : null;
            var screen = _game != null && _game.Session != null ? _game.Session.State.Screen : ClientScreen.Connect;
            bool inRound = screen == ClientScreen.Playing || screen == ClientScreen.Spectating;
            if (!inRound || layout == null || layout.Source == null || layout.Source.EventSpec == null)
            {
                UiKit.Show(pill, false);
                return;
            }
            MapEventState e = layout.Source.AtRoundTick((uint)System.Math.Max(0.0, layout.DrawRoundTick)).Event;
            if (e.Phase == MapEventPhase.None)
            {
                UiKit.Show(pill, false);
                return;
            }
            string arrow = e.Kind == MapEventKind.Wind || e.Kind == MapEventKind.Conveyor ? (e.Dir > 0 ? "→" : "←") : "";
            UiKit.SetText(text, e.IsWarning
                ? Loc.F("event.warning", Loc.MapEventName(e.Kind), arrow)
                : Loc.F("event.active", Loc.MapEventName(e.Kind), arrow, e.TicksLeft / (float)GameConstants.TickRate));
            UiKit.Show(pill, true);
            pill.style.opacity = e.IsWarning ? 0.55f + 0.45f * Mathf.PingPong(Time.time * 4f, 1f) : 1f;
        }
    }
}
