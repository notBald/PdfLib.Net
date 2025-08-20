sampler2D input : register(s0);   

float4 main(float2 uv : TEXCOORD) : COLOR
{ 
            
    float4 Color;
    Color = tex2D( input, uv.xy);
    //Color.rgb = 1-Color.rgb; 
    return Color;             
}
                