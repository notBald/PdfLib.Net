sampler2D input : register(s0);   

float4 main(float2 uv : TEXCOORD) : COLOR
{ 
            
    float4 Color;
    Color = tex2D( input, uv.xy);
    Color.rgb = dot(Color.rgb,float3(0.25,0.65,0.1)); 
    return Color;             
}
                