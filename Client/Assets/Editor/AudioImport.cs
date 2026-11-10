using UnityEditor;
using UnityEngine;

namespace OnlineSeverGame.Client.EditorTools
{
    // Assets/Resources/Audio 소리(Client/SoundGen/gen_sounds.py 로 만든 WAV)의 가져오기 설정(docs/work/juice-sound.md 0장).
    // 효과음(sfx_): 모노, 압축 없음, 로드할 때 풀기(짧고 자주 나서 지연 없이). 배경음(bgm_): 스테레오, Vorbis 압축, 메모리에 압축 유지.
    public sealed class AudioImport : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Audio/";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            bool bgm = System.IO.Path.GetFileName(assetPath).StartsWith("bgm_");
            ai.forceToMono = !bgm; // 배경음은 스테레오(기준 v2), 효과음은 모노
            if (bgm)
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                ai.loadInBackground = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.PCM;
            }
            ai.defaultSampleSettings = s;
        }
    }
}
