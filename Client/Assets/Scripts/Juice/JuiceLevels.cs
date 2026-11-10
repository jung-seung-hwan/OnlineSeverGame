// 자동 생성: Client/SoundGen/gen_sounds.py 가 쓴다. 손으로 고치지 않는다(docs/work/juice-sound.md 0장 기준 v2).
// 효과음 파일마다 '종류별 목표 크기 - 파일의 들리는 크기' 만큼의 재생 배수.
namespace OnlineSeverGame.Client.Juice
{
    public static class JuiceLevels
    {
        public static float Sfx(string file)
        {
            switch (file)
            {
                case "sfx_jump": return 0.2512f; // 목표 -26 dB, 파일 -14.0 dB
                case "sfx_land": return 0.4899f; // 목표 -27 dB, 파일 -20.8 dB
                case "sfx_dash": return 0.3162f; // 목표 -24 dB, 파일 -14.0 dB
                case "sfx_dash_hit": return 0.9884f; // 목표 -19 dB, 파일 -18.9 dB
                case "sfx_stomp": return 0.6996f; // 목표 -20 dB, 파일 -16.9 dB
                case "sfx_crouch": return 0.3057f; // 목표 -29 dB, 파일 -18.7 dB
                case "sfx_death": return 0.7079f; // 목표 -17 dB, 파일 -14.0 dB
                case "sfx_stun": return 0.2818f; // 목표 -25 dB, 파일 -14.0 dB
                case "sfx_immune": return 0.1995f; // 목표 -28 dB, 파일 -14.0 dB
                case "sfx_warn": return 0.4467f; // 목표 -21 dB, 파일 -14.0 dB
                case "sfx_side_warn": return 0.5012f; // 목표 -20 dB, 파일 -14.0 dB
                case "sfx_land_knife": return 0.5449f; // 목표 -22 dB, 파일 -16.7 dB
                case "sfx_land_fork": return 0.5147f; // 목표 -22 dB, 파일 -16.2 dB
                case "sfx_land_icicle": return 0.5056f; // 목표 -22 dB, 파일 -16.1 dB
                case "sfx_land_crate": return 1.0824f; // 목표 -21 dB, 파일 -21.7 dB
                case "sfx_side_pass": return 0.3981f; // 목표 -22 dB, 파일 -14.0 dB
                case "sfx_count": return 0.4467f; // 목표 -21 dB, 파일 -14.0 dB
                case "sfx_go": return 0.5623f; // 목표 -19 dB, 파일 -14.0 dB
                case "sfx_win": return 0.6310f; // 목표 -18 dB, 파일 -14.0 dB
                case "sfx_lose": return 0.5623f; // 목표 -19 dB, 파일 -14.0 dB
                case "sfx_click": return 0.2512f; // 목표 -26 dB, 파일 -14.0 dB
                case "sfx_spring": return 0.4467f; // 목표 -21 dB, 파일 -14.0 dB
                default: return 0.3f;
            }
        }

        // 배경음 파일마다 '목표 크기(A 가중 RMS -20 dB) - 파일 크기' 만큼의 배수(피크 여유 때문에 덜 올린 곡을 채운다).
        public static float Bgm(string file)
        {
            switch (file)
            {
                case "bgm_kitchen": return 1.0000f; // 파일 -20.0 dB
                case "bgm_winter": return 1.0000f; // 파일 -20.0 dB
                case "bgm_warehouse": return 1.4364f; // 파일 -23.1 dB
                case "bgm_dessert": return 1.0000f; // 파일 -20.0 dB
                default: return 1f;
            }
        }
    }
}
