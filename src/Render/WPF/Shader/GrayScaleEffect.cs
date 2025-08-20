using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PdfLib.Res;

namespace PdfLib.Render.WPF.Shader
{
    /// <summary>
    /// From http://rakeshravuri.blogspot.com/2008/08/blending-modes-in-wpf-using.html
    /// 
    /// Only used for testing purposes.
    /// 
    /// See also: http://blogs.msdn.com/b/greg_schechter/archive/2008/09/16/introducing-multi-input-shader-effects.aspx
    /// </summary>
    internal class GrayScaleEffect : ShaderEffect
    {
        public GrayScaleEffect()
        {
            PixelShader = new PixelShader() { UriSource = StrRes.MakeShaderUri("GrayScale.ps") };
            UpdateShaderValue(InputProperty);
        }

        public static readonly DependencyProperty InputProperty = ShaderEffect.RegisterPixelShaderSamplerProperty("Input", typeof(GrayScaleEffect), 0);

        public Brush Input
        {
            get { return (Brush)GetValue(InputProperty); }
            set { SetValue(InputProperty, value); }
        }
    }
}
