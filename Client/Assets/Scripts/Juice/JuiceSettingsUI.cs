using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.Juice
{
    // 소리·흔들림 설정 버튼과 패널(IMGUI, 오른쪽 아래 구석). GameUI 는 고치지 않는다(UI 개편 세션과 겹치지 않게).
    // 버튼 '소리'를 누르면 패널이 열린다: 효과음 음량, 배경음 음량, 화면 흔들림 켜기/끄기. 값은 바로 적용되고 PlayerPrefs 에 기억한다.
    // UI 클릭 소리: 이 컴포넌트가 GUI.depth 를 가장 앞(-10)으로 두어 마우스 이벤트를 먼저 본다. 마우스를 뗄 때 어떤 IMGUI
    // 컨트롤(GameUI 의 버튼 포함)이 눌려 있었으면(hotControl) 클릭 소리를 낸다. 새 UI 는 JuiceAudio.UiClick() 을 부르면 된다.
    public sealed class JuiceSettingsUI : MonoBehaviour
    {
        bool _open;
        GUIStyle _label, _title;
        public static bool IsOpen;

        static string T(string ko, string en) => Loc.Current == Language.Korean ? ko : en;

        // 시험용: 실행 인자 -juicepanel 이면 패널을 열어 둔 채 시작(스크린샷 확인용)
        void Awake() => _open = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-juicepanel") >= 0;

        void OnGUI()
        {
            GUI.depth = -10;
            var e = Event.current;
            if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl != 0) JuiceAudio.UiClick();

            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                _label.normal.textColor = Color.white;
                _title = new GUIStyle(_label) { fontSize = 18, fontStyle = FontStyle.Bold };
            }

            float sw = Screen.width, sh = Screen.height;
            // 오른쪽 아래 구석(바닥 띠 위: 캐릭터를 가리지 않게), 닫혀 있으면 조금 흐리게
            var button = new Rect(sw - 92, sh - 34, 80, 26);
            var before = GUI.color;
            if (!_open && !button.Contains(e.mousePosition)) GUI.color = new Color(1f, 1f, 1f, 0.7f);
            if (GUI.Button(button, (_open ? "▼ " : "♪ ") + T("소리", "Sound"))) _open = !_open;
            GUI.color = before;
            IsOpen = _open;
            if (!_open) return;

            const float w = 300f, h = 150f;
            var panel = new Rect(sw - w - 16, button.y - h - 6, w, h);
            var old = GUI.color;
            GUI.color = new Color(0.06f, 0.06f, 0.08f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = old;

            float x = panel.x + 14, y = panel.y + 10, lw = 110, sw2 = panel.width - lw - 64;
            GUI.Label(new Rect(x, y, panel.width - 28, 26), T("소리 설정", "Sound settings"), _title);
            y += 34;
            float sfx = Slider(new Rect(x, y, lw + sw2 + 40, 26), T("효과음", "Effects"), JuiceAudio.SfxVolume, lw, sw2);
            y += 34;
            float bgm = Slider(new Rect(x, y, lw + sw2 + 40, 26), T("배경음", "Music"), JuiceAudio.BgmVolume, lw, sw2);
            y += 36;
            bool shake = GUI.Toggle(new Rect(x, y, panel.width - 28, 26), JuiceAudio.ScreenShake, " " + T("화면 흔들림", "Screen shake"));
            if (!Mathf.Approximately(sfx, JuiceAudio.SfxVolume) || !Mathf.Approximately(bgm, JuiceAudio.BgmVolume) || shake != JuiceAudio.ScreenShake)
            {
                JuiceAudio.SfxVolume = sfx;
                JuiceAudio.BgmVolume = bgm;
                JuiceAudio.ScreenShake = shake;
                JuiceAudio.SavePrefs();
            }
        }

        float Slider(Rect r, string label, float value, float labelWidth, float sliderWidth)
        {
            GUI.Label(new Rect(r.x, r.y, labelWidth, r.height), label, _label);
            float v = GUI.HorizontalSlider(new Rect(r.x + labelWidth, r.y + 8, sliderWidth, r.height), value, 0f, 1f);
            GUI.Label(new Rect(r.x + labelWidth + sliderWidth + 8, r.y, 40, r.height), Mathf.RoundToInt(v * 100f).ToString(), _label);
            return v;
        }
    }
}
