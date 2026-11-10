using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Protocol;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 다음 라운드 맵과 교체 방식(대기 화면 오른쪽, 결과 화면 오른쪽). 다음 맵 미리보기 그림은 모두에게 보이고,
    // 방장에게는 맵 그림 버튼 네 개(번호 = 키 1~4)와 교체 방식 버튼 세 개(키 R)가 더 보인다.
    // 누르면 서버에 요청만 보내고(RequestMap), 화면 값은 서버가 돌려주는 RoomInfo 로 바뀐다(예전과 같음).
    // 미리보기 그림은 Resources/UI/maps/map{번호}.png(Art/sprites/<맵>/*_full.png 를 640 폭으로 줄인 것, 만드는 법은 문서 6장).
    public sealed class MapPicker
    {
        static readonly MapRotation[] Rotations = { MapRotation.Cycle, MapRotation.Random, MapRotation.Fixed };
        static readonly Dictionary<int, Texture2D> _thumbs = new Dictionary<int, Texture2D>();

        public readonly VisualElement Root;
        readonly GameUI _ui;
        readonly Label _title, _rotation, _previewName, _chosen, _guest, _keys;
        readonly VisualElement _preview, _hostBox;
        readonly Button[] _maps = new Button[4];
        readonly Button[] _rots = new Button[3];
        int _shownPreview = -1;

        public static MapRotation NextRotation(MapRotation r)
        {
            int i = System.Array.IndexOf(Rotations, r);
            return Rotations[(i + 1) % Rotations.Length];
        }

        public static Texture2D Thumb(int stageId)
        {
            if (_thumbs.TryGetValue(stageId, out var t)) return t;
            t = Resources.Load<Texture2D>("UI/maps/map" + stageId);
            _thumbs[stageId] = t;
            return t;
        }

        // prefix 는 버튼 이름 앞머리("lobby" → "lobby-map-1", "lobby-rot-random")
        public MapPicker(GameUI ui, string prefix, float width)
        {
            _ui = ui;
            Root = UiKit.El("panel panel--tight");
            Root.style.width = width;

            var head = UiKit.El(null, Root);
            head.style.flexDirection = FlexDirection.Row;
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 6;
            _title = UiKit.Text("", "t-h2", head);
            _rotation = UiKit.Text("", "t-small t-muted", head);

            _preview = UiKit.El("map-preview", Root);
            _preview.pickingMode = PickingMode.Ignore;
            _preview.style.height = (width - 28f) * 0.5f;
            var strip = UiKit.El(null, _preview);
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.alignItems = Align.Center;
            strip.AddToClassList("map-preview-name");
            _previewName = UiKit.Text("", null, strip);
            _previewName.style.flexGrow = 1;
            _chosen = UiKit.Text("", "badge badge--host", strip);
            _chosen.style.marginRight = 8;

            _hostBox = UiKit.El(null, Root);
            var thumbs = UiKit.El("thumb-row", _hostBox);
            for (int i = 0; i < 4; i++)
            {
                int id = i + 1;
                var b = UiKit.Button(prefix + "-map-" + id, "", () => Request((byte)id, 0), null, null);
                b.ClearClassList();
                b.AddToClassList("thumb");
                b.Clear(); // 광택 띠 대신 번호표
                float tw = (width - 28f - 3 * 8f) / 4f;
                b.style.width = tw;
                b.style.height = tw * 0.62f;
                var tex = Thumb(id);
                if (tex != null) b.style.backgroundImage = new StyleBackground(tex);
                var num = UiKit.Text(id.ToString(), "thumb-num", b);
                num.pickingMode = PickingMode.Ignore;
                thumbs.Add(b);
                _maps[i] = b;
            }
            var seg = UiKit.El("seg-row", _hostBox);
            for (int i = 0; i < Rotations.Length; i++)
            {
                var r = Rotations[i];
                var b = UiKit.Button(prefix + "-rot-" + r.ToString().ToLowerInvariant(), "", () => Request(0, r), "jb--small", seg);
                _rots[i] = b;
            }
            _rots[_rots.Length - 1].style.marginRight = 0;
            _keys = UiKit.Text("", "t-tiny t-muted t-center", _hostBox);
            _keys.style.marginTop = 6;

            _guest = UiKit.Text("", "t-small t-muted t-wrap", Root);
            _guest.style.marginTop = 8;
        }

        void Request(byte stageId, MapRotation rotation) => _ui.Game.Session?.RequestMap(stageId, rotation);

        public void Refresh(SessionState s)
        {
            int next = s.NextMapId > 0 ? s.NextMapId : s.MapId;
            UiKit.SetText(_title, Loc.T("lobby.mapTitle"));
            UiKit.SetText(_rotation, Loc.F("map.rotation", Loc.RotationName(s.Rotation)));
            UiKit.SetText(_previewName, Loc.MapName(next));
            UiKit.Show(_chosen, s.NextChosen);
            UiKit.SetText(_chosen, Loc.T("map.chosen").Trim().Trim('(', ')'));
            if (next != _shownPreview)
            {
                _shownPreview = next;
                var tex = Thumb(next);
                _preview.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
            }

            bool host = s.IsHost;
            UiKit.Show(_hostBox, host);
            UiKit.Show(_guest, !host && s.HostId != 0); // 방장이 없으면(오프라인 미리보기) 안내를 숨긴다
            if (host)
            {
                for (int i = 0; i < _maps.Length; i++) UiKit.SetClass(_maps[i], "thumb--selected", next == i + 1);
                for (int i = 0; i < _rots.Length; i++)
                {
                    UiKit.SetText(_rots[i], Loc.RotationName(Rotations[i]));
                    UiKit.SetClass(_rots[i], "jb--selected", s.Rotation == Rotations[i]);
                }
                UiKit.SetText(_keys, Loc.T("map.keys"));
            }
            else
            {
                string hostName = s.TryGet(s.HostId, out RosterEntry h) ? h.Name : "-";
                UiKit.SetText(_guest, Loc.F("map.hostPicks", hostName));
            }
        }
    }
}
