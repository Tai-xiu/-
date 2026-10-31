Shader "Camp/CylindricalPlanks"
{
    Properties
    {
        _MainTex ("Plank Texture", 2D) = "white" {}
        _CircRepeat ("Circumference Repeat", Float) = 2
        _HeightRepeat ("Height Repeat", Float) = 2
        _TopScale ("Top Planar Scale", Float) = 2
        _BoundsMin ("Bounds Min", Vector) = (0,0,0,0)
        _BoundsSize ("Bounds Size", Vector) = (1,1,1,1)
        _TopTint ("Top Tint", Color) = (1,1,1,1)
        _BandColor ("Iron Band Color", Color) = (0.16,0.14,0.13,1)
        _Band1 ("Band1 Height 0-1", Float) = 0.24
        _Band2 ("Band2 Height 0-1", Float) = 0.76
        _BandWidth ("Band Width (0-1 height)", Float) = 0.035
        _RimColor ("Top Rim Color", Color) = (0.18,0.15,0.13,1)
        _RimRadius ("Rim Radius", Float) = 0.40
        _RimWidth ("Rim Width", Float) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        #pragma target 3.0
        sampler2D _MainTex;
        float4 _MainTex_ST;
        float _CircRepeat;
        float _HeightRepeat;
        float _TopScale;
        float4 _BoundsMin;
        float4 _BoundsSize;
        fixed4 _TopTint;
        fixed4 _BandColor;
        float _Band1;
        float _Band2;
        float _BandWidth;
        fixed4 _RimColor;
        float _RimRadius;
        float _RimWidth;

        struct Input
        {
            float2 sideUV;
            float2 topUV;
            float2 topUVN;
            float vertBlend;
            float heightNorm;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 lp = v.vertex.xyz;
            float ang = atan2(lp.y, lp.x);
            float u = (ang / 6.28318530718 + 0.5) * _CircRepeat;
            float vv = (lp.z - _BoundsMin.z) / max(_BoundsSize.z, 1e-4);
            o.heightNorm = vv;
            o.sideUV = float2(u * _MainTex_ST.x + _MainTex_ST.z, vv * _HeightRepeat * _MainTex_ST.y + _MainTex_ST.w);
            float2 tn = float2((lp.x - _BoundsMin.x) / max(_BoundsSize.x, 1e-4), (lp.y - _BoundsMin.y) / max(_BoundsSize.y, 1e-4));
            o.topUVN = tn;
            o.topUV = tn * _TopScale;
            float3 wn = UnityObjectToWorldNormal(v.normal);
            o.vertBlend = saturate(abs(wn.y) * 2.0 - 1.0);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 side = tex2D(_MainTex, IN.sideUV);
            fixed4 top = tex2D(_MainTex, IN.topUV) * _TopTint;
            fixed4 c = lerp(side, top, IN.vertBlend);
            // 铁箍带：只在侧壁
            float d1 = abs(IN.heightNorm - _Band1);
            float d2 = abs(IN.heightNorm - _Band2);
            float band = max(1 - saturate(d1 / max(_BandWidth, 1e-4)), 1 - saturate(d2 / max(_BandWidth, 1e-4)));
            band *= (1.0 - IN.vertBlend);
            c.rgb = lerp(c.rgb, _BandColor.rgb, band);
            // 顶面圆环包边：用归一化坐标，圆心(0.5,0.5)
            float rr = length(IN.topUVN - 0.5);
            float rim = (1 - saturate(abs(rr - _RimRadius) / max(_RimWidth, 1e-4))) * IN.vertBlend;
            c.rgb = lerp(c.rgb, _RimColor.rgb, rim);
            // 顶盖外缘压暗
            float outer = saturate((rr - 0.46) / 0.06) * IN.vertBlend;
            c.rgb *= 1 - 0.35 * outer;
            o.Albedo = c.rgb;
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}