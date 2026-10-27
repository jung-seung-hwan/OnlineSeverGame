using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // WorldRender 를 받아 플레이어·칼·밀리는 상자 뷰를 만들고 지운다. 흔들리는 장식도 여기서 움직인다.
    public sealed class WorldView : MonoBehaviour
    {
        readonly Dictionary<int, PlayerView> _players = new Dictionary<int, PlayerView>();
        readonly Dictionary<int, SwordView> _swords = new Dictionary<int, SwordView>();
        readonly Dictionary<int, BoxView> _boxes = new Dictionary<int, BoxView>();
        DecorView _decor;
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _remove = new List<int>();
        StageLayout _layout;

        public IReadOnlyDictionary<int, PlayerView> Players => _players;
        public DecorView Decor => _decor;

        public void Init(StageLayout layout)
        {
            _layout = layout;
            if (_decor == null)
            {
                _decor = new GameObject("Decor").AddComponent<DecorView>();
                _decor.transform.SetParent(transform, false);
            }
            _decor.Build(layout);
            Clear();
        }

        public void Clear()
        {
            foreach (var p in _players.Values) Destroy(p.gameObject);
            foreach (var s in _swords.Values) Destroy(s.gameObject);
            foreach (var b in _boxes.Values) Destroy(b.gameObject);
            _players.Clear();
            _swords.Clear();
            _boxes.Clear();
        }

        public void Apply(WorldRender world)
        {
            _seen.Clear();
            foreach (var p in world.Players)
            {
                if (!_players.TryGetValue(p.Id, out var view))
                {
                    view = PlayerView.Create(transform, _layout.PlayerSize);
                    view.name = "Player " + p.Id;
                    _players.Add(p.Id, view);
                }
                view.Apply(p);
                _seen.Add(p.Id);
            }
            Prune(_players);

            _seen.Clear();
            foreach (var s in world.Swords)
            {
                if (!_swords.TryGetValue(s.Id, out var view))
                {
                    view = SwordView.Create(transform, s.Kind, _layout.Height);
                    view.name = "Sword " + s.Id;
                    _swords.Add(s.Id, view);
                }
                view.Apply(s);
                _seen.Add(s.Id);
            }
            Prune(_swords);

            _seen.Clear();
            foreach (var b in world.Boxes)
            {
                if (!_boxes.TryGetValue(b.Id, out var view))
                {
                    view = BoxView.Create(transform, _layout.Source.Id);
                    view.name = "Box " + b.Id;
                    _boxes.Add(b.Id, view);
                }
                view.Apply(b);
                _seen.Add(b.Id);
            }
            Prune(_boxes);

            _decor.Step(world.Players, Time.deltaTime);
        }

        void Prune<T>(Dictionary<int, T> map) where T : Component
        {
            _remove.Clear();
            foreach (var kv in map)
                if (!_seen.Contains(kv.Key)) _remove.Add(kv.Key);
            foreach (var id in _remove)
            {
                Destroy(map[id].gameObject);
                map.Remove(id);
            }
        }
    }
}
