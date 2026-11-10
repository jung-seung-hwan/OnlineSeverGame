using OnlineSeverGame.Shared.Protocol;
using UnityEngine;

namespace OnlineSeverGame.Client.Model
{
    // 내가 고른 캐릭터(젤리·젤리곰·푸딩, docs/work/char-select.md). PlayerPrefs "character"(1~3)에 기억하고,
    // 접속할 때 Hello 에 실어 보낸다. 실행 인자 -character jelly|bear|pudding 이 있으면 이번 실행만 그것(저장 안 함).
    // 색은 고르지 않는다(플레이어 번호). 접속 전에는 번호를 모르므로 마지막으로 받은 번호의 색(LastColor)으로 미리 보여 준다.
    public static class CharacterChoice
    {
        const string PrefKey = "character";
        const string ColorKey = "lastColor";

        public static readonly CharacterKind[] All = { CharacterKind.Jelly, CharacterKind.Bear, CharacterKind.Pudding };

        // 시험(-uitest)에서는 false: 버튼을 눌러도 사용자 저장값을 바꾸지 않는다.
        public static bool Persist = true;

        static CharacterKind _current;
        static bool _loaded;

        public static CharacterKind Current
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    int v = PlayerPrefs.GetInt(PrefKey, (int)CharacterKind.Jelly);
                    _current = ProtocolConstants.IsValidCharacter((byte)v) ? (CharacterKind)v : CharacterKind.Jelly;
                }
                return _current;
            }
        }

        public static void Set(CharacterKind kind)
        {
            if (!ProtocolConstants.IsValidCharacter((byte)kind)) return;
            _current = kind;
            _loaded = true;
            if (!Persist) return;
            PlayerPrefs.SetInt(PrefKey, (int)kind);
            PlayerPrefs.Save();
        }

        // 실행 인자용: 저장하지 않고 이번 실행만.
        public static void Override(CharacterKind kind)
        {
            _current = kind;
            _loaded = true;
        }

        public static bool TryParse(string text, out CharacterKind kind)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "1": case "jelly": kind = CharacterKind.Jelly; return true;
                case "2": case "bear": kind = CharacterKind.Bear; return true;
                case "3": case "pudding": kind = CharacterKind.Pudding; return true;
                default: kind = CharacterKind.Jelly; return false;
            }
        }

        // 마지막 접속에서 받은 색(플레이어 번호 - 1). 접속 화면 미리보기용.
        public static int LastColor
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ColorKey, 0), 0, 7);
            set
            {
                if (!Persist || value == LastColor) return;
                PlayerPrefs.SetInt(ColorKey, value);
                PlayerPrefs.Save();
            }
        }
    }
}
