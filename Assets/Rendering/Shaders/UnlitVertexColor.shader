Shader "ForgingPrototype/UnlitVertexColor"
{
	Properties
	{
		_BaseColor("Tint", Color) = (1,1,1,1)
		_MainTex("Texture", 2D) = "white" {}
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
			"RenderPipeline" = "UniversalPipeline"
			"IgnoreProjector" = "True"
		}

		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		Cull Off

		Pass
		{
			Name "UnlitVertexColor"
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
				float4 _MainTex_ST;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.color = input.color * _BaseColor;
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
				return tex * input.color;
			}
			ENDHLSL
		}
	}

	Fallback Off
}
