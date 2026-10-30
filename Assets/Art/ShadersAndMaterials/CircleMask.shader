
Shader "Custom/CircleMask"
{
    Properties
    {
        _Color ("Overlay Color", Color) = (0,0,0,1)
        _Center ("Circle Center", Vector) = (0.5,0.5,0,0)
        _Radius ("Circle Radius", Float) = 0.15
        _Softness ("Edge Softness", Float) = 0.02
        _Aspect ("Aspect Ratio", Float) = 1.77778
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = ""
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 _Color;
            float4 _Center;
            float _Radius;
            float _Softness;
            float _Aspect;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 delta = i.uv - _Center.xy;
                delta.x *= _Aspect;

                float distanceToCenter = length(delta);

                float alpha = smoothstep(
                    _Radius,
                    _Radius + max(_Softness, 0.00001),
                    distanceToCenter
                );

                return fixed4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
