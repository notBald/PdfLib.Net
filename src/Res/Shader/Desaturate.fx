float Script : STANDARDSGLOBAL <
    string UIWidget = "none";
    string ScriptClass = "scene";
    string ScriptOrder = "postprocess";
    string ScriptOutput = "color";
    string Script = "Technique=Main;";
> = 0.8;

#ifndef WPF
float Saturation <
    string UIWidget = "slider";
    float UIMin = 0.0f;
    float UIMax = 1.0f;
    float UIStep = 0.01f;
    string UIName = "Saturation";
> = 0.5f;
#else
float Saturation : register(c0);
#endif


texture implicitTexture : RENDERCOLORTARGET < 
    float2 ViewPortRatio = {1.0,1.0}; 
    int MipLevels = 1; 
    string Format = "A8R8G8B8" ; 
    string UIWidget = "None"; 
>; 
#ifndef WPF
sampler2D implicitSampler = sampler_state { 
    texture = implicitTexture; 
    AddressU = Clamp; 
    AddressV = Clamp; 
    MagFilter = Linear; 
    MipFilter = POINT; 
    MinFilter = LINEAR; 
    MagFilter = LINEAR; 
};
#else
sampler2D implicitSampler : register(s0);
#endif


texture zBuffer : RENDERDEPTHSTENCILTARGET < 
    float2 ViewPortRatio = {1.0,1.0}; 
    string Format = "D24S8"; 
    string UIWidget = "None"; 
>; 

struct VSOut {
    float4 Pos	: POSITION;
    float2 UV	: TEXCOORD0;
};

VSOut DesaturateVS (
    float3 Position : POSITION, 
    float3 TexCoord : TEXCOORD0 ) {
    VSOut output;
    output.Pos 	= float4(Position, 1.0);
    output.UV 	= TexCoord.xy;
    return output;
}


float4 DesaturatePS(float2 uv : TEXCOORD0) : COLOR {
	float3  LuminanceWeights = float3(0.299,0.587,0.114);
    float4	srcPixel = tex2D(implicitSampler, uv);
    float	luminance = dot(srcPixel,LuminanceWeights);
    float4	dstPixel = lerp(luminance,srcPixel,Saturation);
    //retain the incoming alpha
	dstPixel.a = srcPixel.a;
    return dstPixel;
}

float4 	clearColour 	= {0,0,0,0};
float 	clearDepth  	= 1.0;
technique Main < string Script =
    "RenderColorTarget0=implicitTexture;"
    "RenderDepthStencilTarget=zBuffer;"
    "ClearSetColor=clearColour;"
    "ClearSetDepth=clearDepth;"
	"Clear=Color;"
	"Clear=Depth;"
    "ScriptExternal=color;"
    "Pass=PostP0;";
> {
    pass PostP0 < string Script =
	"RenderColorTarget0=;"
	"RenderDepthStencilTarget=;"
	"Draw=Buffer;";
    > {
	VertexShader = compile vs_2_0 DesaturateVS();
		ZEnable = false;
		ZWriteEnable = false;
		AlphaBlendEnable = false;
		CullMode = None;
	PixelShader = compile ps_2_0 DesaturatePS();
    }
}
