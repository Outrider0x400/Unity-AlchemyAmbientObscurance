Shader "Hidden/AmbientObscuranceBlit"
{
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

		Pass
		{
			// This shader blends the obscurance texture with ambient term of the lighting.
			// Requires High Dynmaic Range actived & Multiple Render Target supported
			// I learned this trick from MiniEngineAO (https://github.com/keijiro/MiniEngineAO). I do not claim this idea as mine.
			Blend Zero OneMinusSrcColor, Zero OneMinusSrcAlpha
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _FilteredObscuranceTex;

			v2f_img vert(uint vid : SV_VertexID) {
				const float x = vid == 1u ? 2 : 0;
				const float y = vid > 1u ? 2 : 0;	

				v2f_img o;
				o.pos = float4(x * 2 - 1, 1 - y * 2, 0, 1);
				o.uv = float2(x, y);
				return o;
			}

			void frag(v2f_img i, out float4 ambientBuffer : SV_Target0, out float4 lightingBuffer : SV_Target1) {
				const float ao = 1 - tex2D(_FilteredObscuranceTex, i.uv).r;
				ambientBuffer = float4(0, 0, 0, ao);
				lightingBuffer = float4(ao, ao, ao, 0);
			}

			ENDCG
		}
    }
}
