using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 임시 그래픽: 흰색 1x1 스프라이트에 색을 입혀 사각형을 그린다.
    public static class Shapes
    {
        static Sprite _white;

        public static Sprite White
        {
            get
            {
                if (_white != null) return _white;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(pixels);
                tex.filterMode = FilterMode.Point;
                tex.Apply();
                // pixelsPerUnit = 4 이므로 스프라이트 하나가 1x1 월드 단위다.
                _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                _white.name = "White";
                return _white;
            }
        }

        // 중심 기준 사각형. size 는 월드 단위.
        public static SpriteRenderer Rect(Transform parent, string name, Color color, Vector2 size, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = White;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        public static void Resize(SpriteRenderer sr, Vector2 size)
        {
            sr.transform.localScale = new Vector3(size.x, size.y, 1f);
        }
    }
}
