// 수묵화풍 셰이더 (내장 렌더 파이프라인). M0 기술 검증의 출발점.
//  - 정점색(지형·세력색)을 먹 쪽으로 채도를 낮추고
//  - 빛을 몇 단계로 끊어 붓 터치처럼, 가장자리(윤곽)는 먹으로 짙게
//  - 월드 좌표 노이즈로 종이 결, 멀어지면 안개(종이색)로 번지게
Shader "Sam11/InkWash"
{
    Properties
    {
        _Color ("색", Color) = (1,1,1,1)
        _Bands ("명암 단계", Range(2, 6)) = 3
        _InkEdge ("윤곽 먹 농도", Range(0, 1)) = 0.55
        _Desaturate ("채도 낮춤", Range(0, 1)) = 0.35
        _PaperScale ("종이 결 크기", Float) = 2.5
        _PaperStrength ("종이 결 세기", Range(0, 0.3)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Color;
            float _Bands, _InkEdge, _Desaturate, _PaperScale, _PaperStrength;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(2)
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float hash (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                // 정점색이 없는 메시(기본 도형)는 흰색 정점색이라 _Color만 쓰인다
                float3 baseCol = i.color.rgb * _Color.rgb;
                float lum = dot(baseCol, float3(0.299, 0.587, 0.114));
                baseCol = lerp(baseCol, lum.xxx, _Desaturate);

                float ndl = saturate(dot(n, l));
                float banded = floor(ndl * _Bands + 0.5) / _Bands;
                float3 col = baseCol * lerp(0.62, 1.05, banded) * (0.85 + 0.15 * _LightColor0.rgb);

                float rim = 1 - saturate(dot(n, v));
                col = lerp(col, float3(0.12, 0.11, 0.10), smoothstep(0.6, 0.95, rim) * _InkEdge);

                float grain = noise(i.worldPos.xz * _PaperScale) * 0.6 + noise(i.worldPos.xz * _PaperScale * 4.1) * 0.4;
                col *= 1 + (grain - 0.5) * 2 * _PaperStrength;

                fixed4 o = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
