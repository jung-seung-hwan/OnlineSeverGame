using UnityEditor;

namespace OnlineSeverGame.Client.EditorTools
{
    // Assets/Resources/Art 아래 그림(Art/sprites 에서 복사한 Blender 렌더)의 가져오기 설정.
    // 스프라이트 자르기·피벗은 실행 중에 ArtSprites 가 하므로 여기서는 화질만 맞춘다:
    // 압축·밉맵 없음(외곽선이 뭉개지지 않게), 2의 거듭제곱으로 늘리지 않음, 최대 4096
    // (캐릭터 시트가 16행 2048 × 4096, docs/work/char-motion.md. 2048이면 줄어들어 프레임 자르기가 틀어진다).
    public sealed class ArtImporter : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.filterMode = UnityEngine.FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 4096;
        }
    }
}
