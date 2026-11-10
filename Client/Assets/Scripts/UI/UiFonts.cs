using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 게임 글꼴: 배달의민족 주아체(Jua, SIL Open Font License 1.1, Resources/UI/Fonts/Jua-OFL.txt).
    // 둥글고 굵은 손글씨풍이라 젤리 그림체와 맞고 한글·영문 모두 있다. 다만 KS X 1001 한글 2,350자만 있어
    // 드문 글자(예: '힣')와 화살표·가운뎃점 같은 기호(←→↑↓·—…▼)가 없다 → OS 글꼴(맑은 고딕 → Apple SD Gothic Neo →
    // Noto Sans CJK KR)을 대체 글꼴로 이어 붙여 빠진 글자만 그쪽에서 그린다. 대체 글꼴을 못 찾으면 그 글자만 빈칸이 된다.
    public static class UiFonts
    {
        static readonly string[] Fallbacks = { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "Arial" };

        static FontAsset _main;

        public static FontAsset Main
        {
            get
            {
                if (_main == null) Load();
                return _main;
            }
        }

        static void Load()
        {
            var font = Resources.Load<Font>("UI/Fonts/Jua-Regular");
            if (font == null)
            {
                Debug.LogError("[UI] font UI/Fonts/Jua-Regular not found");
                return;
            }
            _main = FontAsset.CreateFontAsset(font);
            _main.name = "Jua (dynamic)";
            var table = new List<FontAsset>();
            foreach (var family in Fallbacks)
            {
                FontAsset fb = null;
                try { fb = FontAsset.CreateFontAsset(family, "Regular"); }
                catch (System.Exception e) { Debug.Log("[UI] fallback font " + family + " failed: " + e.Message); }
                if (fb == null) continue;
                fb.name = family + " (fallback)";
                table.Add(fb);
                Debug.Log("[UI] fallback font: " + family);
                break; // 하나면 충분하다(OS 마다 첫 번째로 찾은 것)
            }
            _main.fallbackFontAssetTable = table;
        }

        public static void Apply(VisualElement root)
        {
            var main = Main;
            if (main != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(main);
        }
    }
}
