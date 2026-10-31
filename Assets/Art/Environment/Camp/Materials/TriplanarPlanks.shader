Shader "Camp/TriplanarPlanks"
{
    Properties
    {
        _MainTex ("Plank Texture", 2D) = "white" {}
        _TopScale ("Top Planar Scale", Float) = 1.5
        _SideScale ("Side Planar Scale", Float) = 4
        _BoundsMin ("Bounds Min", Vector) = (0,0,0,0)
        _BoundsSize ("Bounds Size", Vector) = (1,1,1,1)
        _Tint ("Tint", Color) = (1,1,1,1)
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
            float2 uvTop = IN.localPosN.xz * _TopScale;              // 顶/底：横板
            float2 uvX = float2(IN.localPosN.y, IN.localPosN.z) * _SideScale;  // ±X面：竖板
            float2 uvZ = float2(IN.localPosN.y, IN.localPosN.x) * _SideScale;  // ±Z面：竖板
            fixed3 cTop = tex2D(_MainTex, uvTop).rgb;
            fixed3 cX = tex2D(_MainTex, uvX).rgb;
            fixed3 cZ = tex2D(_MainTex, uvZ).rgb;
            float wTop = saturate((abs(n.y) - 0.8) * 5.0);
            float wx = saturate(abs(n.x) / max(abs(n.x) + abs(n.z), 1e-4));
            fixed3 cSide = lerp(cZ, cX, wx);
            fixed3 c = lerp(cSide, cTop, wTop) * _Tint.rgb;
            o.Albedo = c;
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}