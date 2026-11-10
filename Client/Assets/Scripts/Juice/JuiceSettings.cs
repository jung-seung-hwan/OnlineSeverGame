using System.Collections;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Client.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.Juice
{
    // 소리 설정과 UI 클릭 소리를 UI 개편의 자리(UiHooks, docs/work/ui-revamp.md 5장)에 붙인다. GameUI 는 고치지 않는다.
    // - 설정 창(Esc 또는 설정 버튼)의 '소리' 칸: 효과음·배경음 음량 슬라이더(0~1), 화면 흔들림 켜기/끄기 버튼.
    //   값은 바로 적용되고 PlayerPrefs 에 기억한다(JuiceAudio.SavePrefs).
    // - 젤리 버튼을 누를 때마다 클릭 소리(UiHooks.ButtonClicked).
    // 시험용 실행 인자 -juicepanel: 시작 1.5초 뒤 설정 창을 연다(스크린샷 확인용).
    public sealed class JuiceSettings : MonoBehaviour
    {
        static bool _hooked;

        static string T(string ko, string en) => Loc.Current == Language.Korean ? ko : en;

        public static void Register(GameObject host)
        {
            var s = host.AddComponent<JuiceSettings>();
            if (!_hooked)
            {
                _hooked = true;
                UiHooks.ButtonClicked += _ => JuiceAudio.UiClick();
            }
            UiHooks.AddSettingsSection("sound", 10, Build);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-juicepanel") >= 0) s.StartCoroutine(s.OpenLater());
        }

        static void Build(VisualElement box)
        {
            UiKit.SliderRow("slider-sfx", T("효과음", "Effects"), JuiceAudio.SfxVolume, v =>
            {
                JuiceAudio.SfxVolume = v;
                JuiceAudio.SavePrefs();
            }, box);
            UiKit.SliderRow("slider-bgm", T("배경음", "Music"), JuiceAudio.BgmVolume, v =>
            {
                JuiceAudio.BgmVolume = v;
                JuiceAudio.SavePrefs();
            }, box);
            Button shake = null;
            shake = UiKit.Button("btn-shake", ShakeText(), () =>
            {
                JuiceAudio.ScreenShake = !JuiceAudio.ScreenShake;
                JuiceAudio.SavePrefs();
                UiKit.SetText(shake, ShakeText());
                UiKit.SetClass(shake, "jb--selected", JuiceAudio.ScreenShake);
            }, "jb--small", box);
            UiKit.SetClass(shake, "jb--selected", JuiceAudio.ScreenShake);
        }

        static string ShakeText() =>
            T("화면 흔들림: ", "Screen shake: ") + (JuiceAudio.ScreenShake ? T("켜짐", "On") : T("꺼짐", "Off"));

        IEnumerator OpenLater()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            var ui = GetComponent<GameUI>();
            if (ui != null) ui.OpenSettings();
        }
    }
}
