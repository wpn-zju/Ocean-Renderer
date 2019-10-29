Shader "OceanGPU" {
    Properties {
        _WaterColor ("WaterColor", Color) = (1,1,1,1)
		_Ambient ("Ambient", Range(0,1)) = 0.6
        _Specularity ("Specularity", Range(8,512)) = 20 
        _Refraction ("Refraction (RGB)", 2D) = "white" {}
        _Reflection ("Reflection (RGB)", 2D) = "white" {}
        _Foam ("Foam (RGB)", 2D) = "white" {}
		_FoamSize ("FoamUVSize", Float) = 2
		_FoamColorCut ("Foam Color Cut", Range(0,1)) = 0.3
		_FresnelPower ("Fresnel Power", Range(1,100)) = 5
		_AirRefrIndex ("Air Refr Index", Float) = 1.00
		_WaterRefrIndex ("Water Refr Index", Float) = 1.33
		_DirectTranslucencyPow ("Direct Translucency Pow", Float) = 1.5
		_DirectionalScatteringColor ("Directional Scattering Color", Color) = (0.1, 0.15, 0)
		_ScatteringColorHeightIndex ("Scattering Color Height Index", Float) = 0.1
		_ChoppyScale ("Choppy Scale", Range(0, 2)) = 0.5
    }
    SubShader {
		Pass {
			Tags { "LightMode" = "ForwardBase" }

			CGPROGRAM
			#pragma multi_compile_fwdbase

			#pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
			#include "lighting.cginc"
			#include "AutoLight.cginc"

            struct v2f {
                float4  pos : SV_POSITION;
				float4  worldPos : TEXCOORD0;
                float3  objSpaceNormal : TEXCOORD1;
				float3  worldNormal : TEXCOORD2;
				float4  uv : TEXCOORD3;
				SHADOW_COORDS(4)
			};
			
			// .cs
			uint mapSize;
			float sampleScale;
			Texture2D<float> inputH;
			Texture2D<float> inputDx;
			Texture2D<float> inputDy;
			Texture2D<float2> inputNormal;
			Texture2D<float> foamRT;

			// Properties
			float4 _WaterColor;
			float _Ambient;
			float _Specularity;
			sampler2D _Refraction;
			sampler2D _Reflection;
			sampler2D _Foam;
			float _FoamSize;
			float _FoamColorCut;
			float _FresnelPower;
			float _AirRefrIndex;
			float _WaterRefrIndex;
			float _DirectTranslucencyPow;
			float3 _DirectionalScatteringColor;
			float _ScatteringColorHeightIndex;
			float _ChoppyScale;

			// 顶点采样 Vertex Sampling
			float3 SampleTex(uint2 coord)
			{
				uint x1 = coord.x % mapSize;
				uint y1 = coord.y % mapSize;
				uint2 xy1 = uint2(x1, y1);
				return float3(inputDx[xy1] * _ChoppyScale, inputH[xy1], inputDy[xy1] * _ChoppyScale);
			}

			// 法线采样 Normal Sampling
			float3 SampleNormal(uint2 coord)
			{
				uint x1 = coord.x % mapSize;
				uint y1 = coord.y % mapSize;
				uint2 xy1 = uint2(x1, y1);
				return float3(inputNormal[xy1].x, inputNormal[xy1].y, 1 - inputNormal[xy1].x * inputNormal[xy1].x - inputNormal[xy1].y * inputNormal[xy1].y);
			}

			// Foam 采样 Foam Sampling
			float SampleFoam(uint2 coord)
			{
				uint x1 = coord.x % mapSize;
				uint y1 = coord.y % mapSize;
				uint2 xy1 = uint2(x1, y1);
				return float(foamRT[xy1]);
			}
 
            v2f vert (appdata_tan v) {
				v2f o;

				// 世界空间下原坐标 Original vertex in world space
				float3 worldPosOriginal = mul(unity_ObjectToWorld, v.vertex).xyz;

				// 世界空间下顶点偏移 Vertex offset in world space
				float3 offset = SampleTex(worldPosOriginal.xz * sampleScale);

				// 世界空间修正坐标 Vertex with offset in world space
				o.worldPos.xyz = worldPosOriginal + offset;

				// 裁剪空间顶点坐标 Vertex in clip space
				o.pos = UnityWorldToClipPos(o.worldPos);

				// 世界空间修正法线 Normal with offset in world space
				o.worldNormal = normalize(SampleNormal(worldPosOriginal.xz * sampleScale));

				// 模型空间修正法线 Normal with offset in model space
				o.objSpaceNormal = normalize(mul(o.worldNormal, (float3x3)unity_ObjectToWorld));
				
				// uv
				o.uv.xy = v.texcoord;

				// Foam 计算变量 存放在worldPos的w分量 Temp vars for foam calculating
				o.worldPos.w = SampleFoam(worldPosOriginal.xz * sampleScale);

				// 反射透射相关 Temp vars for reflection and refraction
				float4 proj = float4(o.worldPos.xyz, 1.0);
				float4 clipProj = UnityWorldToClipPos(proj);
				o.uv.zw = 0.5 * clipProj.xy * float2(1, _ProjectionParams.x) / clipProj.w + float2(0.5, 0.5);

				// 计算阴影 Shadow
				TRANSFER_SHADOW(o);

				return o;
            }

			float Fresnel(float3 I, float3 N)
			{
				float R0 = (_AirRefrIndex - _WaterRefrIndex) / (_AirRefrIndex + _WaterRefrIndex);
				R0 *= R0;
				return R0 + (1.0 - R0) * pow((1.0 - saturate(dot(I, N))), _FresnelPower);
			}

			float3 CalculateScatteringColor(float3 lightDir, float3 worldNormal, float3 viewDir, float H, float shadowFactor)
			{
				float lightStrength = sqrt(saturate(lightDir.y));
				float ScattingFactor = pow(saturate(dot(viewDir, lightDir)) + saturate(dot(worldNormal, -lightDir)), _DirectTranslucencyPow);
				return _DirectionalScatteringColor * clamp((ScattingFactor + H * _ScatteringColorHeightIndex) * lightStrength, 0, 1) * shadowFactor;
			}
 
			float4 frag(v2f i) : SV_TARGET{
				// 阴影计算 Shadow
				float shadowFactor = SHADOW_ATTENUATION(i);

				// 阴影修正值 (模拟环境光) Environment
				float shadow = clamp(_Ambient + shadowFactor, 0, 1);

				// 世界空间光线方向 Light direction in world space
				float3 worldLightDir = normalize(_WorldSpaceLightPos0.xyz);

				// 世界空间视线方向 View direction in world space
				float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos.xyz);

				// 反射与折射采样坐标 uv using for reflection and refraction texture sampling
				float2 bumpSampleOffset = (i.objSpaceNormal.xz) * 0.05 + i.uv.zw;

				// 散射光 Scattering color
				float3 scatteringColor = CalculateScatteringColor(worldLightDir, i.worldNormal, -viewDir, i.worldPos.y, shadow);

				// 菲涅尔效应 Fresnel
				float fresnelTerm = Fresnel(normalize(viewDir), i.worldNormal);
				float3 reflection = tex2D(_Reflection, bumpSampleOffset);
				float3 refraction = lerp(tex2D(_Refraction, bumpSampleOffset), _WaterColor, 0.7) + scatteringColor;

				// 高光分量 Specular
				float3 specular = pow(max(0.0, dot(reflect(-worldLightDir, normalize(i.worldNormal)), viewDir)), _Specularity) * shadowFactor * _LightColor0.rgb;

				// 泡沫计算 Foam
				float _foam = tex2D(_Foam, i.uv.xy * _FoamSize).r;
				float foam = clamp(_foam - _FoamColorCut, 0.0, 1.0);
				foam *= clamp(i.worldPos.w, 0.0, 1.0);

				shadow = 1.0; // Aug.24

				// 颜色混合 Color mixing
				return float4((lerp(refraction, reflection, fresnelTerm) + specular + foam) * shadow, 1.0);
			}
            ENDCG
        }
    }
	Fallback "VertexLit"
}