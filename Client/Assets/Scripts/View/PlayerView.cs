using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 젤리 캐릭터 스프라이트(ArtSprites). 상태로 동작을 고르고 프레임을 넘긴다.
    // 대시 잔상을 가진다(내 캐릭터 표시는 UI 이름표의 ▼). 시트는 오른쪽을 보므로 왼쪽은 뒤집는다.
    public sealed class PlayerView : MonoBehaviour
    {
        SpriteRenderer _body;
        SpriteRenderer _trail;
        string _anim;
        float _animTime;

        public PlayerRender State { get; private set; }

        public static PlayerView Create(Transform parent, Vector2 size)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PlayerView>();
            v._trail = NewRenderer(go.transform, "Trail", 9);
            v._body = NewRenderer(go.transform, "Body", 11);
            return v;
        }

        static SpriteRenderer NewRenderer(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            return sr;
        }

        static string PickAnimation(PlayerRender s)
        {
            if (s.Spectator) return "idle";
            if (!s.Alive) return "death";
            if (s.Winner) return "victory";
            if (s.Stunned) return "knockback";
            if (s.Dashing) return "dash";
            if (!s.Grounded) return s.VelocityY > 0f ? "jump" : "fall";
            return s.Moving ? "run" : "idle";
        }

        public void Apply(PlayerRender s)
        {
            State = s;
            transform.localPosition = new Vector3(s.Position.x, s.Position.y, 0);

            string anim = PickAnimation(s);
            if (anim != _anim)
            {
                _anim = anim;
                _animTime = 0f;
            }
            else
            {
                _animTime += Time.deltaTime;
            }
            var sprite = ArtSprites.Character(s.ColorIndex, anim).At(_animTime);
            bool flip = s.Facing < 0;

            _body.sprite = sprite;
            _body.flipX = flip;
            // 내 캐릭터를 다른 캐릭터 위에 그린다. 도중 입장 관전자는 흐리게, 죽은 웅덩이는 조금 어둡게.
            _body.sortingOrder = s.IsLocal ? 12 : 11;
            if (s.Spectator) _body.color = new Color(1f, 1f, 1f, 0.35f);
            else if (!s.Alive) _body.color = new Color(0.8f, 0.8f, 0.8f, 0.9f);
            else _body.color = Color.white;

            int facing = flip ? -1 : 1;
            _trail.enabled = s.Alive && s.Dashing;
            _trail.sprite = sprite;
            _trail.flipX = flip;
            _trail.color = new Color(1f, 1f, 1f, 0.35f);
            _trail.transform.localPosition = new Vector3(-facing * 0.45f, 0, 0);
        }
    }
}
