using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // WorldRender 를 받아 플레이어·칼 뷰를 만들고 지운다.
    public sealed class WorldView : MonoBehaviour
    {
        readonly Dictionary<int, PlayerView> _players = new Dictionary<int, PlayerView>();
        readonly Dictionary<int, SwordView> _swords = new Dictionary<int, SwordView>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _remove = new List<int>();
        StageLayout _layout;

        public IReadOnlyDictionary<int, PlayerView> Players => _players;

        public void Init(StageLayout layout)
        {
            _layout = layout;
            Clear();
        }

        public void Clear()
        {
            foreach (var p in _players.Values) Destroy(p.gameObject);
            foreach (var s in _swords.Values) Destroy(s.gameObject);
            _players.Clear();
            _swords.Clear();
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
                    view = SwordView.Create(transform, _layout.SwordSize, _layout.Height);
                    view.name = "Sword " + s.Id;
                    _swords.Add(s.Id, view);
                }
                view.Apply(s);
                _seen.Add(s.Id);
            }
            Prune(_swords);
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
