Shader "Camp/ChestPlanks"
{
    Properties
    {
        _MainTex ("Plank Texture", 2D) = "white" {}
        _TopScale ("Top Planar Scale", Float) = 1.0
        _SideScale ("Side Planar Scale", Float) = 2.5
        _BoundsMin ("Bounds Min", Vector) = (0,0,0,0)
        _BoundsSize ("Bounds Size", Vector) = (1,1,1,1)
        _Tint ("Tint", Color) = (1,1,1,1)
        _Band1 ("Iron Band Height 1", Range(0,1)) = 0.22
        _Band2 ("Iron Band Height 2", Range(0,1)) = 0.78
        _BandWidth ("Iron Band Width", Range(0.01,0.3)) = 0.07
        _BandColor ("Iron Band Color", Color) = (0.17,0.17,0.19,1)
        _BottomDark ("Bottom Darken", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        #pragma target 3.0
        sampler2D _MainTex;
        float _TopScale;
        float _SideScale;
        float4 _BoundsMin;
        float4 _BoundsSize;
        fixed4 _Tint;
        float _Band1;
        float _Band2;
        float _BandWidth;
        fixed4 _BandColor;
        float _BottomDark;

        struct Input
        {
            float3 localPosN;
            float3 localNormal;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.localPosN = (v.vertex.xyz - _BoundsMin.xyz) / max(_BoundsSize.xyz, float3(1e-4, 1e-4, 1e-4));
            o.localNormal = v.normal;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 n = normalize(IN.localNormal);
            float2 uvTop = IN.localPosN.xz * _TopScale;
            float2 uvX = float2(IN.localPosN.y, IN.localPosN.z) * _SideScale;
            float2 uvZ = float2(IN.localPosN.y, IN.localPosN.x) * _SideScale;
            fixed3 texTop = tex2D(_MainTex, uvTop).rgb;
            fixed3 texX = tex2D(_MainTex, uvX).rgb;
            fixed3 texZ = tex2D(_MainTex, uvZ).rgb;
            float wTop = saturate((abs(n.y) - 0.8) * 5.0);
            float wx = saturate(abs(n.x) / max(abs(n.x) + abs(n.z), 1e-4));
            fixed3 texSide = lerp(texZ, texX, wx);
            fixed3 texC = lerp(texSide, texTop, wTop);

            // 木板底色（Point过滤贴图自带像素硬边）
            fixed3 c = texC;

            // 铁箍带：仅侧壁，归一化高度域双环，用木纹灰度做金属明暗
            float halfW = _BandWidth * 0.5;
            float dB = min(abs(IN.localPosN.y - _Band1), abs(IN.localPosN.y - _Band2));
            float band = saturate((halfW - dB) * 60.0) * (1.0 - wTop);
            float texGray = dot(texC, fixed3(0.3, 0.5, 0.2));
            fixed3 iron = _BandColor.rgb * (0.82 + 0.36 * texGray);
            c = lerp(c, iron, band);

            // 底缘压暗（贴地泥污感）
            c *= lerp(1.0, 1.0 - _BottomDark, saturate((0.10 - IN.localPosN.y) * 10.0));

            o.Albedo = c * _Tint.rgb;
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
